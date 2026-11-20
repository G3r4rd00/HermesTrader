using HermesTrade.Core.Enums;
using HermesTrade.Core.Models;

namespace HermesTrade.Interfaces;

/// <summary>
/// A trading strategy that can be evaluated candle-by-candle during a backtest.
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// Called once before each backtest run, allowing the strategy to reset its
    /// state and warm up from the candles that precede the backtest window.
    /// </summary>
    /// <param name="history">
    /// The ordered warm-up candles strictly before <c>BacktestConfig.From</c>.
    /// Never contains candles inside or after the backtest window (no look-ahead).
    /// </param>
    void Initialize(IEnumerable<Candle> history);

    /// <summary>
    /// Evaluates the strategy for the current candle and returns a trading signal.
    /// </summary>
    /// <param name="history_contexts">All information available at this point in time, including previous contexts.</param>
    /// <returns>A <see cref="Signal"/> indicating the desired action.</returns>
    Signal Evaluate(List<StrategyContext> history_contexts);
}
