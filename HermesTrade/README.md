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
using HermesTrade.Engine;
using HermesTrade.Strategies;
using HermesTrade.Data;
using Microsoft.Extensions.Logging;

var config = new BacktestConfig
{
    Symbol         = "BTC-USD",
    Start          = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
    End            = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
    InitialCapital = 10_000m,
    CommissionRate = 0.001m
};

using var httpClient = new HttpClient();
var logger   = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<YahooFinanceDataProvider>();
var cache    = new FileCacheService();
var provider = new YahooFinanceDataProvider(httpClient, cache, logger);
var strategy = new SimpleStrategyExample(oversoldThreshold: 30m, overboughtThreshold: 70m);

var engine = new BacktestEngine();
var result = await engine.RunAsync(config, provider, strategy);

Console.WriteLine($"Net Profit : {result.NetProfit:C}");
Console.WriteLine($"Return     : {result.ReturnPct:P2}");
Console.WriteLine($"Win Rate   : {result.WinRate:P2}");
Console.WriteLine($"Sharpe     : {result.SharpeRatio:F2}");
```

## Implementing a Custom Strategy

```csharp
public class MyStrategy : IStrategy
{
    public void Initialize(IEnumerable<Candle> history) { }

    public Signal Evaluate(StrategyContext context)
    {
        var rsi    = context.Indicators.RSI;
        var hasPos = context.Portfolio.HasOpenPosition;

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

## Requirements

- .NET 8 or later

## License

MIT (c) 2025 HermesTrader

## Author

**Gerardo Tous Vallespir**

- 📧 [gerardotous@gmail.com](mailto:gerardotous@gmail.com)
- 💼 [linkedin.com/in/gerardo-tous-vallespir-42491636](https://www.linkedin.com/in/gerardo-tous-vallespir-42491636/)
