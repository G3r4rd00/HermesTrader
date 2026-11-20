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
    /// <param name="candles">Candles of the backtest window (also used to infer the Sharpe annualisation).</param>
    /// <param name="initialCapital">Starting capital (used for Sharpe ratio base).</param>
    public static BacktestResult Compute(
        IReadOnlyList<Trade>   trades,
        IReadOnlyList<decimal> equityCurve,
        IReadOnlyList<Candle>  candles,
        decimal                initialCapital) =>
        Compute(trades, equityCurve, candles, initialCapital, InferPeriodsPerYear(candles));

    /// <inheritdoc cref="Compute(IReadOnlyList{Trade}, IReadOnlyList{decimal}, IReadOnlyList{Candle}, decimal)"/>
    /// <param name="trades">All completed trades in chronological order.</param>
    /// <param name="equityCurve">Portfolio equity value recorded at each candle.</param>
    /// <param name="candles">Candles of the backtest window.</param>
    /// <param name="initialCapital">Starting capital (start of the drawdown and Sharpe series).</param>
    /// <param name="periodsPerYear">Candles per year used to annualise the Sharpe ratio.</param>
    public static BacktestResult Compute(
        IReadOnlyList<Trade>   trades,
        IReadOnlyList<decimal> equityCurve,
        IReadOnlyList<Candle>  candles,
        decimal                initialCapital,
        double                 periodsPerYear)
    {
        // Equity-based metrics are computed even without trades: the curve may still move
        var totalProfit = trades.Sum(t => t.Profit);
        var wins        = trades.Count(t => t.IsWin);
        var winRate     = trades.Count == 0 ? 0 : (double)wins / trades.Count;
        var maxDrawdown = ComputeMaxDrawdown(equityCurve, initialCapital);
        var sharpe      = ComputeSharpeRatio(equityCurve, initialCapital, periodsPerYear);

        return new BacktestResult
        {
            TotalProfit  = totalProfit,
            TotalTrades  = trades.Count,
            WinRate      = winRate,
            EquityCurve  = equityCurve,
            Trades       = trades,
            MaxDrawdown  = maxDrawdown,
            Candles      = candles,
            SharpeRatio  = sharpe,
        };
    }

    // ── Max Drawdown ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the maximum peak-to-trough decline (in currency units) in the
    /// equity curve, taking its first value as the starting peak.
    /// </summary>
    public static decimal ComputeMaxDrawdown(IReadOnlyList<decimal> equityCurve) =>
        equityCurve.Count == 0 ? 0m : ComputeMaxDrawdown(equityCurve, equityCurve[0]);

    /// <summary>
    /// Returns the maximum peak-to-trough decline (in currency units) in the
    /// equity curve, starting from <paramref name="initialCapital"/> so that losses
    /// and fees on the first candle are included.
    /// </summary>
    public static decimal ComputeMaxDrawdown(IReadOnlyList<decimal> equityCurve, decimal initialCapital)
    {
        if (equityCurve.Count == 0)
            return 0m;

        var peak     = initialCapital;
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
    /// Computes an approximate annualised Sharpe ratio from per-candle equity returns,
    /// assuming a risk-free rate of 0. The first return is measured from
    /// <paramref name="initialCapital"/> to the first equity value.
    /// Returns 0 when there are fewer than 2 returns or when standard deviation
    /// is zero.
    /// </summary>
    /// <param name="equityCurve">Portfolio equity value recorded at each candle.</param>
    /// <param name="initialCapital">Starting capital (base of the first return).</param>
    /// <param name="periodsPerYear">
    /// Candles per year used to annualise (252 for daily stocks, 365 for daily crypto).
    /// </param>
    public static double ComputeSharpeRatio(
        IReadOnlyList<decimal> equityCurve,
        decimal                initialCapital,
        double                 periodsPerYear = 252)
    {
        if (equityCurve.Count == 0 || initialCapital <= 0m)
            return 0;

        // Compute period-over-period returns, starting from the initial capital
        var returns = new double[equityCurve.Count];
        for (int i = 0; i < equityCurve.Count; i++)
        {
            var prev     = i == 0 ? initialCapital : equityCurve[i - 1];
            var current  = equityCurve[i];
            returns[i] = prev == 0m
                ? 0
                : (double)((current - prev) / prev);
        }

        var mean   = returns.Average();
        var stdDev = StandardDeviation(returns);

        if (stdDev == 0)
            return 0;

        return mean / stdDev * Math.Sqrt(periodsPerYear);
    }

    // ── Internal utilities ────────────────────────────────────────────────────

    /// <summary>
    /// Estimates candles per year from the timestamps, so annualisation matches
    /// the data (≈252 for daily stocks, ≈365 for daily crypto, more for intraday).
    /// Reliable for series spanning a few months or more; short windows or data gaps
    /// skew it (set <c>BacktestConfig.PeriodsPerYear</c> instead).
    /// Falls back to 252 when it cannot be inferred.
    /// </summary>
    public static double InferPeriodsPerYear(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < 2)
            return 252;

        var days = (candles[^1].Timestamp - candles[0].Timestamp).TotalDays;
        return days > 0 ? (candles.Count - 1) * 365.25 / days : 252;
    }

    private static double StandardDeviation(double[] values)
    {
        if (values.Length < 2)
            return 0;

        var mean     = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        return Math.Sqrt(variance);
    }
}
