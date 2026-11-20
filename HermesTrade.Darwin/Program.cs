using System.Globalization;
using HermesTrade.Darwin;
using HermesTrade.Interfaces;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("es-ES");
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
Console.OutputEncoding = System.Text.Encoding.UTF8;

var opts = Options.Parse(args);

// Cache and models live next to the .csproj, whether started with `dotnet run` or from Visual Studio (bin\...)
string projectDir = ProjectDirectory();
string cacheDir = opts.Get("cache", Path.Combine(projectDir, "cache"));
var engine = MarketData.CreateEngine(cacheDir);
var fitness = new FitnessSettings
{
    WeightDecay = opts.GetDouble("decay", 0.01),
    MinTradesPerYear = opts.GetDouble("min-trades", 3),
};
var evaluator = new Evaluator(engine, fitness);

if (args.Length == 0)
    await Menu();
else
    await RunCommand();

string ModelPath() => opts.Get("model", Path.Combine(projectDir, "models", "champion.json"));

async Task RunCommand()
{
    switch (opts.Command)
    {
        case "evolve":
            await Evolve();
            break;
        case "report":
            await FullReport();
            break;
        case "test":
            await TestOne();
            break;
        case "baselines":
            await Baselines();
            break;
        case "inspect":
            await Inspect();
            break;
        default:
            Console.WriteLine("""
                HermesTrade.Darwin — estrategias de trading evolucionadas con DarwinGA
                Sin argumentos se abre un menú interactivo.

                  evolve     [--generations 120] [--islands 4] [--pop 50] [--hidden 6] [--seed 2026] [--min-trades 3] [--model models/champion.json]
                             Evoluciona la red con TRAIN, elige el campeón con VALIDATION y lo guarda. No carga datos de TEST.
                  report     [--model ...]   Informe final: campeón vs. referencias en TRAIN, VALIDATION y TEST (+ símbolos nunca vistos).
                  test       --symbol NVDA [--from 2024-01-01] [--to 2025-06-30] [--fees 0.001] [--model ...]
                             Prueba el campeón en cualquier símbolo y periodo.
                  baselines  Puntuación de las estrategias clásicas en TRAIN/VALIDATION (calibrar la fitness sin mirar TEST).
                  inspect    Puntuación TRAIN/VALIDATION de cada red del modelo guardado.
                """);
            break;
    }
}

async Task Menu()
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  HermesTrade.Darwin — trading con redes evolucionadas (DarwinGA)");
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine($"  Modelo: {(File.Exists(ModelPath()) ? ModelPath() : "(todavía no hay; usa la opción 1)")}");
        Console.WriteLine();
        Console.WriteLine("  1) Evolucionar una estrategia (TRAIN + VALIDATION, no mira TEST)");
        Console.WriteLine("  2) Informe final con TEST (periodo y símbolos nunca vistos)");
        Console.WriteLine("  3) Probar el modelo en un símbolo y periodo");
        Console.WriteLine("  4) Estrategias clásicas de referencia (TRAIN/VALIDATION)");
        Console.WriteLine("  5) Inspeccionar las redes del modelo");
        Console.WriteLine("  0) Salir");

        string? choice = Ask("Opción", "1");
        if (choice is null or "0")
            return;

        Options? next = choice switch
        {
            "1" => Options.Create("evolve",
                ("generations", Ask("Generaciones", "120")),
                ("islands", Ask("Islas", "4")),
                ("pop", Ask("Redes por isla", "50")),
                ("hidden", Ask("Neuronas ocultas", "6")),
                ("seed", Ask("Semilla", "2026"))),
            "2" => ConfirmTest() ? Options.Create("report") : null,
            "3" => Options.Create("test",
                ("symbol", Ask("Símbolo (p. ej. SPY, NVDA, BTC-USD)", "SPY")),
                ("from", Ask("Desde (yyyy-MM-dd)", $"{Experiment.TestFrom:yyyy-MM-dd}")),
                ("to", Ask("Hasta (yyyy-MM-dd)", $"{DateTime.UtcNow.Date.AddDays(-1):yyyy-MM-dd}")),
                ("fees", Ask("Comisión por lado", "0.001"))),
            "4" => Options.Create("baselines"),
            "5" => Options.Create("inspect"),
            _ => null,
        };

        if (next is null)
            continue;

        opts = next;
        try
        {
            await RunCommand();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ {ex.Message}");
        }

        Console.WriteLine("\nPulsa Enter para volver al menú...");
        if (Console.ReadLine() is null)
            return;
    }
}

bool ConfirmTest()
{
    Console.WriteLine("  ⚠ El informe abre el periodo TEST. Para que la prueba siga siendo limpia, úsalo solo con el modelo definitivo.");
    return Ask("¿Continuar? (s/n)", "n")?.StartsWith("s", StringComparison.OrdinalIgnoreCase) == true;
}

// null when stdin is closed
static string? Ask(string label, string fallback)
{
    Console.Write($"  {label} [{fallback}]: ");
    string? line = Console.ReadLine();
    if (line is null)
        return null;
    return string.IsNullOrWhiteSpace(line) ? fallback : line.Trim();
}

ModelFile? LoadModel()
{
    if (File.Exists(ModelPath()))
        return ModelFile.Load(ModelPath());

    Console.WriteLine($"No hay modelo en {ModelPath()}. Evoluciona uno primero (opción 1 o `evolve`).");
    return null;
}

static string ProjectDirectory()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "HermesTrade.Darwin.csproj")))
            return dir.FullName;
    return Directory.GetCurrentDirectory();
}

async Task Evolve()
{
    var settings = new EvolutionSettings
    {
        Generations = opts.GetInt("generations", 120),
        Islands = opts.GetInt("islands", 4),
        PopulationPerIsland = opts.GetInt("pop", 50),
        Hidden = opts.GetInt("hidden", 6),
        Seed = opts.GetInt("seed", 2026),
        BagFraction = opts.GetDouble("bag", 0.7),
        MutationProbability = opts.GetDouble("mutation", 0.05),
    };

    Console.WriteLine($"Cargando {Experiment.TrainSymbols.Length} símbolos hasta {Experiment.ValTo:yyyy-MM-dd} (TEST no se descarga)...");
    var data = await MarketData.LoadAsync(engine, Experiment.TrainSymbols, Experiment.TrainFrom, Experiment.ValTo);
    Console.WriteLine($"  {data.Count} símbolos, {data.Sum(d => d.Data.Candles.Count):N0} velas");

    foreach (var (name, create) in RuleStrategy.All().Take(2))
        Console.WriteLine($"  Referencia {name}: train {evaluator.Score(evaluator.YearlyStats(create, data, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees)):F3} · " +
                          $"validation {evaluator.Score(evaluator.YearlyStats(create, data, Experiment.ValFrom, Experiment.ValTo, Experiment.TrainFees)):F3}");

    var evolution = new Evolution(evaluator, data, settings);
    Console.WriteLine($"Evolución: {settings.Islands} islas × {settings.PopulationPerIsland} redes ({Features.InputCount}→{settings.Hidden}→1), {settings.Generations} generaciones");
    Console.WriteLine($"  TRAIN {Experiment.TrainFrom:yyyy}–{Experiment.TrainTo:yyyy} (fitness) · VALIDATION {Experiment.ValFrom:yyyy}–{Experiment.ValTo:yyyy} (selección) · comisión {Experiment.TrainFees:P1}/lado");

    var hallOfFame = evolution.Run();
    var picked = Evolution.PickEnsemble(hallOfFame, settings.EnsembleSize);

    Console.WriteLine();
    Console.WriteLine("Ensemble elegido (por min(train, validation)):");
    foreach (var c in picked)
        Console.WriteLine($"  isla {c.Island} · gen {c.Generation,3} · train {c.Train:F3} · validation {c.Validation:F3} · umbrales {c.Brain.EntryThreshold:F2}/{c.Brain.ExitThreshold:F2}");

    var ensemble = new EnsembleBrain(picked.Select(c => c.Brain).ToList());
    Console.WriteLine($"  Ensemble: train {evaluator.BrainScoreOf(ensemble, data, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees):F3} · " +
                      $"validation {evaluator.BrainScoreOf(ensemble, data, Experiment.ValFrom, Experiment.ValTo, Experiment.TrainFees):F3}");

    var model = new ModelFile
    {
        Features = Features.AllNames.ToArray(),
        Members = picked.Select(c => c.Brain).ToList(),
        MemberInfo = picked.Select(c => new MemberInfo(c.Island, c.Generation, c.Train, c.Validation)).ToList(),
        TrainPeriod = $"{Experiment.TrainFrom:yyyy-MM-dd}..{Experiment.TrainTo:yyyy-MM-dd}",
        ValidationPeriod = $"{Experiment.ValFrom:yyyy-MM-dd}..{Experiment.ValTo:yyyy-MM-dd}",
        TestPeriod = $"{Experiment.TestFrom:yyyy-MM-dd}..{Experiment.TestTo:yyyy-MM-dd}",
        TrainSymbols = Experiment.TrainSymbols,
        UnseenSymbols = Experiment.UnseenSymbols,
        Evolution = settings,
        Fitness = fitness,
    };
    model.Save(ModelPath());
    Console.WriteLine($"Guardado en {Path.GetFullPath(ModelPath())}");

    // Learning curve: how validation tracks train along the run (overfitting shows as a gap)
    Console.WriteLine();
    Console.WriteLine("Curva train vs validation (mejor por bloque de generaciones):");
    foreach (var g in hallOfFame.GroupBy(c => c.Generation / Math.Max(1, settings.Generations / 10)).OrderBy(g => g.Key))
    {
        var best = g.MaxBy(c => c.Train)!;
        Console.WriteLine($"  gen {g.Min(c => c.Generation),3}-{g.Max(c => c.Generation),3}: mejor train {best.Train,7:F3} → su validation {best.Validation,7:F3} · mejor validation del bloque {g.Max(c => c.Validation),7:F3}");
    }
}

async Task FullReport()
{
    var model = LoadModel();
    if (model is null)
        return;
    var brain = model.ToBrain();

    Console.WriteLine($"Modelo: {model.Name} · {model.Members.Count} redes · creado {model.CreatedUtc:yyyy-MM-dd HH:mm} UTC");
    Console.WriteLine("Cargando datos (ahora sí, incluido TEST)...");
    var seen = await MarketData.LoadAsync(engine, Experiment.TrainSymbols, Experiment.TrainFrom, Experiment.TestTo);
    var unseen = await MarketData.LoadAsync(engine, Experiment.UnseenSymbols, Experiment.TestFrom, Experiment.TestTo);
    var all = seen.Concat(unseen).ToList();

    var report = new Report(evaluator);
    var strategies = new List<(string, Func<SymbolData, IStrategy>)> { ("★ Red evolucionada", d => new NeuralStrategy(brain, d)) };
    strategies.AddRange(RuleStrategy.All());

    var fees = Experiment.RealFees;
    report.CompareStrategies("TRAIN (lo que vio la evolución)", strategies, seen, Experiment.TrainFrom, Experiment.TrainTo, fees);
    report.CompareStrategies("VALIDATION (solo para elegir campeón)", strategies, seen, Experiment.ValFrom, Experiment.ValTo, fees);
    report.CompareStrategies("TEST · mismos símbolos, periodo nunca visto", strategies, seen, Experiment.TestFrom, Experiment.TestTo, fees);
    report.CompareStrategies("TEST · símbolos nunca vistos, periodo nunca visto", strategies, unseen, Experiment.TestFrom, Experiment.TestTo, fees);
    report.CompareStrategies("TEST · todos, con comisión doble (0,2%)", strategies, all, Experiment.TestFrom, Experiment.TestTo, Experiment.TrainFees);

    report.SymbolTable(brain, all, Experiment.TestFrom, Experiment.TestTo, fees, Experiment.UnseenSymbols.ToHashSet());
    report.YearTable(brain, all, Experiment.TestFrom, Experiment.TestTo, fees);
    report.RandomBaseline(brain, all, Experiment.TestFrom, Experiment.TestTo, fees, 200, model.Members[0].Hidden);
    Report.FeatureImportance(model.Members);
}

async Task TestOne()
{
    var model = LoadModel();
    if (model is null)
        return;
    var brain = model.ToBrain();
    string symbol = opts.Get("symbol", "SPY");
    var from = opts.GetDate("from", Experiment.TestFrom);
    var to = opts.GetDate("to", DateTime.UtcNow.Date);
    decimal fees = decimal.Parse(opts.Get("fees", "0.001").Replace(',', '.'), CultureInfo.InvariantCulture);

    bool inSample = model.TrainSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase) && from <= Experiment.ValTo;
    var d = await MarketData.LoadOneAsync(engine, symbol, from, to);
    var report = new Report(evaluator);

    Console.WriteLine($"{symbol} · {from:yyyy-MM-dd} → {to:yyyy-MM-dd} · comisión {fees:P2}/lado" +
                      (inSample ? "  ⚠ este tramo incluye datos que la evolución vio (TRAIN/VALIDATION)" : "  ✓ fuera de muestra"));

    var strategies = new List<(string, Func<SymbolData, IStrategy>)> { ("★ Red evolucionada", x => new NeuralStrategy(brain, x)) };
    strategies.AddRange(RuleStrategy.All());
    Console.WriteLine($"  {"Estrategia",-24} {"Retorno",9} {"CAGR",8} {"MaxDD",8} {"Expos.",7} {"Trades",7} {"Sharpe",7}");
    foreach (var (name, create) in strategies)
    {
        var s = Evaluator.Summarize(evaluator.Run(create(d), d, from, to, fees), d, fees);
        Console.WriteLine($"  {name,-24} {s.Return,9:P1} {s.Cagr,8:P1} {s.MaxDrawdown,8:P1} {s.Exposure,7:P0} {s.Trades,7} {s.Sharpe,7:F2}");
    }

    var result = evaluator.Run(new NeuralStrategy(brain, d), d, from, to, fees);
    Console.WriteLine();
    Console.WriteLine("Operaciones de la red:");
    foreach (var t in result.Trades)
    {
        double pct = (double)(t.ExitPrice / t.EntryPrice - 1);
        Console.WriteLine($"  {(t.IsWin ? "✓" : "✗")} {t.EntryTime:yyyy-MM-dd} → {t.ExitTime:yyyy-MM-dd}  {t.EntryPrice,12:N2} → {t.ExitPrice,12:N2}  {pct,8:P1}");
    }

    report.YearTable(brain, [d], from, to, fees);
}

// TRAIN/VALIDATION scores of every member of a saved model, alone and as an ensemble
async Task Inspect()
{
    var model = LoadModel();
    if (model is null)
        return;
    var data = await MarketData.LoadAsync(engine, Experiment.TrainSymbols, Experiment.TrainFrom, Experiment.ValTo);
    double Tr(IBrain b) => evaluator.BrainScoreOf(b, data, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees);
    double Va(IBrain b) => evaluator.BrainScoreOf(b, data, Experiment.ValFrom, Experiment.ValTo, Experiment.TrainFees);
    for (int i = 0; i < model.Members.Count; i++)
    {
        var m = model.Members[i];
        var solo = new EnsembleBrain([m]);
        Console.WriteLine($"  miembro {i}: train {Tr(m):F3} val {Va(m):F3} | como ensemble de 1: train {Tr(solo):F3} val {Va(solo):F3} | guardado {model.MemberInfo[i].TrainScore:F3}/{model.MemberInfo[i].ValidationScore:F3}");
    }

    var all = model.ToBrain();
    Console.WriteLine($"  ensemble {model.Members.Count}: train {Tr(all):F3} val {Va(all):F3}");

    var rows = model.Members.Select((m, i) => ($"miembro {i}", (Func<SymbolData, IStrategy>)(d => new NeuralStrategy(m, d))))
        .Append(("ensemble", d => new NeuralStrategy(all, d))).ToList();
    var report = new Report(evaluator);
    report.CompareStrategies("TRAIN", rows, data, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees);
    report.CompareStrategies("VALIDATION", rows, data, Experiment.ValFrom, Experiment.ValTo, Experiment.TrainFees);
}

async Task Baselines()
{
    var data = await MarketData.LoadAsync(engine, Experiment.TrainSymbols, Experiment.TrainFrom, Experiment.ValTo);
    var report = new Report(evaluator);
    var strategies = RuleStrategy.All().ToList();
    strategies.Add(("Siempre en liquidez", _ => new CashStrategy()));
    report.CompareStrategies("TRAIN", strategies, data, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees);
    report.CompareStrategies("VALIDATION", strategies, data, Experiment.ValFrom, Experiment.ValTo, Experiment.TrainFees);

    // How each fitness variant ranks the classic strategies (TRAIN only)
    var years = strategies.Select(s => (s.Name, Years: evaluator.YearlyStats(s.Create, data, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees))).ToList();
    Console.WriteLine();
    Console.WriteLine($"  {"",30} | " + string.Join(" ", years.Select(y => $"{y.Name,20}")));
    foreach (var dd in new[] { 0.25, 0.5 })
    foreach (var rel in new[] { 0.0, 0.25, 0.5, 1.0 })
    foreach (var minExp in new[] { 0.0, 0.3 })
    {
        var ev = new Evaluator(engine, fitness with { DrawdownPenalty = dd, RelativeWeight = rel, MinExposure = minExp });
        Console.WriteLine($"  dd {dd,4} rel {rel,4} minExp {minExp,4} | " + string.Join(" ", years.Select(y => $"{ev.Score(y.Years),20:F3}")));
    }
}

sealed class CashStrategy : IStrategy
{
    public void Initialize(IEnumerable<HermesTrade.Core.Models.Candle> history) { }
    public HermesTrade.Core.Enums.Signal Evaluate(List<HermesTrade.Core.Models.StrategyContext> history) => HermesTrade.Core.Enums.Signal.Hold;
}

sealed class Options
{
    public string Command { get; private init; } = "";
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Options Parse(string[] args)
    {
        var o = new Options { Command = args.FirstOrDefault()?.ToLowerInvariant() ?? "" };
        for (int i = 1; i < args.Length; i++)
            if (args[i].StartsWith("--") && i + 1 < args.Length)
                o._values[args[i][2..]] = args[++i];
        return o;
    }

    /// <summary>Options typed in the interactive menu (null answers are skipped).</summary>
    public static Options Create(string command, params (string Key, string? Value)[] values)
    {
        var o = new Options { Command = command };
        foreach (var (key, value) in values)
            if (value is not null)
                o._values[key] = value;
        return o;
    }

    public string Get(string key, string fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

    public double GetDouble(string key, double fallback) => _values.TryGetValue(key, out var v) ? double.Parse(v.Replace(',', '.'), CultureInfo.InvariantCulture) : fallback;

    public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    public DateTime GetDate(string key, DateTime fallback) =>
        _values.TryGetValue(key, out var v)
            ? DateTime.SpecifyKind(DateTime.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc)
            : fallback;
}
