using HermesTrade.Core.Enums;
using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

namespace HermesTrade.Darwin;

/// <summary>
/// HermesTrade strategy driven by an evolved network. It decides with the features of the
/// previous candle and the engine fills at the current close: a one-candle execution delay
/// that removes any doubt about trading on a close it has just seen.
/// </summary>
public sealed class NeuralStrategy : IStrategy
{
    public const int ExecutionDelay = 1;

    private readonly IBrain _brain;
    private readonly SymbolData _data;
    private readonly double[] _x = new double[Features.InputCount];
    private readonly double[] _scratch;
    private int _entryIndex = -1;

    public NeuralStrategy(IBrain brain, SymbolData data)
    {
        _brain = brain;
        _data = data;
        _scratch = new double[brain.ScratchSize];
    }

    public void Initialize(IEnumerable<Candle> history) => _entryIndex = -1;

    public Signal Evaluate(List<StrategyContext> history)
    {
        var ctx = history[^1];
        int i = ctx.History.Count - 1; // index in the full candle series
        int f = i - ExecutionDelay;
        if (f < 0 || !_data.Ready[f])
            return Signal.Hold;

        int m = Features.MarketCount;
        _data.Features[f].AsSpan().CopyTo(_x);

        bool inPos = ctx.Portfolio.HasOpenPosition;
        if (inPos && _entryIndex < 0)
            _entryIndex = i;

        _x[m]     = inPos ? 1 : -1;
        _x[m + 1] = inPos && ctx.Portfolio.EntryPrice > 0
            ? Math.Tanh(Math.Log((double)_data.Data.Candles[f].Close / (double)ctx.Portfolio.EntryPrice) / (_data.Sigma[f] * Math.Sqrt(20)))
            : 0;
        _x[m + 2] = inPos ? Math.Tanh((i - _entryIndex) / 40.0) : 0;

        double o = _brain.Output(_x, _scratch);

        if (!inPos && o > _brain.EntryThreshold)
        {
            _entryIndex = i;
            return Signal.Buy;
        }

        if (inPos && o < _brain.ExitThreshold)
        {
            _entryIndex = -1;
            return Signal.Sell;
        }

        return Signal.Hold;
    }
}

/// <summary>Buys on the first candle and never sells.</summary>
public sealed class BuyAndHoldStrategy : IStrategy
{
    public void Initialize(IEnumerable<Candle> history) { }

    public Signal Evaluate(List<StrategyContext> history) => Signal.Buy;
}

/// <summary>
/// Classic rule baselines over HermesTrade indicators, with the same one-candle delay
/// as the network so the comparison is fair.
/// </summary>
public sealed class RuleStrategy(string name, Func<IndicatorSnapshot, Candle, bool?> wantLong, SymbolData data) : IStrategy
{
    public string Name { get; } = name;

    public void Initialize(IEnumerable<Candle> history) { }

    public Signal Evaluate(List<StrategyContext> history)
    {
        var ctx = history[^1];
        int f = ctx.History.Count - 1 - NeuralStrategy.ExecutionDelay;
        if (f < 0)
            return Signal.Hold;

        var want = wantLong(data.Data.Snapshots[f], data.Data.Candles[f]);
        return want switch
        {
            true when !ctx.Portfolio.HasOpenPosition => Signal.Buy,
            false when ctx.Portfolio.HasOpenPosition => Signal.Sell,
            _ => Signal.Hold,
        };
    }

    public static IEnumerable<(string Name, Func<SymbolData, IStrategy> Create)> All()
    {
        yield return ("Buy & Hold", _ => new BuyAndHoldStrategy());
        yield return ("Trend SMA200", d => new RuleStrategy("Trend SMA200",
            (s, c) => s.SMA200 is decimal sma ? c.Close > sma : null, d));
        yield return ("Golden cross 50/200", d => new RuleStrategy("Golden cross 50/200",
            (s, _) => s.SMA50 is decimal f && s.SMA200 is decimal l ? f > l : null, d));
        yield return ("RSI 30/70", d => new RuleStrategy("RSI 30/70",
            (s, _) => s.RSI is decimal r ? (r < 30 ? true : r > 70 ? false : null) : null, d));
    }
}
