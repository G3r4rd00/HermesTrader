using System.Text.RegularExpressions;
using HermesTrade.Core.Models;
using HermesTrade.Interfaces;
using NodaTime;
using YahooQuotesApi;



namespace HermesTrade.Data;

/// <summary>
/// Downloads historical daily OHLCV data from Yahoo Finance using the YahooQuotesApi library.
///
/// Supports:
/// - Stocks (e.g., "AAPL", "MSFT", "TSLA")
/// - Indices (e.g., "^GSPC" for S&amp;P 500, "^DJI" for Dow Jones)
/// - Cryptocurrencies (e.g., "BTC-USD", "ETH-USD")
/// - ETFs and more
///
/// Forex pairs (e.g., "EURUSD=X") are not supported: YahooQuotesApi does not return
/// their history. Prices are in the instrument's own currency.
///
/// Each candle is stamped at 00:00 UTC of its trading date in the exchange's time zone,
/// and <c>from</c>/<c>to</c> are compared by calendar date (inclusive, time of day ignored).
///
/// By default prices are split-adjusted but not dividend-adjusted, so results are a price
/// return only. Pass <c>useDividendAdjustedPrices: true</c> to approximate total return.
///
/// No API key required. Free to use with reasonable rate limits.
/// </summary>
public sealed class YahooFinanceDataProvider : IMarketDataProvider
{
    // "v2": candles stamped at the local trading date (older cache files used session-open instants)
    private const string ProviderName = "yahoo-finance-v2";

    // "XXXUSDT", "XXX-USDT" or "XXX/USDT" exchange-style pairs
    private static readonly Regex UsdtPair =
        new(@"^([A-Z0-9]{2,10}?)[-/]?USDT$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly FileCacheService _cache;
    private readonly bool _useDividendAdjustedPrices;

    /// <summary>
    /// Creates a new Yahoo Finance data provider.
    /// </summary>
    /// <param name="cache">Cache service for storing downloaded data locally.</param>
    public YahooFinanceDataProvider(FileCacheService cache)
        : this(cache, useDividendAdjustedPrices: false)
    {
    }

    /// <summary>
    /// Creates a new Yahoo Finance data provider.
    /// </summary>
    /// <param name="cache">Cache service for storing downloaded data locally.</param>
    /// <param name="useDividendAdjustedPrices">
    /// When true, open/high/low/close are scaled by Yahoo's adjusted close so dividends are
    /// reflected (an approximation of total return). These prices are back-adjusted to the
    /// download date, so they are not prices that actually traded and they change when new
    /// dividends are paid (invalidate the cache to refresh them).
    /// </param>
    public YahooFinanceDataProvider(FileCacheService cache, bool useDividendAdjustedPrices)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _useDividendAdjustedPrices = useDividendAdjustedPrices;
    }

    // Adjusted and raw candles never share a cache file
    private string ProviderKey => _useDividendAdjustedPrices ? ProviderName + "-adj" : ProviderName;

    /// <inheritdoc />
    public async Task<IEnumerable<Core.Models.Candle>> GetHistoricalDataAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        // Calendar dates, inclusive: a single day (from == to) is a valid range
        var dayFrom = from.Date;
        var dayTo   = to.Date;

        if (dayFrom > dayTo)
            throw new ArgumentException("Start date must not be after end date.", nameof(from));

        // Normalize symbol for Yahoo Finance format
        var yahooSymbol = NormalizeSymbol(symbol);

        if (yahooSymbol.EndsWith("=X", StringComparison.Ordinal))
            throw new NotSupportedException(
                $"Forex symbols ('{yahooSymbol}') are not supported: YahooQuotesApi does not return history for currency pairs.");

        // A range reaching yesterday or later is still growing (and its last session may be
        // in progress or not settled yet), so it is never cached.
        var isOpenRange = dayTo >= DateTime.UtcNow.Date.AddDays(-1);

        IReadOnlyList<Core.Models.Candle>? candles = null;

        if (!isOpenRange)
        {
            var cached = await _cache.TryLoadAsync(ProviderKey, yahooSymbol, dayFrom, dayTo, cancellationToken)
                .ConfigureAwait(false);

            if (cached != null)
            {
                var list = cached as IReadOnlyList<Core.Models.Candle> ?? cached.ToList();
                if (IsUsable(list))
                    candles = list;
                else
                    _cache.Invalidate(ProviderKey, yahooSymbol, dayFrom, dayTo); // unusable entry: download again
            }
        }

        if (candles is null)
        {
            candles = await DownloadAsync(yahooSymbol, dayFrom, dayTo, _useDividendAdjustedPrices, cancellationToken)
                .ConfigureAwait(false);

            if (!isOpenRange)
            {
                try
                {
                    await _cache.SaveAsync(ProviderKey, yahooSymbol, dayFrom, dayTo, candles, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The disk cache is best-effort: the data was downloaded fine.
                }
            }
        }

        // A copy, so callers never hold the cache's own collection
        return candles.ToList();
    }

    // Same rules as a fresh download: at least one candle and every close > 0
    private static bool IsUsable(IReadOnlyList<Core.Models.Candle> candles) =>
        candles.Count > 0 && candles.All(c => c.Close > 0);

    private static async Task<List<Core.Models.Candle>> DownloadAsync(
        string yahooSymbol,
        DateTime dayFrom,
        DateTime dayTo,
        bool useDividendAdjustedPrices,
        CancellationToken cancellationToken)
    {
        // Start a day early: sessions of exchanges ahead of UTC (e.g. ASX) open on the
        // previous UTC day. Bars are then filtered by their local trading date.
        var instantFrom = Instant.FromDateTimeUtc(DateTime.SpecifyKind(
            dayFrom > DateTime.MinValue ? dayFrom.AddDays(-1) : dayFrom, DateTimeKind.Utc));

        var yahoo = new YahooQuotesBuilder()
            .WithHistoryStartDate(instantFrom)
            .Build();

        // No base symbol: only the instrument's own ticks are used
        // ("USD" would be read as a stock ticker and add an unrelated download).
        var result = await yahoo.GetHistoryAsync(yahooSymbol, "", cancellationToken)
            .ConfigureAwait(false);

        if (result.HasError)
        {
            throw new InvalidOperationException($"Error fetching data from Yahoo Finance for {yahooSymbol}: {result.Error}");
        }

        if (!result.HasValue || result.Value.Ticks.IsEmpty)
        {
            throw new InvalidOperationException($"No historical data found for {yahooSymbol} from Yahoo Finance.");
        }

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(result.Value.ExchangeTimezoneName ?? string.Empty)
                   ?? DateTimeZone.Utc;
        var firstDay = LocalDate.FromDateTime(dayFrom);
        var lastDay  = LocalDate.FromDateTime(dayTo);

        var ticks = result.Value.Ticks
            .OrderBy(tick => tick.Date)
            .Select(tick => (Day: tick.Date.InZone(zone).Date, Tick: tick))
            .Where(x => x.Day >= firstDay && x.Day <= lastDay)
            .ToList();

        var negative = ticks.FindIndex(x => x.Tick.Close < 0);
        if (negative >= 0)
        {
            throw new InvalidOperationException($"Invalid data: Candle with negative close price found for {yahooSymbol} on {ticks[negative].Day:yyyy-MM-dd}. " +
                $"This may indicate a data issue with Yahoo Finance for this symbol and date.");
        }

        // YahooQuotesApi maps JSON nulls to 0, so Close == 0 means Yahoo has no bar for
        // that timestamp (exchange holiday, or a session whose close is not settled yet).
        var candles = ticks
            .Where(x => x.Tick.Close > 0)
            .GroupBy(x => x.Day)
            .Select(g => g.Last()) // one bar per trading date
            .Select(x =>
            {
                // Adjusted close is 0 when Yahoo has no value: keep the raw prices then
                var factor = useDividendAdjustedPrices && x.Tick.AdjustedClose > 0 && double.IsFinite(x.Tick.AdjustedClose)
                    ? x.Tick.AdjustedClose / x.Tick.Close
                    : 1.0;

                return new Core.Models.Candle(
                    timestamp: new DateTime(x.Day.Year, x.Day.Month, x.Day.Day, 0, 0, 0, DateTimeKind.Utc),
                    open: (decimal)(x.Tick.Open * factor),
                    high: (decimal)(x.Tick.High * factor),
                    low: (decimal)(x.Tick.Low * factor),
                    close: (decimal)(x.Tick.Close * factor),
                    volume: (decimal)x.Tick.Volume
                );
            })
            .ToList();

        if (candles.Count == 0)
        {
            throw new InvalidOperationException($"No historical data found for {yahooSymbol} in the specified date range.");
        }

        return candles;
    }

    /// <summary>
    /// Normalizes symbols to Yahoo Finance convention. Only these inputs are
    /// rewritten: "XXXUSDT" / "XXX-USDT" / "XXX/USDT" → "XXX-USD", and bare "BTC" / "ETH"
    /// → "BTC-USD" / "ETH-USD". Every other symbol (stocks, ETFs such as GBTC or FBTC,
    /// "^" indices, exchange suffixes like ".MC", existing "XXX-USD" crypto) is unchanged.
    /// </summary>
    internal static string NormalizeSymbol(string symbol)
    {
        symbol = symbol.Trim().ToUpperInvariant();

        // Convert BTCUSDT / BTC-USDT → BTC-USD
        var usdtPair = UsdtPair.Match(symbol);
        if (usdtPair.Success)
            return $"{usdtPair.Groups[1].Value}-USD";

        // Convert BTC → BTC-USD, ETH → ETH-USD
        return symbol is "BTC" or "ETH" ? $"{symbol}-USD" : symbol;
    }
}
