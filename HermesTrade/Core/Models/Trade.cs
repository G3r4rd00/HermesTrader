namespace HermesTrade.Core.Models;

/// <summary>
/// Represents a completed trade (entry + exit).
/// </summary>
public sealed class Trade
{
    /// <summary>Timestamp when the position was entered.</summary>
    public DateTime EntryTime { get; init; }

    /// <summary>Timestamp when the position was exited.</summary>
    public DateTime ExitTime { get; init; }

    /// <summary>Price at which the position was entered.</summary>
    public decimal EntryPrice { get; init; }

    /// <summary>Price at which the position was exited.</summary>
    public decimal ExitPrice { get; init; }

    /// <summary>Size of the position in units of the base asset.</summary>
    public decimal PositionSize { get; init; }

    /// <summary>Fee applied on entry (as a fraction, e.g. 0.001 = 0.1%).</summary>
    public decimal EntryFee { get; init; }

    /// <summary>Fee applied on exit (as a fraction, e.g. 0.001 = 0.1%).</summary>
    public decimal ExitFee { get; init; }

    /// <summary>Net profit (or loss) after fees.</summary>
    public decimal Profit =>
        (ExitPrice - EntryPrice) * PositionSize
        - (EntryPrice * PositionSize * EntryFee)
        - (ExitPrice * PositionSize * ExitFee);

    /// <summary>Whether the trade was profitable.</summary>
    public bool IsWin => Profit > 0;

    public Trade(
        DateTime entryTime,
        DateTime exitTime,
        decimal entryPrice,
        decimal exitPrice,
        decimal positionSize,
        decimal entryFee = 0m,
        decimal exitFee = 0m)
    {
        EntryTime = entryTime;
        ExitTime = exitTime;
        EntryPrice = entryPrice;
        ExitPrice = exitPrice;
        PositionSize = positionSize;
        EntryFee = entryFee;
        ExitFee = exitFee;
    }
}
