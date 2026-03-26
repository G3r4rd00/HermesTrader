using System.Net.Http.Json;
using System.Text.Json;
using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

namespace HermesTrade.Data;

/// <summary>
/// Downloads historical kline (OHLCV) data from the Binance public REST API.
///
/// Endpoint: GET https://api.binance.com/api/v3/klines
/// No API key required.
/// </summary>
public sealed class BinanceDataProvider : IMarketDataProvider
{
    private const string BaseUrl    = "https://api.binance.com";
    private const string ProviderName = "binance";

    // Binance returns at most 1 000 candles per request for the klines endpoint.
    private const int MaxCandlesPerRequest = 1_000;

    private readonly HttpClient       _httpClient;
    private readonly FileCacheService _cache;

    public BinanceDataProvider(HttpClient httpClient, FileCacheService cache)
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
        var startMs    = ToUnixMilliseconds(from);
        var endMs      = ToUnixMilliseconds(to);

        // Paginate – fetch up to MaxCandlesPerRequest at a time
        while (startMs < endMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}/api/v3/klines"
                    + $"?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}"
                    + $"&interval=1d"
                    + $"&startTime={startMs}"
                    + $"&endTime={endMs}"
                    + $"&limit={MaxCandlesPerRequest}";

            var raw = await _httpClient
                .GetFromJsonAsync<JsonElement[][]>(url, cancellationToken)
                .ConfigureAwait(false);

            if (raw is null || raw.Length == 0)
                break;

            foreach (var row in raw)
                allCandles.Add(ParseKline(row));

            // The open time of the last candle returned
            var lastOpenMs = raw[^1][0].GetInt64();
            if (raw.Length < MaxCandlesPerRequest)
                break; // no more pages

            startMs = lastOpenMs + 1; // exclusive start for next page
        }

        if (allCandles.Count > 0)
            await _cache.SaveAsync(ProviderName, symbol, from, to, allCandles, cancellationToken)
                        .ConfigureAwait(false);

        return allCandles;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Candle ParseKline(JsonElement[] row)
    {
        // Binance kline array layout:
        // [0]  open time (ms)
        // [1]  open
        // [2]  high
        // [3]  low
        // [4]  close
        // [5]  volume
        var openTimeMs = row[0].GetInt64();
        var timestamp  = DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime;

        return new Candle(
            timestamp,
            decimal.Parse(row[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(row[2].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(row[3].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(row[4].GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(row[5].GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static long ToUnixMilliseconds(DateTime dt) =>
        new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
