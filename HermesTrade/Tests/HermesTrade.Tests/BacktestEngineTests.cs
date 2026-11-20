using System.Collections.Concurrent;
using System.Diagnostics;
using HermesTrade.Configuration;
using HermesTrade.Core.Enums;
using HermesTrade.Core.Models;
using HermesTrade.Engine;
using HermesTrade.Indicators;
using HermesTrade.Utilities;
using static HermesTrade.Tests.TestData;

namespace HermesTrade.Tests;

public class BacktestEngineTests
{
    private static readonly List<Candle> Series = Walk(300);

    // Window days 100..199: candles before it are warm-up, candles after it must be ignored
    private static readonly BacktestConfig Window = Config(Start.AddDays(100), Start.AddDays(199));

    private static BacktestEngine Engine(IReadOnlyList<Candle> candles) =>
        new(new FixedProvider(candles), new DefaultIndicatorService());

    // ── Windowing and look-ahead ──────────────────────────────────────────────

    [Fact]
    public async Task Indicators_are_aligned_with_the_candle_being_evaluated()
    {
        var strategy = DelegateStrategy.Hold();
        await Engine(Series).RunAsync(strategy, Window);

        var first    = strategy.Seen[0];
        var index    = Series.FindIndex(c => c.Timestamp == first.Candle.Timestamp);
        var expected = Series.Skip(index - 9).Take(10).Average(c => c.Close);

        Assert.Equal(Window.From, first.Candle.Timestamp);
        Assert.NotNull(first.Indicators.SMA10);
        Assert.Equal(expected, first.Indicators.SMA10!.Value, 6);
    }

    [Fact]
    public async Task History_never_contains_future_candles()
    {
        var strategy = DelegateStrategy.Hold();
        await Engine(Series).RunAsync(strategy, Window);

        Assert.All(strategy.Seen, c => Assert.Equal(c.Candle, c.History[^1]));
        Assert.All(strategy.Seen, c => Assert.Equal(c.History.Count - 1, c.History.ToList().IndexOf(c.Candle)));
    }

    [Fact]
    public async Task Initialize_only_receives_candles_before_From()
    {
        var strategy = DelegateStrategy.Hold();
        await Engine(Series).RunAsync(strategy, Window);

        Assert.Equal(100, strategy.InitializeHistory.Count);
        Assert.All(strategy.InitializeHistory, c => Assert.True(c.Timestamp < Window.From));
    }

    [Fact]
    public async Task Prev_points_to_the_previous_context()
    {
        var strategy = DelegateStrategy.Hold();
        await Engine(Series).RunAsync(strategy, Window);

        Assert.Null(strategy.Seen[0].Prev);
        Assert.Same(strategy.Seen[0], strategy.Seen[1].Prev);
    }

    [Fact]
    public async Task Portfolio_in_earlier_contexts_is_a_snapshot()
    {
        bool? firstContextOpenAtSecondCandle = null;
        var strategy = new DelegateStrategy(h =>
        {
            if (h.Count == 2) firstContextOpenAtSecondCandle = h[0].Portfolio.HasOpenPosition;
            return h.Count == 1 ? Signal.Buy : Signal.Hold;
        });

        await Engine(Series).RunAsync(strategy, Window);

        Assert.False(firstContextOpenAtSecondCandle);
    }

    [Fact]
    public async Task Open_position_is_closed_at_the_last_candle_inside_the_window()
    {
        var result = await Engine(Series).RunAsync(DelegateStrategy.BuyAt(0), Window);

        var trade = Assert.Single(result.Trades);
        Assert.Equal(Window.To, trade.ExitTime);
        Assert.Equal(Series.Single(c => c.Timestamp == Window.To).Close, trade.ExitPrice);
        Assert.Equal(100, result.Candles.Count);
        Assert.Equal(100, result.EquityCurve.Count);
    }

    // ── Accounting ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0, 0.001)]
    [InlineData(0.5, 0.0015)]
    [InlineData(0.3, 0.0)]
    public async Task TotalProfit_matches_the_equity_change(double fraction, double fees)
    {
        var config = new BacktestConfig
        {
            Symbol               = "TEST",
            From                 = Window.From,
            To                   = Window.To,
            Fees                 = (decimal)fees,
            PositionSizeFraction = (decimal)fraction,
            IndicatorWarmup      = TimeSpan.Zero,
        };
        var strategy = new DelegateStrategy(h => (h.Count % 7) switch { 1 => Signal.Buy, 4 => Signal.Sell, _ => Signal.Hold });

        var result = await Engine(Series).RunAsync(strategy, config);

        Assert.True(result.TotalTrades > 5);
        Assert.Equal(result.EquityCurve[^1] - config.InitialCapital, result.TotalProfit, 18);
    }

    [Fact]
    public async Task Equity_on_the_buy_candle_reflects_the_entry_fee()
    {
        var result = await Engine(Series).RunAsync(DelegateStrategy.BuyAt(0), Window);

        var expected = Window.InitialCapital - Window.InitialCapital * Window.Fees / (1m + Window.Fees);
        Assert.Equal(expected, result.EquityCurve[0], 10);
    }

    [Fact]
    public async Task Non_positive_close_gives_a_clear_error()
    {
        var candles = Walk(60);
        candles[30] = new Candle(candles[30].Timestamp, 1, 1, 1, 0m, 1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Engine(candles).RunAsync(DelegateStrategy.BuyAt(30), Config(Start, Start.AddDays(59))));

        Assert.Contains("non-positive close", error.Message);
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    public static TheoryData<BacktestConfig> InvalidConfigs() => new()
    {
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), PositionSizeFraction = 0m },
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), PositionSizeFraction = 2m },
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), Fees = -1m },
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), Fees = 1.5m },
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), InitialCapital = -1000m },
        new BacktestConfig { Symbol = "X", From = Start.AddDays(9), To = Start },
        new BacktestConfig { Symbol = " ", From = Start, To = Start.AddDays(9) },
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), IndicatorWarmup = TimeSpan.FromDays(-1) },
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), PeriodsPerYear = 0 },
        new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(9), PeriodsPerYear = double.NaN },
    };

    [Theory]
    [MemberData(nameof(InvalidConfigs))]
    public async Task Invalid_configs_are_rejected(BacktestConfig config)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Engine(Series).RunAsync(DelegateStrategy.Hold(), config));
    }

    [Fact]
    public async Task Small_rebate_and_partial_size_are_valid()
    {
        var config = new BacktestConfig { Symbol = "X", From = Window.From, To = Window.To, Fees = -0.0002m, PositionSizeFraction = 0.5m };

        var result = await Engine(Series).RunAsync(new RsiStrategy(), config);

        Assert.Equal(100, result.Candles.Count);
    }

    // ── Indicator warm-up ─────────────────────────────────────────────────────

    [Fact]
    public async Task Warmup_loads_history_before_From_for_indicators_only()
    {
        var provider = new RangeProvider(Walk(3000, from: Start.AddDays(-1500)));
        var config   = new BacktestConfig { Symbol = "X", From = Start, To = Start.AddDays(365) }; // default 400 days
        var strategy = DelegateStrategy.Hold();

        var result = await new BacktestEngine(provider, new DefaultIndicatorService()).RunAsync(strategy, config);

        Assert.Equal(Start.AddDays(-400), provider.LastFrom);
        Assert.NotNull(strategy.Seen[0].Indicators.SMA200);
        Assert.NotNull(strategy.Seen[0].Indicators.MACDSignal);
        Assert.NotNull(strategy.Seen[0].Indicators.ADX);
        Assert.Equal(366, result.Candles.Count);
        Assert.Equal(Start, result.Candles[0].Timestamp);
    }

    [Fact]
    public async Task Zero_warmup_requests_data_from_From()
    {
        var provider = new RangeProvider(Walk(3000, from: Start.AddDays(-1500)));
        var strategy = DelegateStrategy.Hold();

        await new BacktestEngine(provider, new DefaultIndicatorService())
            .RunAsync(strategy, Config(Start, Start.AddDays(365)));

        Assert.Equal(Start, provider.LastFrom);
        Assert.Null(strategy.Seen[0].Indicators.SMA200);
    }

    [Fact]
    public async Task Warmup_does_not_underflow_near_DateTime_MinValue()
    {
        var provider = new RangeProvider(Walk(100, from: new DateTime(1, 1, 1)));
        var config   = new BacktestConfig { Symbol = "X", From = new DateTime(1, 2, 1), To = new DateTime(1, 3, 1) };

        var result = await new BacktestEngine(provider, new DefaultIndicatorService()).RunAsync(DelegateStrategy.Hold(), config);

        Assert.Equal(DateTime.MinValue, provider.LastFrom);
        Assert.NotEmpty(result.Candles);
    }

    // ── Precomputed data and Run() ────────────────────────────────────────────

    [Fact]
    public async Task Run_matches_RunAsync()
    {
        var engine = Engine(Series);
        var data   = await engine.PrecomputeAsync(Window);

        var viaRun   = engine.Run(DelegateStrategy.BuyAt(0), data, Window);
        var viaAsync = await engine.RunAsync(DelegateStrategy.BuyAt(0), Window);

        Assert.Equal(viaAsync.TotalProfit, viaRun.TotalProfit);
        Assert.Equal(viaAsync.EquityCurve, viaRun.EquityCurve);
    }

    [Fact]
    public async Task Runs_over_the_same_data_are_isolated()
    {
        var engine = Engine(Series);
        var config = Config(Start.AddDays(50), Start.AddDays(299));
        var data   = await engine.PrecomputeAsync(config);

        var before = engine.Run(new RsiStrategy(), data, config);
        engine.Run(new MutatingStrategy(), data, config);
        var after  = engine.Run(new RsiStrategy(), data, config);
        var reader = new DelegateStrategy(_ => Signal.Hold);
        engine.Run(reader, data, config);

        Assert.Equal(before.TotalProfit, after.TotalProfit);
        Assert.Equal(before.TotalTrades, after.TotalTrades);
        Assert.All(reader.Seen, c => Assert.False(c.Indicators.Custom.ContainsKey("score")));
    }

    [Fact]
    public async Task Parallel_runs_over_the_same_data_are_isolated()
    {
        var engine   = Engine(Series);
        var config   = Config(Start.AddDays(50), Start.AddDays(299));
        var data     = await engine.PrecomputeAsync(config);
        var baseline = engine.Run(new RsiStrategy(), data, config).TotalProfit;

        var errors  = new ConcurrentBag<Exception>();
        var profits = new ConcurrentBag<decimal>();
        Parallel.For(0, 64, k =>
        {
            try
            {
                if (k % 2 == 0) engine.Run(new MutatingStrategy(), data, config);
                else profits.Add(engine.Run(new RsiStrategy(), data, config).TotalProfit);
            }
            catch (Exception e) { errors.Add(e); }
        });

        Assert.Empty(errors);
        Assert.All(profits, p => Assert.Equal(baseline, p));
    }

    [Fact]
    public async Task Concurrent_runs_on_one_engine_do_not_mix_indicators()
    {
        var provider = new MultiProvider(new() { ["A"] = Walk(2000, 100m, 1), ["B"] = Walk(2500, 50_000m, 2) });
        var engine   = new BacktestEngine(provider, new DefaultIndicatorService());
        var mismatches = 0;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(k => Task.Run(async () =>
        {
            var config   = new BacktestConfig { Symbol = k % 2 == 0 ? "A" : "B", From = Start.AddDays(100), To = Start.AddDays(1999), IndicatorWarmup = TimeSpan.Zero };
            var strategy = new DelegateStrategy(h =>
            {
                var c = h[^1];
                if (c.Indicators.SMA10 is decimal sma && Math.Abs(sma - c.History.TakeLast(10).Average(x => x.Close)) > 1e-6m * sma)
                    Interlocked.Increment(ref mismatches);
                return Signal.Hold;
            });

            if (k % 3 == 0) engine.Run(strategy, await engine.PrecomputeAsync(config), config);
            else await engine.RunAsync(strategy, config);
        })));

        Assert.Equal(0, mismatches);
    }

    [Fact]
    public async Task Run_rejects_snapshots_that_do_not_match_the_candles()
    {
        var engine = Engine(Series);
        var data   = await engine.PrecomputeAsync(Window);
        var broken = new PrecomputedBacktestData { Candles = data.Candles, Snapshots = data.Snapshots[..^1] };

        Assert.Throws<ArgumentException>(() => engine.Run(DelegateStrategy.Hold(), broken, Window));
    }

    [Fact]
    public async Task Run_rejects_unsorted_candles()
    {
        var engine   = Engine(Series);
        var data     = await engine.PrecomputeAsync(Window);
        var unsorted = new PrecomputedBacktestData { Candles = data.Candles.Reverse().ToList(), Snapshots = data.Snapshots };

        Assert.Throws<ArgumentException>(() => engine.Run(DelegateStrategy.Hold(), unsorted, Window));
    }

    [Fact]
    public async Task Run_rejects_a_symbol_or_window_the_data_was_not_loaded_for()
    {
        var engine = Engine(Series);
        var data   = await engine.PrecomputeAsync(Window);

        var otherSymbol = new BacktestConfig { Symbol = "OTHER", From = Window.From, To = Window.To, IndicatorWarmup = TimeSpan.Zero };
        var tooEarly    = Config(Window.From.AddDays(-1), Window.To);
        var tooLate     = Config(Window.From, Window.To.AddDays(1));
        var inside      = Config(Window.From.AddDays(10), Window.To.AddDays(-10));

        Assert.Throws<ArgumentException>(() => engine.Run(DelegateStrategy.Hold(), data, otherSymbol));
        Assert.Throws<ArgumentException>(() => engine.Run(DelegateStrategy.Hold(), data, tooEarly));
        Assert.Throws<ArgumentException>(() => engine.Run(DelegateStrategy.Hold(), data, tooLate));
        Assert.Equal(80, engine.Run(DelegateStrategy.Hold(), data, inside).Candles.Count);
    }

    [Fact]
    public async Task History_supports_fast_LINQ()
    {
        var candles = Walk(40_000, seed: 3);
        var config  = Config(Start, Start.AddDays(40_000));
        var engine  = Engine(candles);
        var data    = await engine.PrecomputeAsync(config);
        var strategy = new DelegateStrategy(h =>
        {
            var history = h[^1].History;
            Assert.IsAssignableFrom<IList<Candle>>(history);
            var average = history.TakeLast(20).Average(c => c.Close);
            return history.Last().Close < average ? Signal.Buy : Signal.Sell;
        });

        var timer = Stopwatch.StartNew();
        engine.Run(strategy, data, config);

        // Linear: ~0.1 s; the old O(n^2) enumeration took several seconds
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"took {timer.Elapsed}");
    }

    [Fact]
    public async Task History_is_read_only()
    {
        var strategy = DelegateStrategy.Hold();
        await Engine(Series).RunAsync(strategy, Window);

        var history = Assert.IsAssignableFrom<IList<Candle>>(strategy.Seen[0].History);
        Assert.True(history.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => history[0] = history[1]);
        Assert.Throws<NotSupportedException>(() => history.Add(history[0]));
    }

    // ── Cancellation and annualisation ────────────────────────────────────────

    [Fact]
    public async Task Run_honours_cancellation()
    {
        var engine = Engine(Series);
        var data   = await engine.PrecomputeAsync(Window);

        Assert.Throws<OperationCanceledException>(() =>
            engine.Run(DelegateStrategy.Hold(), data, Window, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task RunAsync_honours_cancellation_after_loading_data()
    {
        using var cts = new CancellationTokenSource();
        var strategy  = new DelegateStrategy(h => { if (h.Count == 10) cts.Cancel(); return Signal.Hold; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine(Series).RunAsync(strategy, Window, cts.Token));
        Assert.Equal(10, strategy.Seen.Count);
    }

    [Fact]
    public async Task PeriodsPerYear_overrides_the_inferred_annualisation()
    {
        var config = new BacktestConfig { Symbol = "X", From = Window.From, To = Window.To, IndicatorWarmup = TimeSpan.Zero, PeriodsPerYear = 252 };

        var result = await Engine(Series).RunAsync(DelegateStrategy.BuyAt(0), config);

        var expected = MetricsCalculator.ComputeSharpeRatio(result.EquityCurve, config.InitialCapital, 252);
        Assert.Equal(expected, result.SharpeRatio, 12);
    }

    [Fact]
    public async Task Annualisation_is_inferred_from_the_whole_loaded_series()
    {
        var engine = Engine(Series);
        var data   = await engine.PrecomputeAsync(Config(Start, Start.AddDays(299)));
        var narrow = Config(Start.AddDays(10), Start.AddDays(14));

        var result = engine.Run(DelegateStrategy.BuyAt(0), data, narrow);

        var expected = MetricsCalculator.ComputeSharpeRatio(result.EquityCurve, narrow.InitialCapital,
            MetricsCalculator.InferPeriodsPerYear(data.Candles));
        Assert.Equal(expected, result.SharpeRatio, 12);
    }
}
