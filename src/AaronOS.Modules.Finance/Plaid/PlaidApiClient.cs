using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AaronOS.Modules.Finance.Plaid;

file record LinkTokenUserDto([property: JsonPropertyName("client_user_id")] string ClientUserId);

file record CreateLinkTokenRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("secret")] string Secret,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("products")] List<string> Products,
    [property: JsonPropertyName("country_codes")] List<string> CountryCodes,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("user")] LinkTokenUserDto User);

file record ExchangeTokenRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("secret")] string Secret,
    [property: JsonPropertyName("public_token")] string PublicToken);

file record AccountsGetRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("secret")] string Secret,
    [property: JsonPropertyName("access_token")] string AccessToken);

file record InstitutionGetByIdRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("secret")] string Secret,
    [property: JsonPropertyName("institution_id")] string InstitutionId,
    [property: JsonPropertyName("country_codes")] List<string> CountryCodes);

file record TransactionsSyncRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("secret")] string Secret,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("cursor")] string? Cursor);

/// <summary>
/// Thin HTTP client for the four Plaid endpoints this app needs. Not a general-purpose Plaid SDK —
/// deliberately minimal. Owns a single static HttpClient (this app doesn't register
/// IHttpClientFactory anywhere else, and one long-lived client is enough for a single-process
/// desktop app).
/// </summary>
public class PlaidApiClient(PlaidCredentialStore credentialStore)
{
    private static readonly HttpClient Http = new();
    private const string ClientUserId = "aaronos-user";

    private string BaseUrl(PlaidEnvironment environment) => environment switch
    {
        PlaidEnvironment.Sandbox => "https://sandbox.plaid.com",
        PlaidEnvironment.Production => "https://production.plaid.com",
        _ => throw new ArgumentOutOfRangeException(nameof(environment))
    };

    private PlaidCredentials RequireCredentials()
    {
        var credentials = credentialStore.Load();
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.ActiveSecret))
        {
            throw new InvalidOperationException("No Plaid credentials configured for the active environment.");
        }

        return credentials;
    }

    public async Task<string> CreateLinkTokenAsync()
    {
        var credentials = RequireCredentials();
        var request = new CreateLinkTokenRequest(
            credentials.ClientId,
            credentials.ActiveSecret!,
            "AaronOS",
            ["transactions"],
            ["US"],
            "en",
            new LinkTokenUserDto(ClientUserId));

        var response = await Http.PostAsJsonAsync($"{BaseUrl(credentials.Environment)}/link/token/create", request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PlaidLinkTokenResponse>();
        return body!.LinkToken;
    }

    public async Task<PlaidExchangeTokenResponse> ExchangePublicTokenAsync(string publicToken)
    {
        var credentials = RequireCredentials();
        var request = new ExchangeTokenRequest(credentials.ClientId, credentials.ActiveSecret!, publicToken);
        var response = await Http.PostAsJsonAsync($"{BaseUrl(credentials.Environment)}/item/public_token/exchange", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlaidExchangeTokenResponse>())!;
    }

    public async Task<PlaidAccountsGetResponse> GetAccountsAsync(string accessToken)
    {
        var credentials = RequireCredentials();
        var request = new AccountsGetRequest(credentials.ClientId, credentials.ActiveSecret!, accessToken);
        var response = await Http.PostAsJsonAsync($"{BaseUrl(credentials.Environment)}/accounts/get", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlaidAccountsGetResponse>())!;
    }

    public async Task<string> GetInstitutionNameAsync(string institutionId)
    {
        var credentials = RequireCredentials();
        var request = new InstitutionGetByIdRequest(credentials.ClientId, credentials.ActiveSecret!, institutionId, ["US"]);
        var response = await Http.PostAsJsonAsync($"{BaseUrl(credentials.Environment)}/institutions/get_by_id", request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PlaidInstitutionGetByIdResponse>();
        return body!.Institution.Name;
    }

    /// <summary>Loops transactions/sync until has_more is false, accumulating every page.</summary>
    public async Task<PlaidSyncResult> SyncTransactionsAsync(string accessToken, string? cursor)
    {
        var credentials = RequireCredentials();
        var added = new List<PlaidTransactionDto>();
        var modified = new List<PlaidTransactionDto>();
        var removedIds = new List<string>();
        var nextCursor = cursor;

        while (true)
        {
            var request = new TransactionsSyncRequest(credentials.ClientId, credentials.ActiveSecret!, accessToken, nextCursor);
            var response = await Http.PostAsJsonAsync($"{BaseUrl(credentials.Environment)}/transactions/sync", request);
            response.EnsureSuccessStatusCode();
            var page = (await response.Content.ReadFromJsonAsync<PlaidTransactionsSyncResponse>())!;

            added.AddRange(page.Added);
            modified.AddRange(page.Modified);
            removedIds.AddRange(page.Removed.Select(r => r.TransactionId));
            nextCursor = page.NextCursor;

            if (!page.HasMore)
            {
                break;
            }
        }

        return new PlaidSyncResult(added, modified, removedIds, nextCursor!);
    }
}
