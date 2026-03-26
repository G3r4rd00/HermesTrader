namespace HermesTrade.Core.Models;

/// <summary>
/// A snapshot of the portfolio at a specific moment in the backtest.
/// </summary>
public sealed class PortfolioState
{
    /// <summary>Whether there is a currently open trade.</summary>
    public bool HasOpenPosition { get; set; }

    /// <summary>Available cash (not invested).</summary>
    public decimal Cash { get; set; }

    /// <summary>Number of units held in the open position.</summary>
    public decimal PositionSize { get; set; }

    /// <summary>Entry price of the current open position (0 if none).</summary>
    public decimal EntryPrice { get; set; }

    /// <summary>Total portfolio value: cash + open position market value.</summary>
    public decimal CurrentEquity { get; set; }
}
