using System.Collections.Concurrent;
using System.Diagnostics;
using DarwinGA;
using DarwinGA.IslandModel;
using DarwinGA.Reinsertions;
using DarwinGA.Terminations;

namespace HermesTrade.Darwin;

/// <summary>A brain that reached the top of its island, with its train and validation scores.</summary>
public sealed record Candidate(NeuralBrain Brain, int Island, int Generation, double Train, double Validation)
{
    /// <summary>Must be good in both regimes, not just in the one it was optimised for.</summary>
    public double Selection => Math.Min(Train, Validation);
}

/// <summary>
/// Island-model neuroevolution with DarwinGA. The GA only ever sees TRAIN, and each
/// generation only a random subset of its symbols (bagging), so nothing can be memorised.
/// Every generation the best brains of each island are scored on the full TRAIN and on
/// VALIDATION and kept in a hall of fame; the final ensemble is picked from it.
/// </summary>
public sealed class Evolution(Evaluator evaluator, IReadOnlyList<SymbolData> symbols, EvolutionSettings settings)
{
    private readonly List<Candidate> _hallOfFame = [];
    private readonly HashSet<NeuralBrain> _validated = new(ReferenceEqualityComparer.Instance);
    private readonly Random _bagRandom = new(settings.Seed);
    private SymbolData[] _bag = [];
    private int _generation;

    public IReadOnlyList<Candidate> HallOfFame => _hallOfFame;

    public double TrainScore(NeuralBrain b) =>
        evaluator.BrainScore(b, symbols, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees);

    public double ValidationScore(NeuralBrain b) =>
        evaluator.BrainScore(b, symbols, Experiment.ValFrom, Experiment.ValTo, Experiment.TrainFees);

    private double BagScore(NeuralBrain b) =>
        evaluator.BrainScore(b, _bag, Experiment.TrainFrom, Experiment.TrainTo, Experiment.TrainFees);

    public List<Candidate> Run(CancellationToken cancellationToken = default)
    {
        MyRandom.SetSeed(settings.Seed);
        NewBag();

        var recorders = new ConcurrentQueue<NeuralBrain>[settings.Islands];
        int islandCounter = 0;
        var clock = Stopwatch.StartNew();

        var model = new IslandModelGeneticAlgorithm<NeuralBrain>(settings.Islands)
        {
            MigrationIntervalGenerations = settings.MigrationInterval,
            MigrantsPerIsland = settings.Migrants,
            MigrationTopology = MigrationTopology.Ring,
            CreateIslandAlgorithm = () =>
            {
                var recorder = recorders[islandCounter++] = new ConcurrentQueue<NeuralBrain>();
                return new GeneticAlgorithm<NeuralBrain>
                {
                    PopulationCreator = n => Enumerable.Range(0, n)
                        .Select(_ => NeuralBrain.Random(Features.InputCount, settings.Hidden))
                        .ToList(),
                    Fitness = b =>
                    {
                        // Re-scored when the bag changes: elites must keep proving themselves
                        if (double.IsNaN(b.CachedFitness) || b.CachedGeneration != _generation)
                        {
                            b.CachedFitness = BagScore(b);
                            b.CachedGeneration = _generation;
                        }

                        recorder.Enqueue(b);
                        return b.CachedFitness;
                    },
                    Mutation = new NeuralMutation(),
                    Cross = new NeuralCrossover(),
                    MutationProbability = settings.MutationProbability,
                    Selection = new ElitistTournamentSelection(elites: 2, tournamentSize: 3, selectionFraction: 0.5),
                    Reinsert = new ReinsertBest<NeuralBrain>(2),
                    Termination = new GenerationNumTermination(settings.Generations),
                    CloneElement = b => b.Clone(),
                    EnableParallelEvaluation = true,
                    OnNewGeneration = _ => { }, // replaced by the island model
                };
            },
            OnNewGeneration = r => OnGeneration(r.BestResult.GenerationNum, recorders, clock),
        };

        model.Run(settings.PopulationPerIsland, cancellationToken);
        return _hallOfFame;
    }

    private void NewBag()
    {
        int take = Math.Max(2, (int)Math.Round(symbols.Count * settings.BagFraction));
        _bag = symbols.OrderBy(_ => _bagRandom.Next()).Take(take).ToArray();
    }

    private void OnGeneration(int generation, ConcurrentQueue<NeuralBrain>[] recorders, Stopwatch clock)
    {
        // DarwinGA reports the first selected individual as "best": rank what was evaluated instead
        var fresh = new List<(NeuralBrain Brain, int Island)>();
        double bestBag = double.NegativeInfinity, sumBag = 0;
        int count = 0;

        for (int island = 0; island < recorders.Length; island++)
        {
            var seen = new HashSet<NeuralBrain>(ReferenceEqualityComparer.Instance);
            while (recorders[island].TryDequeue(out var b))
                if (b.CachedGeneration == _generation)
                    seen.Add(b);

            foreach (var b in seen)
            {
                bestBag = Math.Max(bestBag, b.CachedFitness);
                sumBag += b.CachedFitness;
                count++;
            }

            fresh.AddRange(seen.OrderByDescending(b => b.CachedFitness)
                .Take(settings.ValidatePerIsland)
                .Where(b => !_validated.Contains(b))
                .Select(b => (b, island)));
        }

        var scored = new Candidate[fresh.Count];
        Parallel.For(0, fresh.Count, i =>
        {
            var (b, island) = fresh[i];
            scored[i] = new Candidate(b, island, generation, TrainScore(b), ValidationScore(b));
        });

        foreach (var c in scored)
        {
            _validated.Add(c.Brain);
            _hallOfFame.Add(c);
        }

        var bestSoFar = _hallOfFame.MaxBy(c => c.Selection);
        string newTrain = scored.Length > 0 ? $"{scored.Max(c => c.Train),7:F3}" : "      -";
        string newVal = scored.Length > 0 ? $"{scored.Max(c => c.Validation),7:F3}" : "      -";
        Console.WriteLine(
            $"  gen {generation,3} | bag best {bestBag,7:F3} avg {sumBag / Math.Max(count, 1),7:F3} | " +
            $"nuevos: train {newTrain} val {newVal} | mejor min(train,val) {bestSoFar?.Selection,7:F3} @gen {bestSoFar?.Generation,3} | {clock.Elapsed:mm\\:ss}");

        _generation++;
        NewBag();
    }

    /// <summary>
    /// Picks an odd-sized voting ensemble from the hall of fame by min(train, validation):
    /// first the best brain of each island (different lineages), then the next best overall.
    /// </summary>
    public static List<Candidate> PickEnsemble(IReadOnlyList<Candidate> hallOfFame, int size)
    {
        var ranked = hallOfFame.OrderByDescending(c => c.Selection).ToList();
        var picked = ranked.GroupBy(c => c.Island).Select(g => g.First())
            .OrderByDescending(c => c.Selection).Take(size).ToList();

        foreach (var c in ranked)
        {
            if (picked.Count >= size)
                break;
            if (!picked.Any(p => ReferenceEquals(p.Brain, c.Brain)))
                picked.Add(c);
        }

        if (picked.Count % 2 == 0 && picked.Count > 1)
            picked.RemoveAt(picked.Count - 1); // odd count: no tied votes

        return picked.OrderByDescending(c => c.Selection).ToList();
    }
}
