using HermesTrade.Core.Models;
using HermesTrade.Interfaces;
using NodaTime;
using YahooQuotesApi;



namespace HermesTrade.Data;

/// <summary>
/// Downloads historical OHLCV data from Yahoo Finance using the YahooQuotesApi library.
/// 
/// Supports:
/// - Stocks (e.g., "AAPL", "MSFT", "TSLA")
/// - Indices (e.g., "^GSPC" for S&P 500, "^DJI" for Dow Jones)
/// - Forex (e.g., "EURUSD=X", "GBPUSD=X")
/// - Cryptocurrencies (e.g., "BTC-USD", "ETH-USD")
/// - ETFs and more
/// 
/// No API key required. Free to use with reasonable rate limits.
/// </summary>
public sealed class YahooFinanceDataProvider : IMarketDataProvider
{
    private const string ProviderName = "yahoo-finance";

    private readonly FileCacheService _cache;

    /// <summary>
    /// Creates a new Yahoo Finance data provider.
    /// </summary>
    /// <param name="cache">Cache service for storing downloaded data locally.</param>
    public YahooFinanceDataProvider(FileCacheService cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Core.Models.Candle>> GetHistoricalDataAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (from >= to)
            throw new ArgumentException("Start date must be before end date.", nameof(from));

        // Normalize symbol for Yahoo Finance format
        var yahooSymbol = NormalizeSymbol(symbol);

        // Try to load from cache first
        var cached = await _cache.TryLoadAsync(ProviderName, yahooSymbol, from, to, cancellationToken)
            .ConfigureAwait(false);

        if (cached != null)
        {
            Console.WriteLine($"[Yahoo Finance] ✓ Loaded {cached.Count()} candles from cache for {yahooSymbol}");
            return cached;
        }

        Console.WriteLine($"[Yahoo Finance] Downloading historical data for {yahooSymbol}...");
        Console.WriteLine($"  Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");

        try
        {
            // Download data from Yahoo Finance using YahooQuotesApi v7
            var instantFrom = Instant.FromDateTimeUtc(DateTime.SpecifyKind(from, DateTimeKind.Utc));
            var instantTo   = Instant.FromDateTimeUtc(DateTime.SpecifyKind(to,   DateTimeKind.Utc));

            var yahoo = new YahooQuotesBuilder()
                .WithHistoryStartDate(instantFrom)
                .Build();

            var result = await yahoo.GetHistoryAsync(yahooSymbol, "USD", cancellationToken)
                .ConfigureAwait(false);

            if (result.HasError)
            {
                Console.WriteLine($"[Yahoo Finance] ⚠ Error from Yahoo Finance for {yahooSymbol}: {result.Error}");
                return [];
            }

            if (!result.HasValue || result.Value.Ticks.IsEmpty)
            {
                Console.WriteLine($"[Yahoo Finance] ⚠ No historical data found for {yahooSymbol}");
                return [];
            }

            var historicalData = result.Value.Ticks;

            var candles = historicalData
                .Where(tick => tick.Date <= instantTo)
                .OrderBy(tick => tick.Date)
                .Select(tick => new Core.Models.Candle(
                    timestamp: tick.Date.ToDateTimeUtc(),
                    open: (decimal)tick.Open,
                    high: (decimal)tick.High,
                    low: (decimal)tick.Low,
                    close: (decimal)tick.Close,
                    volume: (decimal)tick.Volume
                ))
                .ToList();

            if (candles.Count == 0)
            {
                Console.WriteLine($"[Yahoo Finance] ⚠ No data found for {yahooSymbol}");
                return [];
            }

            var candelNonPositiveClose = candles.FirstOrDefault(c => c.Close <= 0);
            if (candelNonPositiveClose != null)
            {
                Console.WriteLine($"[Yahoo Finance] ⚠ Warning: Some candles have non-positive close prices for {yahooSymbol} {candelNonPositiveClose.Timestamp:yyyy-MM-dd}");
            }

            Console.WriteLine($"[Yahoo Finance] ✓ Downloaded {candles.Count} candles");
            Console.WriteLine($"  First: {candles.First().Timestamp:yyyy-MM-dd} @ ${candles.First().Close:N2}");
            Console.WriteLine($"  Last:  {candles.Last().Timestamp:yyyy-MM-dd} @ ${candles.Last().Close:N2}");
            Console.WriteLine($"  Change: {((candles.Last().Close - candles.First().Close) / candles.First().Close * 100):+0.00;-0.00}%");

            // Save to cache for future use
            await _cache.SaveAsync(ProviderName, yahooSymbol, from, to, candles, cancellationToken)
                .ConfigureAwait(false);

            return candles;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[Yahoo Finance] ✗ Error downloading data for {yahooSymbol}: {ex.Message}");
            throw new InvalidOperationException(
                $"Failed to download historical data for {yahooSymbol} from Yahoo Finance. " +
                $"Ensure the symbol is correct and Yahoo Finance supports it.", ex);
        }
    }

    /// <summary>
    /// Normalizes symbol formats to Yahoo Finance convention.
    /// </summary>
    private static string NormalizeSymbol(string symbol)
    {
        // Yahoo Finance uses specific formats:
        // - Stocks: "AAPL", "MSFT"
        // - Crypto: "BTC-USD", "ETH-USD" (Yahoo format)
        // - Forex: "EURUSD=X"
        // - Indices: "^GSPC", "^DJI"

        symbol = symbol.Trim().ToUpperInvariant();

        // Handle common crypto format conversions
        if (symbol.Contains("USDT"))
        {
            // Convert BTCUSDT → BTC-USD
            symbol = symbol.Replace("USDT", "-USD");
        }
        else if (symbol.Contains("BTC") && !symbol.Contains("-") && !symbol.Contains("="))
        {
            // Convert BTC → BTC-USD
            symbol = $"{symbol}-USD";
        }
        else if (symbol.Contains("ETH") && !symbol.Contains("-") && !symbol.Contains("="))
        {
            // Convert ETH → ETH-USD
            symbol = $"{symbol}-USD";
        }

        return symbol;
    }
}
