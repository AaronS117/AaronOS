using System.Net.Http;
using System.Net.Http.Json;

namespace AaronOS.Modules.Medical.Withings;

public class WithingsApiException(int status, string? error, string what)
    : Exception($"Withings rejected {what}: status {status}{(string.IsNullOrWhiteSpace(error) ? "" : $" — {error}")}")
{
    public int Status { get; } = status;
}

public static class WithingsEnvelopeExtensions
{
    /// <summary>
    /// Unwraps a Withings envelope, treating any non-zero status as failure. Separated out so the
    /// error handling is testable without an HTTP round trip.
    /// </summary>
    public static T Require<T>(this WithingsEnvelope<T>? envelope, string what)
    {
        if (envelope is null)
        {
            throw new WithingsApiException(-1, "empty response", what);
        }

        if (envelope.Status != 0 || envelope.Body is null)
        {
            throw new WithingsApiException(envelope.Status, envelope.Error, what);
        }

        return envelope.Body;
    }
}

/// <summary>
/// Thin client for the two Withings endpoints this app needs: the OAuth token exchange and the sleep
/// summary. Not a general-purpose SDK.
///
/// Three things about this API are not standard OAuth2 and are the usual source of wasted hours:
/// there is no /token endpoint (token operations are an <c>action</c> parameter on a normal API
/// path), every response returns HTTP 200 with the real outcome in a <c>status</c> field, and the
/// refresh token rotates on every refresh so it must be persisted each time.
/// </summary>
public class WithingsApiClient(WithingsCredentialStore credentialStore)
{
    private static readonly HttpClient Http = new();

    private const string AuthorizeUrl = "https://account.withings.com/oauth2_user/authorize2";
    private const string TokenUrl = "https://wbsapi.withings.net/v2/oauth2";
    private const string SleepUrl = "https://wbsapi.withings.net/v2/sleep";

    /// <summary>
    /// Withings only accepts HTTPS redirect URLs, which a desktop app cannot serve. Registering their
    /// own API URL is the documented way round it: the browser lands there after consent and the
    /// authorization code sits in the address bar for the user to copy back. Avoids running a local
    /// HTTP listener and the firewall prompt that comes with it.
    /// </summary>
    public const string RedirectUri = "https://wbsapi.withings.net/v2/oauth2";

    /// <summary>
    /// Scopes proven sufficient for the sleep mat's nightly summaries. <c>user.sleepevents</c> also
    /// exists and covers raw in-night events, which this app does not read.
    /// </summary>
    private const string Scope = "user.info,user.metrics,user.activity";

    private const string SleepDataFields =
        "total_sleep_time,lightsleepduration,deepsleepduration,remsleepduration,wakeupduration," +
        "durationtosleep,wakeupcount,sleep_score,hr_average,hr_min,hr_max,rr_average," +
        "breathing_disturbances_intensity,snoring,snoringepisodecount";

    public WithingsCredentials RequireCredentials()
    {
        var credentials = credentialStore.Load();
        if (credentials is null || !credentials.HasAppCredentials)
        {
            throw new InvalidOperationException(
                "No Withings client ID and secret saved yet. Add them on the Sleep page first.");
        }

        return credentials;
    }

    public string BuildAuthorizeUrl(string state)
    {
        var credentials = RequireCredentials();
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = credentials.ClientId,
            ["state"] = state,
            ["scope"] = Scope,
            ["redirect_uri"] = RedirectUri
        };

        return $"{AuthorizeUrl}?{string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"))}";
    }

    /// <summary>Exchanges the pasted authorization code for tokens and saves them.</summary>
    public async Task ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        var credentials = RequireCredentials();

        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["action"] = "requesttoken",
            ["grant_type"] = "authorization_code",
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["code"] = code.Trim(),
            ["redirect_uri"] = RedirectUri
        }, "the authorization code", ct);

        credentials.ApplyToken(token, DateTimeOffset.UtcNow);
        credentialStore.Save(credentials);
    }

    /// <summary>
    /// Returns a usable access token, refreshing first if the current one is stale. Saves immediately
    /// after refreshing, because the refresh token that came back has already replaced the one on
    /// disk — losing it means the next sync cannot authenticate.
    /// </summary>
    public async Task<string> EnsureAccessTokenAsync(CancellationToken ct = default)
    {
        var credentials = RequireCredentials();
        var now = DateTimeOffset.UtcNow;

        if (credentials.HasFreshAccessToken(now))
        {
            return credentials.AccessToken!;
        }

        if (!credentials.IsAuthorized)
        {
            throw new InvalidOperationException(
                "Withings is not connected yet. Authorize the app on the Sleep page first.");
        }

        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["action"] = "requesttoken",
            ["grant_type"] = "refresh_token",
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["refresh_token"] = credentials.RefreshToken!
        }, "the refresh token", ct);

        credentials.ApplyToken(token, now);
        credentialStore.Save(credentials);
        return credentials.AccessToken!;
    }

    private async Task<WithingsTokenBody> PostTokenAsync(
        Dictionary<string, string> form, string what, CancellationToken ct)
    {
        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content
            .ReadFromJsonAsync<WithingsEnvelope<WithingsTokenBody>>(WithingsJson.Options, ct);

        return envelope.Require(what);
    }

    /// <summary>
    /// Every sleep period whose data falls in the range, following the API's offset paging. Dates are
    /// inclusive and interpreted by Withings in the account's own timezone.
    /// </summary>
    public async Task<List<WithingsSleepSeries>> GetSleepSummariesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var all = new List<WithingsSleepSeries>();
        var offset = 0;

        // Bounded rather than while(true): a server that always reports "more" would otherwise spin
        // forever. 40 pages is far more than a few years of nights.
        for (var page = 0; page < 40; page++)
        {
            var token = await EnsureAccessTokenAsync(ct);

            var form = new Dictionary<string, string>
            {
                ["action"] = "getsummary",
                ["startdateymd"] = from.ToString("yyyy-MM-dd"),
                ["enddateymd"] = to.ToString("yyyy-MM-dd"),
                ["data_fields"] = SleepDataFields
            };

            if (offset > 0)
            {
                form["offset"] = offset.ToString();
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, SleepUrl)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<WithingsEnvelope<WithingsSleepSummaryBody>>(WithingsJson.Options, ct);

            var body = envelope.Require("the sleep summary request");
            if (body.Series is { Count: > 0 })
            {
                all.AddRange(body.Series);
            }

            if (!body.More)
            {
                break;
            }

            offset = body.Offset;
        }

        return all;
    }
}
