using HermesTrade.Core.Enums;
using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

namespace HermesTrade.Strategies;

/// <summary>
/// A simple RSI-based strategy included as a reference implementation.
///
/// Rules:
///   BUY  when RSI drops below 30 and there is no open position.
///   SELL when RSI rises above 70 and there is an open position.
/// </summary>
public sealed class SimpleStrategyExample : IStrategy
{
    private readonly decimal _oversoldThreshold;
    private readonly decimal _overboughtThreshold;

    /// <param name="oversoldThreshold">RSI level below which a Buy signal is generated (default 30).</param>
    /// <param name="overboughtThreshold">RSI level above which a Sell signal is generated (default 70).</param>
    public SimpleStrategyExample(decimal oversoldThreshold = 30m, decimal overboughtThreshold = 70m)
    {
        _oversoldThreshold   = oversoldThreshold;
        _overboughtThreshold = overboughtThreshold;
    }

    /// <inheritdoc />
    public void Initialize(IEnumerable<Candle> history) { /* no warm-up required */ }

    /// <inheritdoc />
    public Signal Evaluate(List<StrategyContext> history)
    {
        StrategyContext context = history.Last();
        var rsi = context.Indicators.RSI;

        if (rsi < _oversoldThreshold && !context.Portfolio.HasOpenPosition)
            return Signal.Buy;

        if (rsi > _overboughtThreshold && context.Portfolio.HasOpenPosition)
            return Signal.Sell;

        return Signal.Hold;
    }
}
