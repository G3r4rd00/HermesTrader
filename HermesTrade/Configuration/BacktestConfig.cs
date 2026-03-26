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
    /// Applied on both entry and exit.
    /// </summary>
    public decimal Fees { get; init; } = 0.001m;

    /// <summary>
    /// Fraction of available cash to invest per trade (0–1).
    /// Defaults to 1 (full allocation).
    /// </summary>
    public decimal PositionSizeFraction { get; init; } = 1m;
}
