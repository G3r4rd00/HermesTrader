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
- Tendencia alcista suave más un ciclo sinusoidal
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

Salida real con `MockMarketDataProvider` (el formato de números depende de la cultura del sistema).
Las velas anteriores a `From` (desde 2022-11-27) solo sirven para calentar los indicadores:

```
═══════════════════════════════════════════════════════════
  HermesTrade - Ejemplo de BacktestEngine
═══════════════════════════════════════════════════════════

Configuración del Backtest:
  Symbol:            BTC-USD
  Período:           2024-01-01 a 2024-12-31
  Capital inicial:   $10.000,00
  Comisiones:        0,10 %
  Estrategia:        RSI (Compra<30, Venta>70)

Ejecutando backtest...

Generando datos de mercado sintéticos para BTC-USD...
✓ Generadas 766 velas desde 2022-11-27 hasta 2024-12-31
  Precio inicial: $66.595,93
  Precio final:   $85.612,95
  Variación:      +28,56%

═══════════════════════════════════════════════════════════
  RESULTADOS DEL BACKTEST
═══════════════════════════════════════════════════════════

Total de operaciones: 3
Beneficio neto:       $-2.437,84
Retorno:              -24,38%
Tasa de éxito:        0,00 %
Máximo Drawdown:      $3.434,86
Sharpe Ratio:         -1,44
Capital final:        $7.562,16
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

### Usar datos reales (Yahoo Finance)

En `Program.cs`, comenta la línea de `MockMarketDataProvider` y descomenta las de Yahoo Finance:

```csharp
using HermesTrade.Data;

var cacheService = new FileCacheService("cache");
var dataProvider = new YahooFinanceDataProvider(cacheService);
```

Para otras fuentes (exchanges, CSV, bases de datos...) implementa tu propio `IMarketDataProvider`.

## Próximos pasos

- 📊 Implementa tu propia estrategia usando `IStrategy`
- 💾 Guarda los resultados en archivos CSV o JSON
- 📈 Visualiza la curva de equity con una librería de gráficos
- 🔄 Ejecuta múltiples backtests con diferentes parámetros (optimización)
- 📦 Conecta con otras fuentes de datos implementando `IMarketDataProvider`

## Referencias

- [BacktestEngine.cs](../../Engine/BacktestEngine.cs)
- [IStrategy.cs](../../Interfaces/IStrategy.cs)
- [SimpleStrategyExample.cs](../../Strategies/SimpleStrategyExample.cs)
