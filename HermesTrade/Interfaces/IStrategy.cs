using HermesTrade.Core.Enums;
using HermesTrade.Core.Models;

namespace HermesTrade.Interfaces;

/// <summary>
/// A trading strategy that can be evaluated candle-by-candle during a backtest.
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// Called once before the backtest loop begins, allowing the strategy
    /// to warm up or pre-compute state from the full history.
    /// </summary>
    /// <param name="history">The complete, ordered candle history.</param>
    void Initialize(IEnumerable<Candle> history);

    /// <summary>
    /// Evaluates the strategy for the current candle and returns a trading signal.
    /// </summary>
    /// <param name="context">All information available at this point in time.</param>
    /// <returns>A <see cref="Signal"/> indicating the desired action.</returns>
    Signal Evaluate(StrategyContext context);
}
