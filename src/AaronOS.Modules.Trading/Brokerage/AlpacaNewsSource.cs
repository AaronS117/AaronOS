using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AaronOS.Modules.Trading.Agent;

namespace AaronOS.Modules.Trading.Brokerage;

file record NewsItemDto(
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("symbols")] List<string>? Symbols);

file record NewsResponseDto(
    [property: JsonPropertyName("news")] List<NewsItemDto>? News);

/// <summary>
/// Headlines from Alpaca for the live run.
///
/// Asks for the same window the replay serves, so a live decision and a replayed one see comparable
/// amounts of text. A failure returns nothing rather than throwing: news is an input to a judgement, not
/// a precondition for trading, and a news outage should not stop a cycle.
/// </summary>
public class AlpacaNewsSource(TradingCredentialStore credentialStore) : INewsSource
{
    private const string DataHost = "https://data.alpaca.markets";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<NewsHeadline>> AsOfAsync(
        IEnumerable<string> symbols, DateOnly session, CancellationToken token = default)
    {
        var credentials = credentialStore.Load();
        if (credentials is null || credentials.AlpacaKeyId.Length == 0)
        {
            return [];
        }

        var list = symbols.Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var url = $"{DataHost}/v1beta1/news?symbols={string.Join(',', list)}" +
                  $"&start={NewsWindow.EarliestUtc(session):yyyy-MM-ddTHH:mm:ssZ}" +
                  $"&end={NewsWindow.CutoffUtc(session):yyyy-MM-ddTHH:mm:ssZ}" +
                  $"&limit={NewsWindow.MaxHeadlines}&sort=desc";

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Add("APCA-API-KEY-ID", credentials.AlpacaKeyId);
            message.Headers.Add("APCA-API-SECRET-KEY", credentials.AlpacaSecret);

            using var response = await Http.SendAsync(message, token);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var dto = await response.Content.ReadFromJsonAsync<NewsResponseDto>(token);
            return (dto?.News ?? [])
                .Where(n => n.CreatedAt <= NewsWindow.CutoffUtc(session))
                .OrderBy(n => n.CreatedAt)
                .Select(n => new NewsHeadline(
                    n.CreatedAt, string.Join(',', n.Symbols ?? []), n.Headline))
                .ToList();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }
}
