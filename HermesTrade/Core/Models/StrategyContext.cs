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

    /// <summary>Full historical candle series (read-only, newest last).</summary>
    public required IReadOnlyList<Candle> History { get; init; }
}
