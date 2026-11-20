# Usar Yahoo Finance con HermesTrade

Este documento explica cómo usar datos reales de Yahoo Finance en lugar de datos sintéticos.

## 🚀 Activar Yahoo Finance

### Opción 1: Editar Program.cs (Recomendado para pruebas)

1. Abre `Examples/HermesTrade.Example/Program.cs`
2. Comenta la línea de MockMarketDataProvider:

3. Descomenta las líneas de Yahoo Finance:

```csharp
// Opción 1 (por defecto): datos sintéticos, sin red y reproducibles
// var dataProvider = new MockMarketDataProvider();

// Opción 2: datos reales de Yahoo Finance
var cacheService = new FileCacheService("cache");
var dataProvider = new YahooFinanceDataProvider(cacheService);
```

4. Cambia el símbolo y período según necesites:

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
No soportado: YahooQuotesApi no devuelve histórico de pares de divisas ("EURUSD=X"),
y el proveedor lanza `NotSupportedException` para esos símbolos.

### ETFs
```csharp
"SPY"   // SPDR S&P 500 ETF
"QQQ"   // Invesco QQQ Trust
"DIA"   // SPDR Dow Jones Industrial Average ETF
"IWM"   // iShares Russell 2000 ETF
"GLD"   // SPDR Gold Shares
"TLT"   // iShares 20+ Year Treasury Bond ETF
```

## 📅 Intervalo de Tiempo

El proveedor solo devuelve **velas diarias** (YahooQuotesApi siempre pide `interval=1d`).
No hay opción semanal, mensual ni intradiaria: para otros intervalos agrega tú las velas
diarias o implementa un `IMarketDataProvider` propio.

## 💾 Caché de Datos

El `YahooFinanceDataProvider` usa un sistema de caché local:

- **Primera consulta**: Descarga datos de Yahoo Finance y los guarda localmente
- **Consultas posteriores**: Lee desde el caché (mucho más rápido)
- **Ubicación**: Carpeta `cache/` relativa al directorio de trabajo actual (desde donde lanzas `dotnet run`)
- **Nombre de fichero**: `YAHOO-FINANCE-V2_{SÍMBOLO}_{yyyyMMdd}_{yyyyMMdd}.json`, p. ej. `YAHOO-FINANCE-V2_AAPL_20230101_20231231.json`
  (`YAHOO-FINANCE-V2-ADJ_...` con precios ajustados por dividendos; los ficheros `YAHOO-FINANCE_...` de versiones anteriores ya no se usan y se pueden borrar)
- **Rangos abiertos**: si `To` es ayer, hoy o una fecha futura no se usa la caché, porque los datos siguen creciendo y la última sesión puede no estar cerrada
- **Entradas inválidas**: un fichero vacío, corrupto o con cierres a 0 se descarta y se vuelve a descargar

### Limpiar la Caché

Si quieres forzar la descarga de datos actualizados:

```powershell
# Eliminar toda la caché
Remove-Item -Recurse cache/

# O eliminar solo un símbolo específico
Remove-Item cache/YAHOO-FINANCE*_AAPL_*.json
```

## 🎯 Ejemplos Completos

### Ejemplo 1: Backtest de Apple (2023)

```csharp
var cacheService = new FileCacheService("cache");
var dataProvider = new YahooFinanceDataProvider(cacheService);
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

### Fechas de las velas
Cada vela diaria se fecha a las 00:00 UTC de su día de cotización en la zona horaria de la bolsa
(una sesión de Sídney del lunes es el lunes, aunque abra el domingo en UTC). `From` y `To` se
comparan por fecha, inclusive: `To = new DateTime(2024, 12, 31)` incluye el 31 de diciembre.

### Dividendos
Por defecto los precios están ajustados por splits pero no por dividendos, así que el resultado es
solo la rentabilidad del precio. Para aproximar la rentabilidad total:

```csharp
var dataProvider = new YahooFinanceDataProvider(cacheService, useDividendAdjustedPrices: true);
```

Esos precios están reajustados hacia atrás a la fecha de descarga: no son precios negociados
y cambian con cada nuevo dividendo (borra la caché para refrescarlos).

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
- [YahooQuotesApi NuGet](https://www.nuget.org/packages/YahooQuotesApi/)
- [Lista de símbolos Yahoo Finance](https://finance.yahoo.com/lookup/)

## 🎓 Siguiente Paso

Una vez que tengas datos reales funcionando, considera:

1. **Optimizar tu estrategia** probando diferentes parámetros
2. **Crear estrategias personalizadas** implementando `IStrategy`
3. **Exportar resultados** a CSV o JSON para análisis
4. **Comparar múltiples activos** ejecutando backtests en paralelo
