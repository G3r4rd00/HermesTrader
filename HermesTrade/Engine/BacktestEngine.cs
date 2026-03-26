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
    /// Runs a full backtest for the given strategy and configuration.
    /// </summary>
    public async Task<BacktestResult> RunAsync(
        Interfaces.IStrategy       strategy,
        Configuration.BacktestConfig config,
        CancellationToken          cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(config);

        // ── 1. Load market data ───────────────────────────────────────────────
        var candleSeq = await _dataProvider
            .GetHistoricalDataAsync(config.Symbol, config.From, config.To, cancellationToken)
            .ConfigureAwait(false);

        var candles = candleSeq.OrderBy(c => c.Timestamp).ToList();
        if (candles.Count == 0)
            return EmptyResult();

        // ── 2. Pre-compute indicators ─────────────────────────────────────────
        _indicatorService.Initialize(candles);

        // ── 3. Initialise strategy ────────────────────────────────────────────
        strategy.Initialize(candles);

        // ── 4. Backtest loop ──────────────────────────────────────────────────
        var portfolio = new PortfolioState
        {
            Cash          = config.InitialCapital,
            CurrentEquity = config.InitialCapital,
        };

        var openPosition  = new Position();
        var completedTrades = new List<Trade>();
        var equityCurve   = new List<decimal>(candles.Count);

        for (int i = 0; i < candles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candle = candles[i];

            // Update unrealised equity
            portfolio.CurrentEquity = portfolio.Cash
                + (openPosition.IsOpen
                    ? openPosition.Size * candle.Close
                    : 0m);

            var context = new StrategyContext
            {
                Candle     = candle,
                Indicators = _indicatorService.GetSnapshot(i),
                Portfolio  = portfolio,
                History    = candles,
            };

            var signal = strategy.Evaluate(context);

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

            equityCurve.Add(portfolio.CurrentEquity);
        }

        // Close any open position at the last close price
        if (openPosition.IsOpen)
        {
            var lastCandle = candles[^1];
            var trade = openPosition.Close(lastCandle.Timestamp, lastCandle.Close,
                                           config.Fees, config.Fees);
            portfolio.Cash += trade.ExitPrice * trade.PositionSize
                              - trade.ExitPrice * trade.PositionSize * config.Fees;
            portfolio.HasOpenPosition = false;
            portfolio.PositionSize    = 0m;
            portfolio.EntryPrice      = 0m;
            completedTrades.Add(trade);

            // Update final equity
            portfolio.CurrentEquity = portfolio.Cash;
            if (equityCurve.Count > 0)
                equityCurve[^1] = portfolio.CurrentEquity;
        }

        return MetricsCalculator.Compute(completedTrades, equityCurve, config.InitialCapital);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ExecuteBuy(
        Candle candle,
        PortfolioState portfolio,
        Position position,
        Configuration.BacktestConfig config)
    {
        var investAmount = portfolio.Cash * config.PositionSizeFraction;
        var fee          = investAmount * config.Fees;
        var netInvest    = investAmount - fee;
        var size         = netInvest / candle.Close;

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
            MaxDrawdown  = 0m,
            SharpeRatio  = 0,
        };
}
