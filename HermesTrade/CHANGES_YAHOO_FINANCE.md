# Cambios Realizados: Yahoo Finance Integration

## 📝 Resumen

Se ha reemplazado los proveedores de datos de Binance y Coinbase por **Yahoo Finance** como fuente de datos históricos gratuita.

## ✅ Archivos Eliminados

- ❌ `Data/BinanceDataProvider.cs` - Eliminado
- ❌ `Data/CoinbaseDataProvider.cs` - Eliminado

## ✅ Archivos Creados

### 1. `Data/YahooFinanceDataProvider.cs`
Nuevo proveedor de datos que usa el paquete NuGet `YahooFinanceApi`.

**Características:**
- ✅ Soporte para múltiples tipos de activos:
  - Acciones (AAPL, MSFT, TSLA, etc.)
  - Criptomonedas (BTC-USD, ETH-USD, etc.)
  - Índices (^GSPC, ^DJI, etc.)
  - Forex (EURUSD=X, GBPUSD=X, etc.)
  - ETFs (SPY, QQQ, etc.)
- ✅ Sin API key requerida
- ✅ 100% gratuito
- ✅ Caché local automático
- ✅ Normalización automática de símbolos

### 2. `Examples/HermesTrade.Example/YAHOO_FINANCE_GUIDE.md`
Guía completa de uso de Yahoo Finance con:
- Lista de símbolos disponibles por categoría
- Ejemplos de configuración
- Troubleshooting
- Consideraciones de límites y restricciones

## 📦 Paquete NuGet Agregado

```xml
<PackageReference Include="YahooFinanceApi" />
```

## 🔧 Archivos Modificados

### `Examples/HermesTrade.Example/Program.cs`
- ✅ Agregado `using HermesTrade.Data;`
- ✅ Agregado `using YahooFinanceApi;`
- ✅ Agregados comentarios con instrucciones para cambiar entre MockMarketDataProvider y YahooFinanceDataProvider
- ✅ Lista de símbolos disponibles en los comentarios

### `Examples/HermesTrade.Example/README.md`
- ✅ Actualizado con información sobre Yahoo Finance
- ✅ Eliminadas referencias a Binance y Coinbase
- ✅ Agregada sección de "Usar datos reales de Yahoo Finance"

### `BACKTEST_EXAMPLE.md`
- ✅ Actualizado para reflejar Yahoo Finance como proveedor
- ✅ Agregada sección detallada sobre uso de datos reales
- ✅ Listados de símbolos soportados
- ✅ Ventajas y consideraciones

### `HermesTrade.csproj`
- ✅ Agregado `YahooFinanceApi` como dependencia
- ✅ Mantenido el resto de configuración intacta

## 🎯 Cómo Usar Yahoo Finance

### Opción 1: En el código existente

En `Examples/HermesTrade.Example/Program.cs`, descomenta:

```csharp
// Opción 2: Datos reales de Yahoo Finance (descomenta para usar)
var cacheService = new FileCacheService("cache");
var dataProvider = new YahooFinanceDataProvider(cacheService, Period.Daily);
```

Y comenta la línea de MockMarketDataProvider:

```csharp
// Opción 1: Datos sintéticos (para pruebas rápidas)
// var dataProvider = new MockMarketDataProvider();
```

### Opción 2: Crear tu propio código

```csharp
using HermesTrade.Data;
using YahooFinanceApi;

var cacheService = new FileCacheService("cache");
var dataProvider = new YahooFinanceDataProvider(cacheService, Period.Daily);
var indicatorService = new DefaultIndicatorService();
var backtestEngine = new BacktestEngine(dataProvider, indicatorService);

var config = new BacktestConfig
{
    Symbol = "AAPL",  // Cualquier símbolo de Yahoo Finance
    From = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    To = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc),
    InitialCapital = 10_000m,
    Fees = 0.001m,
    PositionSizeFraction = 1m
};

var strategy = new SimpleStrategyExample(30m, 70m);
var result = await backtestEngine.RunAsync(strategy, config);
```

## 💡 Ejemplos de Símbolos

### Acciones
- `AAPL` - Apple Inc.
- `MSFT` - Microsoft Corporation
- `GOOGL` - Alphabet Inc.
- `TSLA` - Tesla Inc.
- `AMZN` - Amazon.com Inc.

### Criptomonedas
- `BTC-USD` - Bitcoin
- `ETH-USD` - Ethereum
- `ADA-USD` - Cardano
- `SOL-USD` - Solana

### Índices
- `^GSPC` - S&P 500
- `^DJI` - Dow Jones
- `^IXIC` - NASDAQ

### Forex
- `EURUSD=X` - Euro / USD
- `GBPUSD=X` - Libra / USD

## 🔍 Verificación

```bash
# Compilar proyecto
dotnet build

# Ejecutar ejemplo con datos mock (por defecto)
dotnet run --project Examples/HermesTrade.Example/HermesTrade.Example.csproj

# Para usar Yahoo Finance, edita Program.cs según las instrucciones
```

## 📚 Documentación Adicional

- `Examples/HermesTrade.Example/YAHOO_FINANCE_GUIDE.md` - Guía completa
- `Examples/HermesTrade.Example/README.md` - README del ejemplo
- `BACKTEST_EXAMPLE.md` - Documentación general del proyecto

## ✨ Beneficios

1. **Gratuito**: Sin costos, sin API keys
2. **Diversificado**: Acciones, crypto, índices, forex, ETFs
3. **Histórico extenso**: Años de datos disponibles
4. **Caché automático**: Las consultas repetidas son instantáneas
5. **Fácil de usar**: Interfaz simple y clara
6. **Alta calidad**: Datos confiables de Yahoo Finance

## ⚠️ Limitaciones

- Solo soporta intervalos diarios, semanales y mensuales (no intradiarios)
- Rate limits no documentados (usa caché para mitigar)
- Algunos símbolos pueden no estar disponibles
- Datos de mercados cerrados en fines de semana/festivos

## 🚀 Próximos Pasos

1. Prueba el ejemplo con datos mock (ya funciona)
2. Descomenta Yahoo Finance en Program.cs
3. Prueba con diferentes símbolos
4. Lee la guía completa en `YAHOO_FINANCE_GUIDE.md`
5. Implementa tu propia estrategia
