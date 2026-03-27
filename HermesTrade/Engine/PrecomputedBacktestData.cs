using HermesTrade.Core.Models;

namespace HermesTrade.Engine;

/// <summary>
/// Holds market data and pre-computed indicator snapshots so that the
/// expensive data-loading and indicator-computation steps only happen once
/// per backtest campaign, rather than once per fitness evaluation.
/// </summary>
public sealed class PrecomputedBacktestData
{
    public required IReadOnlyList<Candle> Candles { get; init; }
    public required IndicatorSnapshot[] Snapshots { get; init; }
}
