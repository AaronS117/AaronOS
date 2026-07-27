namespace AaronOS.Modules.Medical.Withings;

/// <summary>
/// Withings app credentials plus the current OAuth grant. Persisted only via
/// <see cref="WithingsCredentialStore"/>, which DPAPI-encrypts it at rest — never construct this
/// from a hardcoded literal.
/// </summary>
public class WithingsCredentials
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public string? UserId { get; set; }

    public bool HasAppCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public bool IsAuthorized => !string.IsNullOrWhiteSpace(RefreshToken);

    /// <summary>
    /// Access tokens last three hours. Treated as stale a minute early so a token cannot expire
    /// between this check and the request that uses it.
    /// </summary>
    public bool HasFreshAccessToken(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        AccessTokenExpiresAt is { } expiry &&
        expiry > now.AddMinutes(1);

    /// <summary>
    /// Folds a token response into these credentials.
    ///
    /// The important part is the refresh token. Withings rotates it on every refresh and invalidates
    /// the old one immediately, so failing to persist the new value breaks the *next* sync rather
    /// than this one — a failure that looks like an unrelated bug days later. When a response omits
    /// the refresh token the existing one is kept rather than nulled.
    /// </summary>
    public void ApplyToken(WithingsTokenBody token, DateTimeOffset now)
    {
        AccessToken = token.AccessToken;
        AccessTokenExpiresAt = now.AddSeconds(token.ExpiresIn);

        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            RefreshToken = token.RefreshToken;
        }

        if (!string.IsNullOrWhiteSpace(token.UserId))
        {
            UserId = token.UserId;
        }
    }
}
