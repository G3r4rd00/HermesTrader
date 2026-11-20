using HermesTrade.Core.Models;

namespace HermesTrade.Engine;

/// <summary>
/// Holds market data and pre-computed indicator snapshots so that the
/// expensive data-loading and indicator-computation steps only happen once
/// per backtest campaign, rather than once per fitness evaluation.
/// </summary>
public sealed class PrecomputedBacktestData
{
    /// <summary>All loaded candles, ascending by timestamp (indicator warm-up included).</summary>
    public required IReadOnlyList<Candle> Candles { get; init; }

    /// <summary>One indicator snapshot per candle, aligned with <see cref="Candles"/>.</summary>
    public required IndicatorSnapshot[] Snapshots { get; init; }

    /// <summary>Symbol the data was loaded for (null when built by hand: not checked).</summary>
    public string? Symbol { get; init; }

    /// <summary>Start of the backtest range the data was loaded for (null: not checked).</summary>
    public DateTime? From { get; init; }

    /// <summary>End of the backtest range the data was loaded for (null: not checked).</summary>
    public DateTime? To { get; init; }
}
