using HermesTrade.Core.Models;
using HermesTrade.Engine;
using HermesTrade.Indicators;
using HermesTrade.Utilities;
using static HermesTrade.Tests.TestData;

namespace HermesTrade.Tests;

public class MetricsCalculatorTests
{
    [Fact]
    public async Task Drawdown_and_Sharpe_start_from_the_initial_capital()
    {
        // 1% fee, buy on the first candle, flat price: 10000 -> 9900.99 -> 9801.98
        var engine = new BacktestEngine(new FixedProvider(Flat(10)), new DefaultIndicatorService());
        var config = Config(Start, Start.AddDays(9), fees: 0.01m);

        var result = await engine.RunAsync(DelegateStrategy.BuyAt(0), config);

        Assert.Equal(198.0198m, result.MaxDrawdown, 4);
        Assert.Equal(-result.MaxDrawdown, result.TotalProfit, 18);

        var equity  = new[] { config.InitialCapital }.Concat(result.EquityCurve).ToArray();
        var returns = equity.Zip(equity.Skip(1), (a, b) => (double)((b - a) / a)).ToArray();
        var mean    = returns.Average();
        var std     = Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (returns.Length - 1));
        Assert.Equal(mean / std * Math.Sqrt(365.25), result.SharpeRatio, 10);
    }

    [Fact]
    public async Task One_candle_loss_shows_up_in_the_drawdown()
    {
        var engine = new BacktestEngine(new FixedProvider(Flat(10)), new DefaultIndicatorService());

        var result = await engine.RunAsync(DelegateStrategy.BuyAt(0), Config(Start, Start, fees: 0.01m));

        Assert.True(result.TotalProfit < 0);
        Assert.Equal(-result.TotalProfit, result.MaxDrawdown, 18);
    }

    [Fact]
    public void Equity_metrics_are_computed_without_trades()
    {
        var candles = Flat(4);
        var equity  = new[] { 10_000m, 12_000m, 6_000m, 7_000m };

        var result = MetricsCalculator.Compute([], equity, candles, 10_000m);

        Assert.Equal(0, result.TotalTrades);
        Assert.Equal(0, result.WinRate);
        Assert.Equal(6_000m, result.MaxDrawdown);
        Assert.NotEqual(0, result.SharpeRatio);
    }

    [Fact]
    public void Periods_per_year_are_inferred_from_the_timestamps()
    {
        var daily  = Walk(366);
        var weekly = Enumerable.Range(0, 53).Select(i => new Candle(Start.AddDays(7 * i), 1, 1, 1, 1, 1)).ToList();

        Assert.Equal(365.25, MetricsCalculator.InferPeriodsPerYear(daily), 6);
        Assert.Equal(52.18, MetricsCalculator.InferPeriodsPerYear(weekly), 2);
        Assert.Equal(252, MetricsCalculator.InferPeriodsPerYear(daily.Take(1).ToList()));
    }
}

public class IndicatorServiceTests
{
    [Fact]
    public void Flat_prices_do_not_crash()
    {
        var service = new DefaultIndicatorService();

        service.Initialize(Flat(300));

        Assert.Equal(300, service.GetAllSnapshots().Length);
    }

    [Fact]
    public void Huge_OBV_becomes_null_instead_of_overflowing()
    {
        // Rising prices with 1e27 volume: cumulative OBV exceeds the decimal range
        var candles = Enumerable.Range(0, 200)
            .Select(i => new Candle(Start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1e27m))
            .ToList();
        var service = new DefaultIndicatorService();

        service.Initialize(candles);

        var snapshots = service.GetAllSnapshots();
        Assert.NotNull(snapshots[10].OBV);
        Assert.Null(snapshots[^1].OBV);
        Assert.NotNull(snapshots[^1].RSI);
    }

    [Fact]
    public void Snapshot_clone_copies_custom_values()
    {
        var snapshot = new IndicatorSnapshot { RSI = 30m };
        snapshot.Custom["x"] = 1m;

        var clone = snapshot.Clone();
        clone.Custom["x"] = 2m;
        clone.RSI = 70m;

        Assert.Equal(1m, snapshot.Custom["x"]);
        Assert.Equal(30m, snapshot.RSI);
    }
}
