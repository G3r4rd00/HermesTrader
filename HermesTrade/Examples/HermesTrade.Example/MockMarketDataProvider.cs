using HermesTrade.Core.Models;
using HermesTrade.Interfaces;

namespace HermesTrade.Example;

/// <summary>
/// Implementación de ejemplo de IMarketDataProvider que genera datos sintéticos
/// para demostrar el uso del BacktestEngine.
///
/// Este proveedor crea una serie de precios simulando un mercado con tendencia
/// alcista suave, un ciclo de unos 126 días y oscilaciones aleatorias que generarán
/// señales RSI. La serie parte siempre de la misma fecha fija, así que el precio de
/// un día no depende del rango pedido.
/// </summary>
public sealed class MockMarketDataProvider : IMarketDataProvider
{
    private static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public Task<IEnumerable<Candle>> GetHistoricalDataAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new ArgumentException("'from' no puede ser posterior a 'to'.", nameof(from));

        var candles = new List<Candle>();
        var random = new Random(42); // Semilla fija para reproducibilidad

        var currentDate = from < Epoch ? from.Date : Epoch;
        var logPrice = Math.Log(40_000); // Precio inicial BTC

        Console.WriteLine($"Generando datos de mercado sintéticos para {symbol}...");

        while (currentDate <= to)
        {
            // Log-retornos: deriva positiva (tendencia alcista) + ciclo + ruido.
            // Al trabajar en log la deriva no se pierde por el efecto de la volatilidad.
            var drift = 0.0003;
            var cycleComponent = Math.Sin((currentDate - Epoch).TotalDays * 0.05) * 0.01;
            var randomComponent = (random.NextDouble() - 0.5) * 0.04;
            logPrice += drift + cycleComponent + randomComponent;

            var currentPrice = (decimal)Math.Exp(logPrice);

            // Generar velas OHLC con algo de volatilidad intradiaria
            var dailyVolatility = currentPrice * 0.02m; // 2% de volatilidad diaria

            var open = currentPrice + (decimal)(random.NextDouble() - 0.5) * dailyVolatility;
            var high = Math.Max(open, currentPrice) + (decimal)random.NextDouble() * dailyVolatility;
            var low = Math.Min(open, currentPrice) - (decimal)random.NextDouble() * dailyVolatility;
            var close = currentPrice;
            var volume = 1000m + (decimal)random.NextDouble() * 5000m;

            if (currentDate >= from)
            {
                candles.Add(new Candle(
                    timestamp: currentDate,
                    open: Math.Round(open, 2),
                    high: Math.Round(high, 2),
                    low: Math.Round(low, 2),
                    close: Math.Round(close, 2),
                    volume: Math.Round(volume, 8)
                ));
            }

            currentDate = currentDate.AddDays(1);
        }

        Console.WriteLine($"✓ Generadas {candles.Count} velas desde {from:yyyy-MM-dd} hasta {to:yyyy-MM-dd}");
        if (candles.Count > 0)
        {
            Console.WriteLine($"  Precio inicial: ${candles.First().Close:N2}");
            Console.WriteLine($"  Precio final:   ${candles.Last().Close:N2}");
            Console.WriteLine($"  Variación:      {((candles.Last().Close - candles.First().Close) / candles.First().Close * 100):+0.00;-0.00}%\n");
        }

        return Task.FromResult<IEnumerable<Candle>>(candles);
    }
}
