using HermesTrade.Configuration;
using HermesTrade.Core.Enums;
using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

namespace HermesTrade.Tests;

/// <summary>Deterministic synthetic data, providers and strategies shared by the tests.</summary>
internal static class TestData
{
    public static readonly DateTime Start = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Daily random walk (one candle per calendar day).</summary>
    public static List<Candle> Walk(int count, decimal startPrice = 100m, int seed = 42, DateTime? from = null)
    {
        var random = new Random(seed);
        var price  = startPrice;
        var first  = from ?? Start;

        return Enumerable.Range(0, count).Select(i =>
        {
            price *= 1m + (decimal)((random.NextDouble() - 0.5) * 0.04);
            return new Candle(first.AddDays(i), price, price * 1.01m, price * 0.99m, price, 1000m);
        }).ToList();
    }

    public static List<Candle> Flat(int count, decimal price = 100m) =>
        Enumerable.Range(0, count)
            .Select(i => new Candle(Start.AddDays(i), price, price, price, price, 1m))
            .ToList();

    public static BacktestConfig Config(DateTime from, DateTime to, decimal fees = 0.001m, TimeSpan? warmup = null) =>
        new()
        {
            Symbol          = "TEST",
            From            = from,
            To              = to,
            Fees            = fees,
            IndicatorWarmup = warmup ?? TimeSpan.Zero,
        };
}

/// <summary>Returns the same candles whatever range is requested.</summary>
internal sealed class FixedProvider(IReadOnlyList<Candle> candles) : IMarketDataProvider
{
    public Task<IEnumerable<Candle>> GetHistoricalDataAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Candle>>(candles);
}

/// <summary>Returns only the candles inside the requested range and records it.</summary>
internal sealed class RangeProvider(IReadOnlyList<Candle> candles) : IMarketDataProvider
{
    public DateTime LastFrom { get; private set; }

    public Task<IEnumerable<Candle>> GetHistoricalDataAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        LastFrom = from;
        return Task.FromResult<IEnumerable<Candle>>(candles.Where(c => c.Timestamp >= from && c.Timestamp <= to).ToList());
    }
}

/// <summary>Different series per symbol, returned asynchronously.</summary>
internal sealed class MultiProvider(Dictionary<string, List<Candle>> series) : IMarketDataProvider
{
    public async Task<IEnumerable<Candle>> GetHistoricalDataAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return series[symbol];
    }
}

/// <summary>Strategy driven by a delegate; records every context it evaluates.</summary>
internal sealed class DelegateStrategy(Func<List<StrategyContext>, Signal> evaluate) : IStrategy
{
    public List<StrategyContext> Seen { get; } = new();
    public List<Candle> InitializeHistory { get; private set; } = new();

    public void Initialize(IEnumerable<Candle> history)
    {
        Seen.Clear();
        InitializeHistory = history.ToList();
    }

    public Signal Evaluate(List<StrategyContext> history)
    {
        Seen.Add(history[^1]);
        return evaluate(history);
    }

    public static DelegateStrategy Hold() => new(_ => Signal.Hold);

    /// <summary>Buys on the n-th evaluated candle (0-based) and never sells.</summary>
    public static DelegateStrategy BuyAt(int n) => new(h => h.Count == n + 1 ? Signal.Buy : Signal.Hold);
}

internal sealed class RsiStrategy : IStrategy
{
    public void Initialize(IEnumerable<Candle> history) { }

    public Signal Evaluate(List<StrategyContext> history)
    {
        var context = history[^1];
        var rsi     = context.Indicators.RSI;

        if (rsi < 40 && !context.Portfolio.HasOpenPosition) return Signal.Buy;
        if (rsi > 60 &&  context.Portfolio.HasOpenPosition) return Signal.Sell;
        return Signal.Hold;
    }
}

/// <summary>Writes into the indicator snapshot it receives (allowed by the API).</summary>
internal sealed class MutatingStrategy : IStrategy
{
    public void Initialize(IEnumerable<Candle> history) { }

    public Signal Evaluate(List<StrategyContext> history)
    {
        var indicators = history[^1].Indicators;
        indicators.RSI = 50m;
        indicators.Custom["score"] = history[^1].Candle.Close;
        indicators.Custom["k" + (history.Count % 50)] = 1m;
        return Signal.Hold;
    }
}
