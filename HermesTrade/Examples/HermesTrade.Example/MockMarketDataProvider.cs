using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

namespace HermesTrade.Example;

/// <summary>
/// Implementación de ejemplo de IMarketDataProvider que genera datos sintéticos
/// para demostrar el uso del BacktestEngine.
/// 
/// Este proveedor crea una serie de precios simulando un mercado con tendencia
/// alcista y oscilaciones que generarán señales RSI.
/// </summary>
public sealed class MockMarketDataProvider : IMarketDataProvider
{
    public Task<IEnumerable<Candle>> GetHistoricalDataAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var candles = new List<Candle>();
        var random = new Random(42); // Semilla fija para reproducibilidad
        
        var currentDate = from;
        var basePrice = 40_000m; // Precio inicial BTC
        var currentPrice = basePrice;
        
        Console.WriteLine($"Generando datos de mercado sintéticos para {symbol}...");
        
        while (currentDate <= to)
        {
            // Generar variación de precio con tendencia y volatilidad
            var trendComponent = Math.Sin((currentDate - from).TotalDays * 0.05) * 0.02;
            var randomComponent = (decimal)(random.NextDouble() - 0.5) * 0.04m;
            var priceChange = currentPrice * (decimal)trendComponent + currentPrice * randomComponent;
            
            currentPrice += priceChange;
            
            // Asegurar que el precio no sea negativo
            if (currentPrice < 1000m)
                currentPrice = 1000m;
            
            // Generar velas OHLC con algo de volatilidad intradiaria
            var dailyVolatility = currentPrice * 0.02m; // 2% de volatilidad diaria
            
            var open = currentPrice + (decimal)(random.NextDouble() - 0.5) * dailyVolatility;
            var high = Math.Max(open, currentPrice) + (decimal)random.NextDouble() * dailyVolatility;
            var low = Math.Min(open, currentPrice) - (decimal)random.NextDouble() * dailyVolatility;
            var close = currentPrice;
            var volume = 1000m + (decimal)random.NextDouble() * 5000m;
            
            candles.Add(new Candle(
                timestamp: currentDate,
                open: Math.Round(open, 2),
                high: Math.Round(high, 2),
                low: Math.Round(low, 2),
                close: Math.Round(close, 2),
                volume: Math.Round(volume, 8)
            ));
            
            currentDate = currentDate.AddDays(1);
        }
        
        Console.WriteLine($"✓ Generadas {candles.Count} velas desde {from:yyyy-MM-dd} hasta {to:yyyy-MM-dd}");
        Console.WriteLine($"  Precio inicial: ${candles.First().Close:N2}");
        Console.WriteLine($"  Precio final:   ${candles.Last().Close:N2}");
        Console.WriteLine($"  Variación:      {((candles.Last().Close - candles.First().Close) / candles.First().Close * 100):+0.00;-0.00}%\n");
        
        return Task.FromResult<IEnumerable<Candle>>(candles);
    }
}
