using HermesTrade.Configuration;
using HermesTrade.Engine;
using HermesTrade.Indicators;
using HermesTrade.Strategies;
using HermesTrade.Example;

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  HermesTrade - Ejemplo de BacktestEngine");
Console.WriteLine("═══════════════════════════════════════════════════════════\n");

// ── 1. Crear el proveedor de datos de ejemplo ────────────────────────────
var dataProvider = new MockMarketDataProvider();

// ── 2. Crear el servicio de indicadores ──────────────────────────────────
var indicatorService = new DefaultIndicatorService();

// ── 3. Crear el motor de backtesting ─────────────────────────────────────
var backtestEngine = new BacktestEngine(dataProvider, indicatorService);

// ── 4. Configurar el backtest ────────────────────────────────────────────
var config = new BacktestConfig
{
    Symbol = "BTC-USD",
    From = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    To = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc),
    InitialCapital = 10_000m,
    Fees = 0.001m,  // 0.1%
    PositionSizeFraction = 1m  // Invertir 100% del capital disponible
};

// ── 5. Crear la estrategia ───────────────────────────────────────────────
var strategy = new SimpleStrategyExample(
    oversoldThreshold: 30m,
    overboughtThreshold: 70m
);

Console.WriteLine("Configuración del Backtest:");
Console.WriteLine($"  Symbol:            {config.Symbol}");
Console.WriteLine($"  Período:           {config.From:yyyy-MM-dd} a {config.To:yyyy-MM-dd}");
Console.WriteLine($"  Capital inicial:   ${config.InitialCapital:N2}");
Console.WriteLine($"  Comisiones:        {config.Fees:P2}");
Console.WriteLine($"  Estrategia:        RSI (Compra<30, Venta>70)");
Console.WriteLine("\nEjecutando backtest...\n");

// ── 6. Ejecutar el backtest ──────────────────────────────────────────────
try
{
    var result = await backtestEngine.RunAsync(strategy, config);

    // ── 7. Mostrar resultados ────────────────────────────────────────────
    Console.WriteLine("═══════════════════════════════════════════════════════════");
    Console.WriteLine("  RESULTADOS DEL BACKTEST");
    Console.WriteLine("═══════════════════════════════════════════════════════════\n");

    Console.WriteLine($"Total de operaciones: {result.TotalTrades}");
    Console.WriteLine($"Beneficio neto:       ${result.TotalProfit:N2}");
    Console.WriteLine($"Retorno:              {(result.TotalProfit / config.InitialCapital * 100):N2}%");
    Console.WriteLine($"Tasa de éxito:        {result.WinRate:P2}");
    Console.WriteLine($"Máximo Drawdown:      ${result.MaxDrawdown:N2}");
    Console.WriteLine($"Sharpe Ratio:         {result.SharpeRatio:N2}");

    if (result.EquityCurve.Count > 0)
    {
        var finalEquity = result.EquityCurve[^1];
        Console.WriteLine($"Capital final:        ${finalEquity:N2}");
    }

    if (result.Trades.Count > 0)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════════════");
        Console.WriteLine("  PRIMERAS 5 OPERACIONES");
        Console.WriteLine("═══════════════════════════════════════════════════════════\n");

        foreach (var trade in result.Trades.Take(5))
        {
            var profit = trade.Profit;
            var profitPercent = (profit / (trade.EntryPrice * trade.PositionSize)) * 100;
            var profitSymbol = profit >= 0 ? "✓" : "✗";

            Console.WriteLine($"{profitSymbol} {trade.EntryTime:yyyy-MM-dd} → {trade.ExitTime:yyyy-MM-dd}");
            Console.WriteLine($"  Entrada:  ${trade.EntryPrice:N2} x {trade.PositionSize:N8}");
            Console.WriteLine($"  Salida:   ${trade.ExitPrice:N2}");
            Console.WriteLine($"  P&L:      ${profit:N2} ({profitPercent:+0.00;-0.00}%)");
            Console.WriteLine();
        }

        if (result.Trades.Count > 5)
        {
            Console.WriteLine($"... y {result.Trades.Count - 5} operaciones más.");
        }
    }

    Console.WriteLine("\n═══════════════════════════════════════════════════════════");
    Console.WriteLine("  Backtest completado exitosamente");
    Console.WriteLine("═══════════════════════════════════════════════════════════\n");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error al ejecutar el backtest:");
    Console.WriteLine($"   {ex.Message}");
    Console.WriteLine($"\n{ex.StackTrace}");
    return 1;
}

return 0;
