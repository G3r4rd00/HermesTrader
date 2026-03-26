namespace HermesTrade.Core.Models;

/// <summary>
/// Represents a single OHLCV candlestick in a price chart.
/// </summary>
public sealed class Candle
{
    /// <summary>The timestamp marking the start of the candle period.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>The opening price of the candle period.</summary>
    public decimal Open { get; init; }

    /// <summary>The highest price reached during the candle period.</summary>
    public decimal High { get; init; }

    /// <summary>The lowest price reached during the candle period.</summary>
    public decimal Low { get; init; }

    /// <summary>The closing price of the candle period.</summary>
    public decimal Close { get; init; }

    /// <summary>The total volume traded during the candle period.</summary>
    public decimal Volume { get; init; }

    public Candle(DateTime timestamp, decimal open, decimal high, decimal low, decimal close, decimal volume)
    {
        Timestamp = timestamp;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
}
