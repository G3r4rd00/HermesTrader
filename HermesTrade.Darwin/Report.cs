using HermesTrade.Interfaces;

namespace HermesTrade.Darwin;

/// <summary>Cross-symbol summary of one strategy over one window.</summary>
public sealed record Aggregate(
    double MeanReturn, double MedianReturn, double ProfitableShare, double MeanCagr,
    double MeanDrawdown, double MeanBuyHold, double MeanBuyHoldDrawdown, double Exposure,
    double TradesPerSymbol, double Score);

public sealed class Report(Evaluator evaluator)
{
    public (List<PeriodStats> Symbols, Aggregate Total) Evaluate(
        Func<SymbolData, IStrategy> create, IReadOnlyList<SymbolData> symbols, DateTime from, DateTime to, decimal fees)
    {
        var stats = new PeriodStats[symbols.Count];
        var years = new List<YearStats>[symbols.Count];
        Parallel.For(0, symbols.Count, i =>
        {
            var d = symbols[i];
            var r = evaluator.Run(create(d), d, Max(from, d.Data.From ?? from), to, fees);
            stats[i] = Evaluator.Summarize(r, d, fees);
            years[i] = Evaluator.SplitYears(r, d, fees);
        });

        var rets = stats.Select(s => s.Return).OrderBy(x => x).ToArray();
        var total = new Aggregate(
            rets.Average(),
            rets.Length % 2 == 1 ? rets[rets.Length / 2] : (rets[rets.Length / 2 - 1] + rets[rets.Length / 2]) / 2,
            stats.Count(s => s.Return > 0) / (double)stats.Length,
            stats.Average(s => s.Cagr),
            stats.Average(s => s.MaxDrawdown),
            stats.Average(s => s.BuyHold),
            stats.Average(s => s.BuyHoldDrawdown),
            stats.Average(s => s.Exposure),
            stats.Average(s => s.Trades),
            evaluator.Score(years.SelectMany(y => y).ToList()));

        return (stats.ToList(), total);
    }

    public void CompareStrategies(
        string title, IReadOnlyList<(string Name, Func<SymbolData, IStrategy> Create)> strategies,
        IReadOnlyList<SymbolData> symbols, DateTime from, DateTime to, decimal fees)
    {
        Console.WriteLine();
        Console.WriteLine($"── {title} ── {from:yyyy-MM-dd} → {to:yyyy-MM-dd} · {symbols.Count} símbolos · comisión {fees:P2}/lado");
        Console.WriteLine($"  {"Estrategia",-24} {"Ret.medio",10} {"Mediana",9} {"%símb>0",8} {"CAGR",8} {"MaxDD",8} {"Expos.",7} {"Trades",7} {"Score",7}");
        foreach (var (name, create) in strategies)
        {
            var (_, a) = Evaluate(create, symbols, from, to, fees);
            Console.WriteLine($"  {name,-24} {a.MeanReturn,10:P1} {a.MedianReturn,9:P1} {a.ProfitableShare,8:P0} {a.MeanCagr,8:P1} {a.MeanDrawdown,8:P1} {a.Exposure,7:P0} {a.TradesPerSymbol,7:F1} {a.Score,7:F3}");
        }
    }

    public void SymbolTable(IBrain brain, IReadOnlyList<SymbolData> symbols, DateTime from, DateTime to, decimal fees, ISet<string> unseen)
    {
        var (stats, _) = Evaluate(d => new NeuralStrategy(brain, d), symbols, from, to, fees);
        Console.WriteLine();
        Console.WriteLine($"── Detalle por símbolo (TEST {from:yyyy-MM-dd} → {to:yyyy-MM-dd}) ──");
        Console.WriteLine($"  {"Símbolo",-9} {"",7} {"Estrategia",11} {"MaxDD",8} {"B&H",10} {"MaxDD B&H",10} {"Expos.",7} {"Trades",7} {"Aciertos",9}");
        foreach (var s in stats.OrderBy(s => unseen.Contains(s.Symbol)).ThenBy(s => s.Symbol))
        {
            string tag = unseen.Contains(s.Symbol) ? "nuevo" : "";
            string mark = s.Return >= 0 ? "✓" : "✗";
            Console.WriteLine($"  {s.Symbol,-9} {tag,7} {mark} {s.Return,9:P1} {s.MaxDrawdown,8:P1} {s.BuyHold,10:P1} {s.BuyHoldDrawdown,10:P1} {s.Exposure,7:P0} {s.Trades,7} {s.WinRate,9:P0}");
        }
    }

    public void YearTable(IBrain brain, IReadOnlyList<SymbolData> symbols, DateTime from, DateTime to, decimal fees)
    {
        var years = new List<YearStats>();
        foreach (var d in symbols)
            years.AddRange(Evaluator.SplitYears(evaluator.Run(new NeuralStrategy(brain, d), d, from, to, fees), d, fees));

        Console.WriteLine();
        Console.WriteLine("── Año a año (media de todos los símbolos) ──");
        Console.WriteLine($"  {"Año",-6} {"Estrategia",11} {"B&H",9} {"MaxDD",8} {"Expos.",7} {"%símb>0",8}");
        foreach (var g in years.GroupBy(y => y.Year).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key,-6} {g.Average(y => y.Return),11:P1} {g.Average(y => y.BuyHold),9:P1} {g.Average(y => y.MaxDrawdown),8:P1} {g.Average(y => y.Exposure),7:P0} {g.Count(y => y.Return > 0) / (double)g.Count(),8:P0}");
    }

    /// <summary>
    /// Null hypothesis: how do untrained random networks do on the same test? If the
    /// champion is not far above them, its result is luck, not learning.
    /// </summary>
    public void RandomBaseline(IBrain champion, IReadOnlyList<SymbolData> symbols, DateTime from, DateTime to, decimal fees, int count, int hidden)
    {
        DarwinGA.MyRandom.SetSeed(12345);
        var brains = Enumerable.Range(0, count).Select(_ => NeuralBrain.Random(Features.InputCount, hidden)).ToList();
        var results = new double[count];
        Parallel.For(0, count, i => results[i] = Evaluate(d => new NeuralStrategy(brains[i], d), symbols, from, to, fees).Total.MeanReturn);
        Array.Sort(results);

        double champ = Evaluate(d => new NeuralStrategy(champion, d), symbols, from, to, fees).Total.MeanReturn;
        double pct = results.Count(r => r < champ) / (double)count;
        Console.WriteLine();
        Console.WriteLine($"── Control: {count} redes aleatorias sin entrenar (TEST, todos los símbolos) ──");
        Console.WriteLine($"  Retorno medio: p10 {results[count / 10]:P1} · mediana {results[count / 2]:P1} · p90 {results[count * 9 / 10]:P1} · máx {results[^1]:P1}");
        Console.WriteLine($"  Campeón: {champ:P1} → mejor que el {pct:P0} de las redes aleatorias");
    }

    public static void FeatureImportance(IReadOnlyList<NeuralBrain> members)
    {
        var names = Features.AllNames.ToArray();
        var imp = new double[names.Length];
        foreach (var m in members)
        {
            var mi = m.InputImportance();
            for (int i = 0; i < imp.Length; i++)
                imp[i] += mi[i] / members.Count;
        }

        Console.WriteLine();
        Console.WriteLine("── Qué mira la red (importancia de cada entrada) ──");
        foreach (var (name, v) in names.Zip(imp).OrderByDescending(x => x.Second).Take(12))
            Console.WriteLine($"  {name,-20} {v,6:P1} {new string('█', (int)Math.Round(v * 150))}");
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
