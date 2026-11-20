namespace HermesTrade.Configuration;

/// <summary>
/// Configuration settings for a single backtest run.
/// </summary>
public sealed class BacktestConfig
{
    /// <summary>Market symbol to back-test, e.g. "BTCUSDT" or "BTC-USD".</summary>
    public required string Symbol { get; init; }

    /// <summary>Start of the historical window to back-test (UTC).</summary>
    public required DateTime From { get; init; }

    /// <summary>End of the historical window to back-test (UTC).</summary>
    public required DateTime To { get; init; }

    /// <summary>Starting portfolio value in the quote currency (e.g. USD).</summary>
    public decimal InitialCapital { get; init; } = 10_000m;

    /// <summary>
    /// Per-trade fee expressed as a fraction of notional value (e.g. 0.001 = 0.1%).
    /// Applied on both entry and exit. Must be in (-1, 1); a negative value is a rebate.
    /// </summary>
    public decimal Fees { get; init; } = 0.001m;

    /// <summary>
    /// Fraction of available cash to invest per trade, in (0, 1].
    /// Defaults to 1 (full allocation).
    /// </summary>
    public decimal PositionSizeFraction { get; init; } = 1m;

    /// <summary>
    /// Extra history loaded before <see cref="From"/> only to warm up indicators
    /// (e.g. SMA200, RSI, MACD). Those candles are never traded and are not part of
    /// the equity curve, the metrics or <c>BacktestResult.Candles</c>.
    /// Defaults to 400 calendar days; use <see cref="TimeSpan.Zero"/> to disable.
    /// </summary>
    public TimeSpan IndicatorWarmup { get; init; } = TimeSpan.FromDays(400);

    /// <summary>
    /// Candles per year used to annualise the Sharpe ratio (e.g. 252 for daily stocks,
    /// 365 for daily crypto). Null (the default) infers it from the loaded candles' timestamps,
    /// which is reliable for long series; set it for short or irregular data.
    /// </summary>
    public double? PeriodsPerYear { get; init; }

    /// <summary>
    /// Throws if any setting is out of range. Called by <c>BacktestEngine</c> before every run.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol must not be empty.", nameof(Symbol));
        if (From > To)
            throw new ArgumentException($"From ({From:O}) must not be after To ({To:O}).", nameof(From));
        if (InitialCapital <= 0m)
            throw new ArgumentOutOfRangeException(nameof(InitialCapital), InitialCapital, "InitialCapital must be greater than 0.");
        if (Fees <= -1m || Fees >= 1m)
            throw new ArgumentOutOfRangeException(nameof(Fees), Fees, "Fees must be greater than -1 and less than 1.");
        if (PositionSizeFraction <= 0m || PositionSizeFraction > 1m)
            throw new ArgumentOutOfRangeException(nameof(PositionSizeFraction), PositionSizeFraction, "PositionSizeFraction must be in (0, 1].");
        if (IndicatorWarmup < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(IndicatorWarmup), IndicatorWarmup, "IndicatorWarmup must not be negative.");
        if (PeriodsPerYear is double periods && !(periods > 0 && double.IsFinite(periods)))
            throw new ArgumentOutOfRangeException(nameof(PeriodsPerYear), PeriodsPerYear, "PeriodsPerYear must be a positive number.");
    }
}
