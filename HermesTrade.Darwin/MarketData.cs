using HermesTrade.Configuration;
using HermesTrade.Data;
using HermesTrade.Engine;
using HermesTrade.Indicators;

namespace HermesTrade.Darwin;

/// <summary>One symbol's candles, HermesTrade indicators and the network's feature matrix.</summary>
public sealed class SymbolData
{
    public required string Symbol { get; init; }
    public required PrecomputedBacktestData Data { get; init; }
    public required double[][] Features { get; init; }
    public required bool[] Ready { get; init; }
    public required double[] Sigma { get; init; }

    /// <summary>365 for crypto (trades every day), 252 otherwise.</summary>
    public double PeriodsPerYear => Symbol.EndsWith("-USD", StringComparison.OrdinalIgnoreCase) ? 365 : 252;

    public DateTime FirstDate => Data.Candles[0].Timestamp;
    public DateTime LastDate => Data.Candles[^1].Timestamp;
}

public static class MarketData
{
    /// <summary>
    /// Downloads (or reads from the cache) every symbol for [from, to] plus indicator warm-up.
    /// Nothing after <paramref name="to"/> is ever loaded, so code working on the result
    /// physically cannot see later candles.
    /// </summary>
    public static async Task<List<SymbolData>> LoadAsync(
        BacktestEngine engine, IEnumerable<string> symbols, DateTime from, DateTime to)
    {
        var tasks = symbols.Select(async symbol =>
        {
            try
            {
                return await LoadOneAsync(engine, symbol, from, to);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! {symbol}: {ex.Message}");
                return null;
            }
        });

        var loaded = await Task.WhenAll(tasks);
        return loaded.Where(d => d is not null).Select(d => d!).ToList();
    }

    public static async Task<SymbolData> LoadOneAsync(BacktestEngine engine, string symbol, DateTime from, DateTime to)
    {
        var data = await engine.PrecomputeAsync(new BacktestConfig
        {
            Symbol = symbol,
            From = from,
            To = to,
            IndicatorWarmup = TimeSpan.FromDays(500),
        });

        if (data.Candles.Count == 0)
            throw new InvalidOperationException("no candles");

        var features = Features.Build(data.Candles, data.Snapshots, out var ready, out var sigma);
        return new SymbolData { Symbol = symbol, Data = data, Features = features, Ready = ready, Sigma = sigma };
    }

    /// <summary>
    /// Engine wired to Yahoo Finance with dividend-adjusted prices (total return, fair to
    /// buy &amp; hold) and a fresh indicator service per call (PrecomputeAsync serialises on it).
    /// </summary>
    public static BacktestEngine CreateEngine(string cacheDirectory) =>
        new(new YahooFinanceDataProvider(new FileCacheService(cacheDirectory), useDividendAdjustedPrices: true),
            new DefaultIndicatorService());
}
