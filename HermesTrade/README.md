# HermesTrade

[![NuGet](https://img.shields.io/nuget/v/HermesTrade.svg)](https://www.nuget.org/packages/HermesTrade)
[![NuGet Downloads](https://img.shields.io/nuget/dt/HermesTrade.svg)](https://www.nuget.org/packages/HermesTrade)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Email](https://img.shields.io/badge/Email-gerardotous%40gmail.com-blue?logo=gmail&logoColor=white)](mailto:gerardotous@gmail.com)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-Gerardo%20Tous%20Vallespir-0077B5?logo=linkedin&logoColor=white)](https://www.linkedin.com/in/gerardo-tous-vallespir-42491636/)

A production-ready **.NET 8** backtesting engine for trading strategies on cryptocurrencies and other financial instruments.

## Features

- Candle-by-candle backtest loop via `BacktestEngine`
- Flexible `IStrategy` interface - bring your own logic
- Built-in RSI indicator via `IIndicatorService`
- Yahoo Finance data provider with local file cache
- Portfolio state tracking (open position, cash, trades)
- Performance metrics: net profit, win rate, max drawdown, Sharpe ratio
- Fully configurable via `BacktestConfig`

## Installation

```bash
dotnet add package HermesTrade
```

## Quick Start

```csharp
using HermesTrade.Configuration;
using HermesTrade.Data;
using HermesTrade.Engine;
using HermesTrade.Indicators;
using HermesTrade.Strategies;

var config = new BacktestConfig
{
    Symbol         = "BTC-USD",
    From           = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    To             = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc),
    InitialCapital = 10_000m,
    Fees           = 0.001m   // 0.1% per side
};

var provider = new YahooFinanceDataProvider(new FileCacheService());
var engine   = new BacktestEngine(provider, new DefaultIndicatorService());
var strategy = new SimpleStrategyExample(oversoldThreshold: 30m, overboughtThreshold: 70m);

var result = await engine.RunAsync(strategy, config);

Console.WriteLine($"Net Profit : {result.TotalProfit:C}");
Console.WriteLine($"Return     : {result.TotalProfit / config.InitialCapital:P2}");
Console.WriteLine($"Win Rate   : {result.WinRate:P2}");
Console.WriteLine($"Sharpe     : {result.SharpeRatio:F2}");
```

## Implementing a Custom Strategy

```csharp
using HermesTrade.Core.Enums;
using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

public class MyStrategy : IStrategy
{
    // Warm-up candles before BacktestConfig.From (never future candles)
    public void Initialize(IEnumerable<Candle> history) { }

    // history: contexts evaluated so far in the backtest window, oldest first;
    // the last one is the current candle.
    public Signal Evaluate(List<StrategyContext> history)
    {
        var context = history[^1];
        var rsi     = context.Indicators.RSI;   // null until the indicator is warmed up
        var hasPos  = context.Portfolio.HasOpenPosition;

        if (rsi < 25 && !hasPos) return Signal.Buy;
        if (rsi > 75 &&  hasPos) return Signal.Sell;

        return Signal.Hold;
    }
}
```

## Key Types

| Type | Description |
|------|-------------|
| `BacktestEngine` | Runs the backtest loop |
| `BacktestConfig` | Symbol, dates, capital, commission |
| `IStrategy` | Interface for your strategy |
| `StrategyContext` | Candle + indicators + portfolio on each tick |
| `IMarketDataProvider` | Interface for custom data sources |
| `YahooFinanceDataProvider` | Fetches OHLCV data from Yahoo Finance |
| `FileCacheService` | Persists downloaded data locally |
| `SimpleStrategyExample` | Reference RSI strategy |
| `BacktestResult` | Trades, profit, drawdown, Sharpe |

## Notes

- **Indicator warm-up**: `BacktestConfig.IndicatorWarmup` (default 400 days) loads extra history before `From`
  so indicators such as SMA200 are ready on the first candle. Those candles are never traded and are not part
  of the results; `IStrategy.Initialize` receives exactly them (never candles inside or after the window).
- **Fills**: orders fill at the close of the candle that produced the signal; trade times are that candle's timestamp.
- **Sharpe ratio**: annualised with `BacktestConfig.PeriodsPerYear`, or inferred from the candle timestamps when null.
- **Reuse**: `PrecomputeAsync` + `Run` evaluates many strategies over the same data, also in parallel
  (one strategy instance per call), for any window inside the precomputed range.
- **Yahoo Finance**: daily candles stamped at 00:00 UTC of the exchange's trading date; `From`/`To` are compared
  by date (inclusive). Prices are split-adjusted but not dividend-adjusted unless you pass
  `useDividendAdjustedPrices: true`. Forex symbols (`EURUSD=X`) are not supported.

## Requirements

- .NET 8 or later

## License

MIT (c) 2025 HermesTrader

## Author

**Gerardo Tous Vallespir**

- 📧 [gerardotous@gmail.com](mailto:gerardotous@gmail.com)
- 💼 [linkedin.com/in/gerardo-tous-vallespir-42491636](https://www.linkedin.com/in/gerardo-tous-vallespir-42491636/)
