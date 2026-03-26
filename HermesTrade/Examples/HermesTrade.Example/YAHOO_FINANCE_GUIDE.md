# Usar Yahoo Finance con HermesTrade

Este documento explica cómo usar datos reales de Yahoo Finance en lugar de datos sintéticos.

## 🚀 Activar Yahoo Finance

### Opción 1: Editar Program.cs (Recomendado para pruebas)

1. Abre `Examples/HermesTrade.Example/Program.cs`
2. Comenta la línea de MockMarketDataProvider:

```csharp
// Opción 1: Datos sintéticos (para pruebas rápidas)
// var dataProvider = new MockMarketDataProvider();

// Opción 2: Datos reales de Yahoo Finance (descomenta para usar)
var cacheService = new FileCacheService("cache");
var dataProvider = new YahooFinanceDataProvider(cacheService, Period.Daily);
```

3. Cambia el símbolo y período según necesites:

```csharp
var config = new BacktestConfig
{
    Symbol = "AAPL",  // ← Cambia esto
    From = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    To = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc),
    InitialCapital = 10_000m,
    Fees = 0.001m,
    PositionSizeFraction = 1m
};
```

## 📊 Símbolos Disponibles

### Acciones (Stocks)
```csharp
"AAPL"   // Apple Inc.
"MSFT"   // Microsoft Corporation
"GOOGL"  // Alphabet Inc. (Google)
"AMZN"   // Amazon.com Inc.
"TSLA"   // Tesla Inc.
"META"   // Meta Platforms Inc. (Facebook)
"NVDA"   // NVIDIA Corporation
"JPM"    // JPMorgan Chase & Co.
"V"      // Visa Inc.
"WMT"    // Walmart Inc.
```

### Criptomonedas
```csharp
"BTC-USD"  // Bitcoin
"ETH-USD"  // Ethereum
"ADA-USD"  // Cardano
"SOL-USD"  // Solana
"DOT-USD"  // Polkadot
"MATIC-USD" // Polygon
"AVAX-USD" // Avalanche
"LINK-USD" // Chainlink
```

### Índices
```csharp
"^GSPC"  // S&P 500
"^DJI"   // Dow Jones Industrial Average
"^IXIC"  // NASDAQ Composite
"^RUT"   // Russell 2000
"^VIX"   // CBOE Volatility Index
```

### Forex (Divisas)
```csharp
"EURUSD=X"  // Euro / US Dollar
"GBPUSD=X"  // British Pound / US Dollar
"JPYUSD=X"  // Japanese Yen / US Dollar
"AUDUSD=X"  // Australian Dollar / US Dollar
"CADUSD=X"  // Canadian Dollar / US Dollar
```

### ETFs
```csharp
"SPY"   // SPDR S&P 500 ETF
"QQQ"   // Invesco QQQ Trust
"DIA"   // SPDR Dow Jones Industrial Average ETF
"IWM"   // iShares Russell 2000 ETF
"GLD"   // SPDR Gold Shares
"TLT"   // iShares 20+ Year Treasury Bond ETF
```

## 📅 Intervalos de Tiempo (Period)

```csharp
Period.Daily       // Velas diarias (recomendado para backtesting)
Period.Weekly      // Velas semanales
Period.Monthly     // Velas mensuales
```

**Nota:** Yahoo Finance API gratuita solo soporta estos intervalos. Para intervalos intradiarios (1m, 5m, 15m, 1h) necesitarías una fuente de datos diferente.

## 💾 Caché de Datos

El `YahooFinanceDataProvider` usa un sistema de caché local:

- **Primera consulta**: Descarga datos de Yahoo Finance y los guarda localmente
- **Consultas posteriores**: Lee desde el caché (mucho más rápido)
- **Ubicación**: Carpeta `cache/` en el directorio de ejecución

### Limpiar la Caché

Si quieres forzar la descarga de datos actualizados:

```bash
# Eliminar toda la caché
Remove-Item -Recurse cache/

# O eliminar solo un símbolo específico
Remove-Item cache/yahoo-finance/AAPL-*
```

## 🎯 Ejemplos Completos

### Ejemplo 1: Backtest de Apple (2023)

```csharp
var cacheService = new FileCacheService("cache");
var dataProvider = new YahooFinanceDataProvider(cacheService, Period.Daily);
var indicatorService = new DefaultIndicatorService();
var backtestEngine = new BacktestEngine(dataProvider, indicatorService);

var config = new BacktestConfig
{
    Symbol = "AAPL",
    From = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    To = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc),
    InitialCapital = 10_000m,
    Fees = 0.001m,
    PositionSizeFraction = 1m
};

var strategy = new SimpleStrategyExample(
    oversoldThreshold: 30m,
    overboughtThreshold: 70m
);

var result = await backtestEngine.RunAsync(strategy, config);
```

### Ejemplo 2: Backtest de Bitcoin (últimos 2 años)

```csharp
var config = new BacktestConfig
{
    Symbol = "BTC-USD",
    From = DateTime.UtcNow.AddYears(-2),
    To = DateTime.UtcNow,
    InitialCapital = 50_000m,
    Fees = 0.001m,
    PositionSizeFraction = 0.5m  // Solo invertir 50%
};
```

### Ejemplo 3: Backtest del S&P 500

```csharp
var config = new BacktestConfig
{
    Symbol = "^GSPC",
    From = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    To = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc),
    InitialCapital = 100_000m,
    Fees = 0.0005m,  // 0.05% (comisiones más bajas para índices)
    PositionSizeFraction = 1m
};
```

## ⚠️ Consideraciones

### Límites de Yahoo Finance
- ✅ **Sin API Key**: No requiere registro
- ✅ **Gratuito**: 100% gratis para uso personal
- ⚠️ **Rate Limits**: Limita las peticiones (usa caché para mitigar)
- ⚠️ **Datos diarios**: No soporta intervalos intradiarios en la API gratuita

### Horarios de Mercado
Yahoo Finance devuelve datos según los horarios de mercado:
- **Acciones US**: Lunes-Viernes (mercado cerrado fines de semana)
- **Crypto**: 24/7/365
- **Indices**: Lunes-Viernes

### Fechas sin datos
Si una fecha cae en fin de semana o festivo, no habrá datos disponibles.

## 🔧 Troubleshooting

### Error: "Símbolo no encontrado"
```
✗ Error downloading data for XYZ: Symbol not found
```

**Solución**: Verifica el símbolo en https://finance.yahoo.com/

### Error: "No data found"
```
⚠ No data found for AAPL
```

**Causas posibles**:
- El símbolo no tiene datos históricos en ese período
- Fechas incorrectas (from > to)
- El símbolo está delisted o suspendido

### Error: "Rate limit exceeded"
```
HTTP 429 Too Many Requests
```

**Solución**: 
- Espera unos minutos antes de intentar de nuevo
- Usa el caché (no elimines `cache/`)
- Reduce la frecuencia de consultas

## 📚 Recursos

- [Yahoo Finance](https://finance.yahoo.com/)
- [YahooFinanceApi NuGet](https://www.nuget.org/packages/YahooFinanceApi/)
- [Lista de símbolos Yahoo Finance](https://finance.yahoo.com/lookup/)

## 🎓 Siguiente Paso

Una vez que tengas datos reales funcionando, considera:

1. **Optimizar tu estrategia** probando diferentes parámetros
2. **Crear estrategias personalizadas** implementando `IStrategy`
3. **Exportar resultados** a CSV o JSON para análisis
4. **Comparar múltiples activos** ejecutando backtests en paralelo
