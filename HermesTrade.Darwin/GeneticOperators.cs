using DarwinGA;
using DarwinGA.Interfaces;

namespace HermesTrade.Darwin;

/// <summary>
/// Gaussian mutation: each gene moves by N(0, sigma) with probability <c>mutationProb</c>
/// and, rarely, is reset to a fresh random value (escapes dead neurons).
/// </summary>
public sealed class NeuralMutation(double sigma = 0.25, double resetFraction = 0.1) : IMutation<NeuralBrain>
{
    public void Apply(NeuralBrain b, double mutationProb)
    {
        double initW1 = 1.0 / Math.Sqrt(b.Inputs);
        double initW2 = 1.0 / Math.Sqrt(b.Hidden);
        bool changed = MutateArray(b.W1, mutationProb, initW1);
        changed |= MutateArray(b.W2, mutationProb, initW2);

        // Thresholds are only two genes: give them a higher chance to move
        double pt = Math.Min(1.0, mutationProb * 5);
        if (MyRandom.NextDouble() < pt) { b.EntryThreshold += NeuralBrain.Gaussian() * 0.05; changed = true; }
        if (MyRandom.NextDouble() < pt) { b.ExitThreshold  += NeuralBrain.Gaussian() * 0.05; changed = true; }

        b.EntryThreshold = Math.Clamp(b.EntryThreshold, -0.9, 0.9);
        b.ExitThreshold  = Math.Clamp(b.ExitThreshold, -0.9, 0.9);
        if (b.ExitThreshold > b.EntryThreshold)
            (b.EntryThreshold, b.ExitThreshold) = (b.ExitThreshold, b.EntryThreshold);

        if (changed)
            b.CachedFitness = double.NaN;
    }

    private bool MutateArray(double[] w, double p, double initScale)
    {
        bool changed = false;
        for (int i = 0; i < w.Length; i++)
        {
            if (MyRandom.NextDouble() >= p)
                continue;

            w[i] = MyRandom.NextDouble() < resetFraction
                ? NeuralBrain.Gaussian() * initScale
                : w[i] + NeuralBrain.Gaussian() * sigma * initScale * 2;
            changed = true;
        }

        return changed;
    }
}

/// <summary>
/// Tournament selection that always lets the top <c>elites</c> through, listed first.
/// DarwinGA's ReinsertBest picks survivors among the selected individuals and reports the
/// first one as the generation's best; with a plain tournament the true best can miss every
/// tournament and be lost. Putting the elites first fixes both.
/// </summary>
public sealed class ElitistTournamentSelection(int elites = 2, int tournamentSize = 3, double selectionFraction = 0.5) : ISelection
{
    public IEnumerable<FitnessResult> Select(IEnumerable<FitnessResult> population)
    {
        var list = population.ToList();
        int toSelect = Math.Clamp((int)Math.Round(selectionFraction * list.Count), 2, list.Count);
        var result = list.OrderByDescending(r => r.FitnessValue).Take(Math.Min(elites, toSelect)).ToList();

        while (result.Count < toSelect)
        {
            FitnessResult best = list[MyRandom.NextInt(list.Count)];
            for (int k = 1; k < tournamentSize; k++)
            {
                var c = list[MyRandom.NextInt(list.Count)];
                if (c.FitnessValue > best.FitnessValue)
                    best = c;
            }

            result.Add(best);
        }

        return result;
    }
}

/// <summary>
/// Voter crossover: every hidden neuron (incoming weights, bias and outgoing weight) is
/// inherited whole from one parent, so children combine complete sub-strategies instead of
/// scrambling weights. Output bias and thresholds are blended.
/// </summary>
public sealed class NeuralCrossover : ICross<NeuralBrain>
{
    public NeuralBrain Apply(NeuralBrain a, NeuralBrain b)
    {
        var child = a.Clone();
        child.CachedFitness = double.NaN;
        if (ReferenceEquals(a, b))
            return child;

        int stride = a.Inputs + 1;
        for (int j = 0; j < a.Hidden; j++)
        {
            if (!MyRandom.NextBool())
                continue;

            Array.Copy(b.W1, j * stride, child.W1, j * stride, stride);
            child.W2[j] = b.W2[j];
        }

        double t = MyRandom.NextDouble();
        child.W2[a.Hidden]     = t * a.W2[a.Hidden] + (1 - t) * b.W2[b.Hidden];
        child.EntryThreshold   = t * a.EntryThreshold + (1 - t) * b.EntryThreshold;
        child.ExitThreshold    = t * a.ExitThreshold + (1 - t) * b.ExitThreshold;
        return child;
    }
}
