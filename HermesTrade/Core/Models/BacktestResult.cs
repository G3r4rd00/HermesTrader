namespace HermesTrade.Core.Models;

/// <summary>
/// The full result produced by a backtest run.
/// </summary>
public sealed class BacktestResult
{
    /// <summary>Net profit (or loss) across all trades.</summary>
    public decimal TotalProfit { get; init; }

    /// <summary>Total number of completed trades.</summary>
    public int TotalTrades { get; init; }

    /// <summary>Fraction of trades that were profitable (0–1).</summary>
    public double WinRate { get; init; }

    /// <summary>Chronological equity curve (portfolio value at each candle).</summary>
    public IReadOnlyList<decimal> EquityCurve { get; init; } = [];

    /// <summary>All completed trades in chronological order.</summary>
    public IReadOnlyList<Trade> Trades { get; init; } = [];

    /// <summary>Maximum peak-to-trough drawdown in currency units.</summary>
    public decimal MaxDrawdown { get; init; }

    /// <summary>Annualised Sharpe ratio (0 if insufficient data).</summary>
    public double SharpeRatio { get; init; }
}
