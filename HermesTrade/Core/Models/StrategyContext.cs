namespace HermesTrade.Core.Models;

/// <summary>
/// All information available to a strategy when it evaluates a single candle.
/// </summary>
public sealed class StrategyContext
{
    /// <summary>The current candle being evaluated.</summary>
    public required Candle Candle { get; init; }

    /// <summary>Pre-computed indicator values for the current candle index.</summary>
    public required IndicatorSnapshot Indicators { get; init; }

    /// <summary>Current portfolio state before any action on this candle.</summary>
    public required PortfolioState Portfolio { get; init; }

    /// <summary>
    /// Candle series up to and including the current candle (read-only, newest last).
    /// Includes warm-up candles before the backtest window, never future candles.
    /// </summary>
    public required IReadOnlyList<Candle> History { get; init; }

    /// <summary>Context of the previous candle in the backtest window (null on the first one).</summary>
    public StrategyContext? Prev { get; init; }
}
