using HermesTrade.Core.Models;

namespace HermesTrade.Utilities;

/// <summary>
/// Stateless helper that derives performance metrics from a completed list of trades.
/// </summary>
public static class MetricsCalculator
{
    /// <summary>
    /// Computes all standard metrics and returns a <see cref="BacktestResult"/>.
    /// </summary>
    /// <param name="trades">All completed trades in chronological order.</param>
    /// <param name="equityCurve">Portfolio equity value recorded at each candle.</param>
    /// <param name="initialCapital">Starting capital (used for Sharpe ratio base).</param>
    public static BacktestResult Compute(
        IReadOnlyList<Trade>   trades,
        IReadOnlyList<decimal> equityCurve,
        decimal                initialCapital)
    {
        if (trades.Count == 0)
        {
            return new BacktestResult
            {
                TotalProfit  = 0m,
                TotalTrades  = 0,
                WinRate      = 0,
                EquityCurve  = equityCurve,
                Trades       = trades,
                MaxDrawdown  = 0m,
                SharpeRatio  = 0,
            };
        }

        var totalProfit = trades.Sum(t => t.Profit);
        var wins        = trades.Count(t => t.IsWin);
        var winRate     = (double)wins / trades.Count;
        var maxDrawdown = ComputeMaxDrawdown(equityCurve);
        var sharpe      = ComputeSharpeRatio(equityCurve, initialCapital);

        return new BacktestResult
        {
            TotalProfit  = totalProfit,
            TotalTrades  = trades.Count,
            WinRate      = winRate,
            EquityCurve  = equityCurve,
            Trades       = trades,
            MaxDrawdown  = maxDrawdown,
            SharpeRatio  = sharpe,
        };
    }

    // ── Max Drawdown ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the maximum peak-to-trough decline (in currency units) in the
    /// equity curve.
    /// </summary>
    public static decimal ComputeMaxDrawdown(IReadOnlyList<decimal> equityCurve)
    {
        if (equityCurve.Count == 0)
            return 0m;

        var peak     = equityCurve[0];
        var maxDD    = 0m;

        foreach (var value in equityCurve)
        {
            if (value > peak)
                peak = value;

            var drawdown = peak - value;
            if (drawdown > maxDD)
                maxDD = drawdown;
        }

        return maxDD;
    }

    // ── Sharpe Ratio ──────────────────────────────────────────────────────────

    /// <summary>
    /// Computes an approximate annualised Sharpe ratio from daily equity returns,
    /// assuming a risk-free rate of 0.
    /// Returns 0 when there are fewer than 2 data points or when standard deviation
    /// is zero.
    /// </summary>
    public static double ComputeSharpeRatio(
        IReadOnlyList<decimal> equityCurve,
        decimal                initialCapital)
    {
        if (equityCurve.Count < 2 || initialCapital <= 0m)
            return 0;

        // Compute period-over-period returns
        var returns = new double[equityCurve.Count - 1];
        for (int i = 1; i < equityCurve.Count; i++)
        {
            var prev     = equityCurve[i - 1];
            var current  = equityCurve[i];
            returns[i - 1] = prev == 0m
                ? 0
                : (double)((current - prev) / prev);
        }

        var mean   = returns.Average();
        var stdDev = StandardDeviation(returns);

        if (stdDev == 0)
            return 0;

        // Annualise assuming ~252 trading days per year
        return mean / stdDev * Math.Sqrt(252);
    }

    // ── Internal utilities ────────────────────────────────────────────────────

    private static double StandardDeviation(double[] values)
    {
        if (values.Length < 2)
            return 0;

        var mean     = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        return Math.Sqrt(variance);
    }
}
