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
            return cached;
        }

        
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
            throw new InvalidOperationException($"Error fetching data from Yahoo Finance for {yahooSymbol}: {result.Error}");
        }

        if (!result.HasValue || result.Value.Ticks.IsEmpty)
        {
            throw new InvalidOperationException($"No historical data found for {yahooSymbol} from Yahoo Finance.");
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
            throw new InvalidOperationException($"No historical data found for {yahooSymbol} in the specified date range.");
        }

        var candelNonPositiveClose = candles.FirstOrDefault(c => c.Close <= 0);
        if (candelNonPositiveClose != null)
        {
            throw new InvalidOperationException($"Invalid data: Candle with non-positive close price found for {yahooSymbol} on {candelNonPositiveClose.Timestamp:yyyy-MM-dd}. " +
                $"This may indicate a data issue with Yahoo Finance for this symbol and date.");
                
        }

        // Save to cache for future use
        await _cache.SaveAsync(ProviderName, yahooSymbol, from, to, candles, cancellationToken)
            .ConfigureAwait(false);

        return candles;
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
