using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Brokerage;

// Alpaca returns every monetary field as a JSON string rather than a number, so these DTOs take
// strings and parse explicitly. Binding them to decimal directly fails at runtime.
file record AccountDto(
    [property: JsonPropertyName("equity")] string Equity,
    [property: JsonPropertyName("cash")] string Cash,
    [property: JsonPropertyName("status")] string Status);

file record PositionDto(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("qty")] string Qty,
    [property: JsonPropertyName("market_value")] string MarketValue,
    [property: JsonPropertyName("avg_entry_price")] string AvgEntryPrice,
    [property: JsonPropertyName("unrealized_pl")] string UnrealizedPl);

file record ClockDto([property: JsonPropertyName("is_open")] bool IsOpen);

file record OrderDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("filled_avg_price")] string? FilledAvgPrice,
    [property: JsonPropertyName("filled_at")] DateTime? FilledAt);

file record PlaceOrderRequest(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("qty")] string Qty,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("time_in_force")] string TimeInForce);

file record QuoteDto(
    [property: JsonPropertyName("ap")] decimal AskPrice,
    [property: JsonPropertyName("bp")] decimal BidPrice);

file record LatestQuotesDto(
    [property: JsonPropertyName("quotes")] Dictionary<string, QuoteDto>? Quotes);

file record BarDto(
    [property: JsonPropertyName("c")] decimal Close,
    [property: JsonPropertyName("t")] DateTime Timestamp);

file record BarsDto(
    [property: JsonPropertyName("bars")] Dictionary<string, List<BarDto>>? Bars);

/// <summary>The broker's own view of the account, before any of it is written down.</summary>
public readonly record struct BrokerAccount(decimal Equity, decimal Cash, string Status);

public readonly record struct SubmittedOrder(string BrokerOrderId, string Status);

/// <summary>Mid-price and spread for one symbol.</summary>
public readonly record struct SymbolQuote(string Symbol, decimal Bid, decimal Ask)
{
    public decimal Mid => (Bid + Ask) / 2m;
}

public class AlpacaApiException(string message) : Exception(message);

/// <summary>
/// The handful of Alpaca endpoints this module needs, and no more.
///
/// Paper and live differ only in the trading host, which is why proving a strategy on paper and then
/// deciding about real money is a configuration change rather than a rewrite. The live host is named
/// here for completeness; nothing in this module selects it.
/// </summary>
public class AlpacaClient(TradingCredentialStore credentialStore)
{
    private const string PaperHost = "https://paper-api.alpaca.markets";
    private const string LiveHost = "https://api.alpaca.markets";
    private const string DataHost = "https://data.alpaca.markets";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public bool IsConfigured => credentialStore.Load() is { AlpacaKeyId.Length: > 0, AlpacaSecret.Length: > 0 };

    public async Task<BrokerAccount> GetAccountAsync(CancellationToken token = default)
    {
        var dto = await GetAsync<AccountDto>(TradingHost() + "/v2/account", token);
        return new BrokerAccount(ParseMoney(dto.Equity), ParseMoney(dto.Cash), dto.Status);
    }

    public async Task<bool> IsMarketOpenAsync(CancellationToken token = default) =>
        (await GetAsync<ClockDto>(TradingHost() + "/v2/clock", token)).IsOpen;

    public async Task<List<HeldPosition>> GetPositionsAsync(CancellationToken token = default)
    {
        var dtos = await GetAsync<List<PositionDto>>(TradingHost() + "/v2/positions", token);
        return dtos
            .Select(p => new HeldPosition(
                p.Symbol,
                (int)Math.Floor(ParseMoney(p.Qty)),
                ParseMoney(p.MarketValue)))
            .ToList();
    }

    /// <summary>
    /// Latest quotes for the given symbols. Symbols the feed does not return are simply absent from
    /// the result rather than defaulted to zero, so a missing price is refused by the guardrails
    /// instead of being treated as free.
    /// </summary>
    public async Task<Dictionary<string, SymbolQuote>> GetQuotesAsync(
        IEnumerable<string> symbols, CancellationToken token = default)
    {
        var list = symbols.Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var url = $"{DataHost}/v2/stocks/quotes/latest?symbols={string.Join(',', list)}";
        var dto = await GetAsync<LatestQuotesDto>(url, token);

        return (dto.Quotes ?? [])
            .Where(kv => kv.Value.AskPrice > 0 || kv.Value.BidPrice > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => new SymbolQuote(kv.Key, kv.Value.BidPrice, kv.Value.AskPrice),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Most recent daily close for one symbol, used to stamp the benchmark each day.</summary>
    public async Task<decimal?> GetLatestDailyCloseAsync(string symbol, CancellationToken token = default)
    {
        var start = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = $"{DataHost}/v2/stocks/bars?symbols={symbol}&timeframe=1Day&start={start}&limit=10";
        var dto = await GetAsync<BarsDto>(url, token);

        if (dto.Bars is null || !dto.Bars.TryGetValue(symbol, out var bars) || bars.Count == 0)
        {
            return null;
        }

        return bars.OrderBy(b => b.Timestamp).Last().Close;
    }

    public async Task<SubmittedOrder> PlaceMarketOrderAsync(
        string symbol, OrderSide side, int quantity, CancellationToken token = default)
    {
        var request = new PlaceOrderRequest(
            symbol.ToUpperInvariant(),
            quantity.ToString(CultureInfo.InvariantCulture),
            side == OrderSide.Buy ? "buy" : "sell",
            "market",
            "day");

        using var message = new HttpRequestMessage(HttpMethod.Post, TradingHost() + "/v2/orders")
        {
            Content = JsonContent.Create(request),
        };
        AddAuth(message);

        using var response = await Http.SendAsync(message, token);
        await EnsureSuccessAsync(response, token);

        var dto = await response.Content.ReadFromJsonAsync<OrderDto>(token)
                  ?? throw new AlpacaApiException("Alpaca accepted the order but returned no body.");
        return new SubmittedOrder(dto.Id, dto.Status);
    }

    /// <summary>Current status and fill price of an order, for reconciling a stored row.</summary>
    public async Task<(string Status, decimal? FilledPrice, DateTime? FilledAtUtc)> GetOrderAsync(
        string brokerOrderId, CancellationToken token = default)
    {
        var dto = await GetAsync<OrderDto>(TradingHost() + "/v2/orders/" + brokerOrderId, token);
        var filled = string.IsNullOrWhiteSpace(dto.FilledAvgPrice) ? null : (decimal?)ParseMoney(dto.FilledAvgPrice);
        return (dto.Status, filled, dto.FilledAt?.ToUniversalTime());
    }

    private string TradingHost() => credentialStore.Load()?.AlpacaLive == true ? LiveHost : PaperHost;

    private async Task<T> GetAsync<T>(string url, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuth(message);

        using var response = await Http.SendAsync(message, token);
        await EnsureSuccessAsync(response, token);

        return await response.Content.ReadFromJsonAsync<T>(token)
               ?? throw new AlpacaApiException($"Alpaca returned an empty body for {url}.");
    }

    private void AddAuth(HttpRequestMessage message)
    {
        var credentials = credentialStore.Load()
            ?? throw new AlpacaApiException("No Alpaca keys saved. Add them in Settings.");

        message.Headers.Add("APCA-API-KEY-ID", credentials.AlpacaKeyId);
        message.Headers.Add("APCA-API-SECRET-KEY", credentials.AlpacaSecret);
    }

    /// <summary>
    /// Surfaces Alpaca's own message rather than a bare status code. Its rejections say exactly what
    /// was wrong ("insufficient buying power", "asset not tradable"), and losing that in favour of
    /// "403 Forbidden" turns a five-second fix into a debugging session.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(token);
        throw new AlpacaApiException($"Alpaca returned {(int)response.StatusCode}: {Truncate(body, 400)}");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
}
