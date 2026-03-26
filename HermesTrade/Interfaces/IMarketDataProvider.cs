using HermesTrade.Core.Models;

namespace HermesTrade.Interfaces;

/// <summary>
/// Provides historical OHLCV market data for a given symbol and date range.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Retrieves historical candle data for the specified symbol between
    /// <paramref name="from"/> and <paramref name="to"/> (inclusive).
    /// </summary>
    /// <param name="symbol">Market symbol, e.g. "BTCUSDT" or "BTC-USD".</param>
    /// <param name="from">Start of the requested date range (UTC).</param>
    /// <param name="to">End of the requested date range (UTC).</param>
    /// <returns>An ordered (ascending by time) sequence of candles.</returns>
    Task<IEnumerable<Candle>> GetHistoricalDataAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}
