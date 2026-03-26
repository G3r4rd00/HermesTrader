# HermesTrade - Backtesting Engine

## Estructura de la Solución

```
HermesTrade/
├── HermesTrade.csproj              # Biblioteca principal
│   ├── Engine/                     # Motor de backtesting
│   ├── Strategies/                 # Estrategias de ejemplo
│   ├── Indicators/                 # Servicios de indicadores
│   ├── Data/                       # Proveedores de datos (Binance, Coinbase)
│   ├── Core/                       # Modelos y enums
│   ├── Interfaces/                 # Contratos
│   └── Configuration/              # Configuraciones
│
└── Examples/
    └── HermesTrade.Example/        # Proyecto de ejemplo
        ├── Program.cs              # Aplicación de consola
        ├── MockMarketDataProvider.cs
        └── README.md

```

## Proyecto de Ejemplo

El proyecto **HermesTrade.Example** demuestra cómo usar el `BacktestEngine` para simular estrategias de trading.

### Características

✅ Configuración completa de un backtest  
✅ Implementación de proveedor de datos mock  
✅ Uso de la estrategia RSI incluida  
✅ Visualización de resultados detallados  

### Ejecutar desde Visual Studio

1. Abre la solución `HermesTrade.sln`
2. En el Explorador de soluciones, haz clic derecho en `HermesTrade.Example`
3. Selecciona "Establecer como proyecto de inicio"
4. Presiona `F5` para ejecutar con depuración o `Ctrl+F5` sin depuración

### Ejecutar desde línea de comandos

```bash
# Desde el directorio raíz del proyecto
dotnet run --project Examples/HermesTrade.Example/HermesTrade.Example.csproj
```

### Componentes del Ejemplo

#### Program.cs
Demuestra el flujo completo:
- Configuración del BacktestEngine
- Definición de parámetros de backtest
- Ejecución de estrategia RSI
- Interpretación de resultados

#### MockMarketDataProvider.cs
Generador de datos sintéticos que:
- Simula movimientos de mercado realistas
- Crea tendencias y volatilidad
- Genera señales RSI para pruebas

### Personalización

Modifica `Program.cs` para ajustar:

```csharp
// Cambiar estrategia
var strategy = new SimpleStrategyExample(
    oversoldThreshold: 25m,
    overboughtThreshold: 75m
);

// Ajustar configuración
var config = new BacktestConfig
{
    Symbol = "ETH-USD",
    From = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    To = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc),
    InitialCapital = 50_000m,
    Fees = 0.0015m,
    PositionSizeFraction = 0.5m
};
```

### Usar Datos Reales

Para conectar con datos reales de exchanges:

```csharp
// Binance
var dataProvider = new BinanceDataProvider(apiKey, apiSecret);

// Coinbase
var dataProvider = new CoinbaseDataProvider(apiKey, apiSecret);
```

## Próximos Pasos

- 🔧 Implementa tu propia estrategia usando `IStrategy`
- 📊 Exporta resultados a CSV o JSON
- 📈 Crea visualizaciones de la curva de equity
- ⚡ Optimiza parámetros con múltiples backtests
- 🔄 Conecta con APIs de exchanges reales

## Recursos

- [Documentación del BacktestEngine](Engine/BacktestEngine.cs)
- [Interfaz IStrategy](Interfaces/IStrategy.cs)
- [Estrategia RSI de ejemplo](Strategies/SimpleStrategyExample.cs)
- [Proveedores de datos](Data/)
