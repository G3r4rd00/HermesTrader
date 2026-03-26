# HermesTrade.Example

Proyecto de ejemplo que demuestra cómo usar el **BacktestEngine** de HermesTrade para ejecutar simulaciones de estrategias de trading.

## Descripción

Este proyecto de consola ilustra:

- ✅ Cómo configurar un backtest con `BacktestConfig`
- ✅ Cómo usar `SimpleStrategyExample` (estrategia basada en RSI)
- ✅ Cómo usar Yahoo Finance para obtener datos históricos reales
- ✅ Cómo implementar un `IMarketDataProvider` personalizado (MockMarketDataProvider)
- ✅ Cómo interpretar los resultados del backtest

## Componentes

### Program.cs
Programa principal que:
1. Configura el motor de backtesting
2. Define los parámetros del backtest (símbolo, fechas, capital inicial, comisiones)
3. Ejecuta la estrategia RSI sobre datos históricos
4. Muestra los resultados detallados

### MockMarketDataProvider.cs
Implementación de prueba de `IMarketDataProvider` que genera datos de mercado sintéticos con:
- Tendencia simulada usando una función sinusoidal
- Volatilidad aleatoria controlada
- Velas OHLC realistas

**Nota:** El ejemplo por defecto usa datos sintéticos. Para usar datos reales de Yahoo Finance, 
descomenta la sección correspondiente en `Program.cs`.

## Cómo ejecutar

Desde el directorio raíz de la solución:

```bash
dotnet run --project Examples/HermesTrade.Example/HermesTrade.Example.csproj
```

O desde Visual Studio:
1. Establece `HermesTrade.Example` como proyecto de inicio
2. Presiona F5 o Ctrl+F5

## Ejemplo de salida

```
═══════════════════════════════════════════════════════════
  HermesTrade - Ejemplo de BacktestEngine
═══════════════════════════════════════════════════════════

Configuración del Backtest:
  Symbol:            BTC-USD
  Período:           2024-01-01 a 2024-12-31
  Capital inicial:   $10,000.00
  Comisiones:        0.10%
  Estrategia:        RSI (Compra<30, Venta>70)

Ejecutando backtest...

═══════════════════════════════════════════════════════════
  RESULTADOS DEL BACKTEST
═══════════════════════════════════════════════════════════

Total de operaciones: 15
Beneficio neto:       $1,234.56
Retorno:              12.35%
Tasa de éxito:        66.67%
Máximo Drawdown:      $567.89
Sharpe Ratio:         1.23
Capital final:        $11,234.56
```

## Personalización

### Cambiar la estrategia

Modifica los parámetros de RSI en `Program.cs`:

```csharp
var strategy = new SimpleStrategyExample(
    oversoldThreshold: 25m,   // Más agresivo
    overboughtThreshold: 75m  // Más conservador
);
```

### Ajustar la configuración

```csharp
var config = new BacktestConfig
{
    Symbol = "ETH-USD",           // Cambiar símbolo
    InitialCapital = 50_000m,     // Más capital
    Fees = 0.0015m,               // Comisiones diferentes
    PositionSizeFraction = 0.5m   // Invertir solo 50%
};
```

### Usar datos reales

Reemplaza `MockMarketDataProvider` con `BinanceDataProvider` o `CoinbaseDataProvider`:

```csharp
var dataProvider = new BinanceDataProvider("tu_api_key", "tu_api_secret");
```

## Próximos pasos

- 📊 Implementa tu propia estrategia usando `IStrategy`
- 💾 Guarda los resultados en archivos CSV o JSON
- 📈 Visualiza la curva de equity con una librería de gráficos
- 🔄 Ejecuta múltiples backtests con diferentes parámetros (optimización)
- 📦 Conecta con fuentes de datos reales (Binance, Coinbase, etc.)

## Referencias

- [BacktestEngine.cs](../../Engine/BacktestEngine.cs)
- [IStrategy.cs](../../Interfaces/IStrategy.cs)
- [SimpleStrategyExample.cs](../../Strategies/SimpleStrategyExample.cs)
