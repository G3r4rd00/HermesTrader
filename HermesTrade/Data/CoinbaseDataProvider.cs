using System.Net.Http.Json;
using System.Text.Json;
using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

namespace HermesTrade.Data;

/// <summary>
/// Downloads historical candle data from the Coinbase Advanced Trade public REST API.
///
/// Endpoint: GET https://api.exchange.coinbase.com/products/{productId}/candles
/// No API key required.
/// </summary>
public sealed class CoinbaseDataProvider : IMarketDataProvider
{
    private const string BaseUrl      = "https://api.exchange.coinbase.com";
    private const string ProviderName = "coinbase";

    // Coinbase returns at most 300 candles per request
    private const int MaxCandlesPerRequest = 300;

    // Granularity in seconds – 86400 = 1 day
    private const int GranularitySeconds = 86_400;

    private readonly HttpClient       _httpClient;
    private readonly FileCacheService _cache;

    public CoinbaseDataProvider(HttpClient httpClient, FileCacheService cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache      = cache      ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Candle>> GetHistoricalDataAsync(
        string            symbol,
        DateTime          from,
        DateTime          to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        // Try cache first
        var cached = await _cache.TryLoadAsync(ProviderName, symbol, from, to, cancellationToken)
                                 .ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var allCandles = new List<Candle>();
        var window     = TimeSpan.FromSeconds(GranularitySeconds * MaxCandlesPerRequest);
        var current    = from;

        // Paginate by splitting the date range into MaxCandlesPerRequest-sized windows
        while (current < to)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var windowEnd = current + window;
            if (windowEnd > to) windowEnd = to;

            var url = $"{BaseUrl}/products/{Uri.EscapeDataString(symbol.ToUpperInvariant())}/candles"
                    + $"?granularity={GranularitySeconds}"
                    + $"&start={current:O}"
                    + $"&end={windowEnd:O}";

            var raw = await _httpClient
                .GetFromJsonAsync<JsonElement[][]>(url, cancellationToken)
                .ConfigureAwait(false);

            if (raw is null || raw.Length == 0)
                break;

            // Coinbase returns results newest-first; iterate in reverse to get ascending order
            for (int j = raw.Length - 1; j >= 0; j--)
                allCandles.Add(ParseCandle(raw[j]));

            current = windowEnd;
        }

        // Deduplicate (timestamps can overlap at window boundaries) and sort
        var sorted = allCandles
            .GroupBy(c => c.Timestamp)
            .Select(g => g.First())
            .OrderBy(c => c.Timestamp)
            .ToList();

        if (sorted.Count > 0)
            await _cache.SaveAsync(ProviderName, symbol, from, to, sorted, cancellationToken)
                        .ConfigureAwait(false);

        return sorted;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Candle ParseCandle(JsonElement[] row)
    {
        // Coinbase candle array layout: [time, low, high, open, close, volume]
        var unixSeconds = row[0].GetInt64();
        var timestamp   = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        return new Candle(
            timestamp,
            open:   row[3].GetDecimal(),
            high:   row[2].GetDecimal(),
            low:    row[1].GetDecimal(),
            close:  row[4].GetDecimal(),
            volume: row[5].GetDecimal());
    }
}
