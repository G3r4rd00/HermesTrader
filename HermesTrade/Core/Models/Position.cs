namespace HermesTrade.Core.Models;

/// <summary>
/// Tracks an open trading position.
/// </summary>
public sealed class Position
{
    /// <summary>Timestamp when the position was opened.</summary>
    public DateTime OpenTime { get; private set; }

    /// <summary>Price at which the position was opened.</summary>
    public decimal EntryPrice { get; private set; }

    /// <summary>Size of the position in units of the base asset.</summary>
    public decimal Size { get; private set; }

    /// <summary>Whether the position is currently open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Opens a new position.</summary>
    public void Open(DateTime time, decimal price, decimal size)
    {
        OpenTime = time;
        EntryPrice = price;
        Size = size;
        IsOpen = true;
    }

    /// <summary>Closes the position and returns the resulting trade.</summary>
    public Trade Close(DateTime time, decimal exitPrice, decimal entryFee = 0m, decimal exitFee = 0m)
    {
        if (!IsOpen)
            throw new InvalidOperationException("Cannot close a position that is not open.");

        IsOpen = false;

        return new Trade(
            entryTime: OpenTime,
            exitTime: time,
            entryPrice: EntryPrice,
            exitPrice: exitPrice,
            positionSize: Size,
            entryFee: entryFee,
            exitFee: exitFee);
    }
}
