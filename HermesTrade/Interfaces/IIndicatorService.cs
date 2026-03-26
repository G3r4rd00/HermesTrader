using HermesTrade.Core.Models;

namespace HermesTrade.Interfaces;

/// <summary>
/// Pre-computes and provides technical indicator snapshots for each candle in a series.
/// </summary>
public interface IIndicatorService
{
    /// <summary>
    /// Initialises the service by computing all indicators from the full candle history.
    /// Must be called before any call to <see cref="GetSnapshot"/>.
    /// </summary>
    /// <param name="candles">The complete, ordered candle series.</param>
    void Initialize(IReadOnlyList<Candle> candles);

    /// <summary>
    /// Returns a pre-computed indicator snapshot for the candle at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">Zero-based index into the candle series.</param>
    IndicatorSnapshot GetSnapshot(int index);
}
