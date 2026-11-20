using HermesTrade.Core.Models;
using HermesTrade.Utilities;

namespace HermesTrade.Engine;

/// <summary>
/// Orchestrates a full backtest run: loads data, initialises the strategy and
/// indicators, iterates over candles, executes trades, and returns metrics.
/// </summary>
public sealed class BacktestEngine
{
    private readonly Interfaces.IMarketDataProvider _dataProvider;
    private readonly Interfaces.IIndicatorService   _indicatorService;

    public BacktestEngine(
        Interfaces.IMarketDataProvider dataProvider,
        Interfaces.IIndicatorService   indicatorService)
    {
        _dataProvider     = dataProvider     ?? throw new ArgumentNullException(nameof(dataProvider));
        _indicatorService = indicatorService ?? throw new ArgumentNullException(nameof(indicatorService));
    }

    /// <summary>
    /// Loads market data and pre-computes all indicators for <paramref name="config"/>
    /// once. The result can be reused across many
    /// <see cref="Run(Interfaces.IStrategy, PrecomputedBacktestData, Configuration.BacktestConfig, CancellationToken)"/>
    /// calls (e.g. inside a genetic-algorithm fitness function) to avoid redundant I/O and
    /// computation, for any backtest window inside [config.From, config.To].
    /// </summary>
    public async Task<PrecomputedBacktestData> PrecomputeAsync(
        Configuration.BacktestConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        var candles = await LoadCandlesAsync(config, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new PrecomputedBacktestData
        {
            Candles   = candles,
            Snapshots = ComputeSnapshots(candles),
            Symbol    = config.Symbol,
            From      = config.From,
            To        = config.To,
        };
    }

    private async Task<List<Candle>> LoadCandlesAsync(
        Configuration.BacktestConfig config,
        CancellationToken cancellationToken)
    {
        // Extra history before From so indicators are warmed up on the first in-window candle
        var candleSeq = await _dataProvider
            .GetHistoricalDataAsync(config.Symbol, DataStart(config), config.To, cancellationToken)
            .ConfigureAwait(false);

        return candleSeq.OrderBy(c => c.Timestamp).ToList();
    }

    private static DateTime DataStart(Configuration.BacktestConfig config) =>
        config.From - DateTime.MinValue > config.IndicatorWarmup
            ? config.From - config.IndicatorWarmup
            : DateTime.SpecifyKind(DateTime.MinValue, config.From.Kind);

    // IIndicatorService is stateful (Initialize, then GetSnapshot), so concurrent
    // RunAsync/PrecomputeAsync calls must not interleave on the same service.
    private IndicatorSnapshot[] ComputeSnapshots(IReadOnlyList<Candle> candles)
    {
        lock (_indicatorService)
        {
            _indicatorService.Initialize(candles);
            return (_indicatorService as Indicators.DefaultIndicatorService)?.GetAllSnapshots()
                   ?? BuildSnapshotsFromService(candles);
        }
    }

    private IndicatorSnapshot[] BuildSnapshotsFromService(IReadOnlyList<Core.Models.Candle> candles)
    {
        var arr = new IndicatorSnapshot[candles.Count];
        for (int i = 0; i < candles.Count; i++)
            arr[i] = _indicatorService.GetSnapshot(i);
        return arr;
    }

    /// <summary>
    /// Runs only the backtest loop using pre-loaded data (no I/O, no indicator
    /// recomputation). Prefer this overload inside tight loops such as GA fitness
    /// evaluation. Safe to call concurrently on the same <paramref name="data"/>,
    /// provided each call uses its own <paramref name="strategy"/> instance.
    /// </summary>
    public BacktestResult Run(
        Interfaces.IStrategy strategy,
        PrecomputedBacktestData data,
        Configuration.BacktestConfig config) =>
        Run(strategy, data, config, CancellationToken.None);

    /// <inheritdoc cref="Run(Interfaces.IStrategy, PrecomputedBacktestData, Configuration.BacktestConfig)"/>
    /// <param name="strategy">The strategy to evaluate.</param>
    /// <param name="data">Data returned by <see cref="PrecomputeAsync"/> (or built by hand).</param>
    /// <param name="config">Backtest window and settings; the window must lie inside the precomputed range.</param>
    /// <param name="cancellationToken">Checked on every candle.</param>
    public BacktestResult Run(
        Interfaces.IStrategy strategy,
        PrecomputedBacktestData data,
        Configuration.BacktestConfig config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        ValidateData(data, config);

        if (data.Candles.Count == 0)
            return EmptyResult();

        return RunLoop(strategy, data.Candles, data.Snapshots, config, cancellationToken);
    }

    private static void ValidateData(PrecomputedBacktestData data, Configuration.BacktestConfig config)
    {
        if (data.Candles is null || data.Snapshots is null)
            throw new ArgumentException("Candles and Snapshots must not be null.", nameof(data));

        if (data.Snapshots.Length != data.Candles.Count)
            throw new ArgumentException(
                $"Snapshots must have exactly one entry per candle (candles={data.Candles.Count}, snapshots={data.Snapshots.Length}).",
                nameof(data));

        for (int i = 1; i < data.Candles.Count; i++)
            if (data.Candles[i].Timestamp < data.Candles[i - 1].Timestamp)
                throw new ArgumentException("Candles must be sorted ascending by Timestamp.", nameof(data));

        // Data built by PrecomputeAsync records what it was loaded for
        if (data.Symbol is not null && !string.Equals(data.Symbol, config.Symbol, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Data was precomputed for '{data.Symbol}', not '{config.Symbol}'.", nameof(config));

        if ((data.From is DateTime from && config.From < from) || (data.To is DateTime to && config.To > to))
            throw new ArgumentException(
                $"The backtest window [{config.From:O}, {config.To:O}] is outside the precomputed range [{data.From:O}, {data.To:O}].",
                nameof(config));
    }

    /// <summary>
    /// Runs a full backtest for the given strategy and configuration.
    /// </summary>
    public async Task<BacktestResult> RunAsync(
        Interfaces.IStrategy       strategy,
        Configuration.BacktestConfig config,
        CancellationToken          cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        // ── 1. Load market data (including indicator warm-up) ────────────────
        var candles = await LoadCandlesAsync(config, cancellationToken).ConfigureAwait(false);
        if (candles.Count == 0)
            return EmptyResult();

        // ── 2. Pre-compute indicators ─────────────────────────────────────────
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = ComputeSnapshots(candles);

        // ── 3. Backtest loop (initialises the strategy) ───────────────────────
        return RunLoop(strategy, candles, snapshots, config, cancellationToken);
    }

    private static BacktestResult RunLoop(
        Interfaces.IStrategy strategy,
        IReadOnlyList<Core.Models.Candle> candles,
        IndicatorSnapshot[] snapshots,
        Configuration.BacktestConfig config,
        CancellationToken cancellationToken)
    {
        var portfolio = new PortfolioState
        {
            Cash          = config.InitialCapital,
            CurrentEquity = config.InitialCapital,
        };

        var openPosition    = new Position();
        var completedTrades = new List<Trade>();
        var equityCurve     = new List<decimal>(candles.Count);
        var rangeCandles    = new List<Candle>(candles.Count);
        var history         = new List<StrategyContext>(candles.Count);
        StrategyContext? prev = null;

        // Candles before From only serve as warm-up: Initialize gets exactly
        // those, never candles inside or after the backtest window.
        int start = 0;
        while (start < candles.Count && candles[start].Timestamp < config.From)
            start++;

        strategy.Initialize(new CandleWindow(candles, start));

        // `i` indexes the full series so it stays aligned with `snapshots[i]`
        for (int i = start; i < candles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candle = candles[i];
            if (candle.Timestamp > config.To)
                break;

            if (candle.Close <= 0m)
                throw new InvalidOperationException(
                    $"Invalid data: non-positive close price {candle.Close} for {config.Symbol} " +
                    $"at {candle.Timestamp:O}. The backtest engine requires Close > 0.");

            // Update unrealised equity
            portfolio.CurrentEquity = MarkToMarket(portfolio, openPosition, candle);

            var context = new StrategyContext
            {
                Candle     = candle,
                Indicators = snapshots[i].Clone(),
                Portfolio  = portfolio.Clone(),
                History    = new CandleWindow(candles, i + 1),
                Prev       = prev,
            };

            history.Add(context);
            prev = context;

            var signal = strategy.Evaluate(history);

            // ── Execute signal ────────────────────────────────────────────────

            switch (signal)
            {
                case Core.Enums.Signal.Buy when !portfolio.HasOpenPosition:
                    ExecuteBuy(candle, portfolio, openPosition, config);
                    break;

                case Core.Enums.Signal.Sell when portfolio.HasOpenPosition:
                    var trade = ExecuteSell(candle, portfolio, openPosition, config);
                    completedTrades.Add(trade);
                    break;
            }

            // Equity after this candle's trade (reflects the entry fee on a buy)
            portfolio.CurrentEquity = MarkToMarket(portfolio, openPosition, candle);
            equityCurve.Add(portfolio.CurrentEquity);
            rangeCandles.Add(candle);
        }

        // Close any open position at the last close price inside the window
        if (openPosition.IsOpen)
        {
            completedTrades.Add(ExecuteSell(rangeCandles[^1], portfolio, openPosition, config));
            equityCurve[^1] = portfolio.CurrentEquity;
        }

        // Annualise from the whole loaded series (warm-up included): a longer span gives a
        // steadier estimate, and every window over the same data gets the same factor.
        var periodsPerYear = config.PeriodsPerYear ?? MetricsCalculator.InferPeriodsPerYear(candles);

        return MetricsCalculator.Compute(completedTrades, equityCurve, rangeCandles, config.InitialCapital, periodsPerYear);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static decimal MarkToMarket(PortfolioState portfolio, Position position, Candle candle) =>
        portfolio.Cash + (position.IsOpen ? position.Size * candle.Close : 0m);

    private static void ExecuteBuy(
        Candle candle,
        PortfolioState portfolio,
        Position position,
        Configuration.BacktestConfig config)
    {
        // investAmount covers notional + entry fee, so Trade.Profit (fee on
        // entry notional) matches the cash actually spent.
        var investAmount = portfolio.Cash * config.PositionSizeFraction;
        var notional     = investAmount / (1m + config.Fees);
        var size         = notional / candle.Close;

        portfolio.Cash           -= investAmount;
        portfolio.HasOpenPosition = true;
        portfolio.PositionSize    = size;
        portfolio.EntryPrice      = candle.Close;

        position.Open(candle.Timestamp, candle.Close, size);
    }

    private static Trade ExecuteSell(
        Candle candle,
        PortfolioState portfolio,
        Position position,
        Configuration.BacktestConfig config)
    {
        var trade    = position.Close(candle.Timestamp, candle.Close, config.Fees, config.Fees);
        var proceeds = trade.ExitPrice * trade.PositionSize
                       * (1m - config.Fees);

        portfolio.Cash           += proceeds;
        portfolio.HasOpenPosition = false;
        portfolio.PositionSize    = 0m;
        portfolio.EntryPrice      = 0m;
        portfolio.CurrentEquity   = portfolio.Cash;

        return trade;
    }

    private static BacktestResult EmptyResult() =>
        new()
        {
            TotalProfit  = 0m,
            TotalTrades  = 0,
            WinRate      = 0,
            EquityCurve  = [],
            Trades       = [],
            Candles       = new List<Candle>(0),
            MaxDrawdown  = 0m,
            SharpeRatio  = 0,
        };

    /// <summary>
    /// Read-only view of the first <c>count</c> candles, so a strategy can only
    /// see data up to the candle it is evaluating (no look-ahead). Implements
    /// <see cref="IList{T}"/> so LINQ (Last, Count, Skip, TakeLast...) stays O(1)/O(k).
    /// </summary>
    private sealed class CandleWindow(IReadOnlyList<Candle> source, int count) : IList<Candle>, IReadOnlyList<Candle>
    {
        public int Count => count;

        public bool IsReadOnly => true;

        public Candle this[int index]
        {
            get => (uint)index < (uint)count
                ? source[index]
                : throw new ArgumentOutOfRangeException(nameof(index));
            set => throw new NotSupportedException();
        }

        public int IndexOf(Candle item)
        {
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(source[i], item))
                    return i;
            return -1;
        }

        public bool Contains(Candle item) => IndexOf(item) >= 0;

        public void CopyTo(Candle[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            if (array.Length - arrayIndex < count)
                throw new ArgumentException("Destination array is not long enough.", nameof(array));

            for (int i = 0; i < count; i++)
                array[arrayIndex + i] = source[i];
        }

        public IEnumerator<Candle> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return source[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        void IList<Candle>.Insert(int index, Candle item) => throw new NotSupportedException();
        void IList<Candle>.RemoveAt(int index)            => throw new NotSupportedException();
        void ICollection<Candle>.Add(Candle item)         => throw new NotSupportedException();
        void ICollection<Candle>.Clear()                  => throw new NotSupportedException();
        bool ICollection<Candle>.Remove(Candle item)      => throw new NotSupportedException();
    }
}
