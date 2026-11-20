using System.Text.Json.Serialization;
using DarwinGA;
using DarwinGA.Interfaces;

namespace HermesTrade.Darwin;

/// <summary>Anything that maps the input vector to a long/flat decision.</summary>
public interface IBrain
{
    double EntryThreshold { get; }
    double ExitThreshold { get; }

    /// <summary>Network output in (-1, 1): high means "be long", low means "be in cash".</summary>
    double Output(ReadOnlySpan<double> inputs, Span<double> scratch);

    int ScratchSize { get; }
}

/// <summary>
/// The chromosome: a small MLP (inputs → tanh hidden → tanh output) plus two evolved
/// hysteresis thresholds. Each hidden neuron, together with its outgoing weight, is an
/// additive "voter": output = tanh(Σ v_h · tanh(w_h · x + b_h) + b). Crossover swaps whole
/// voters between parents, so evolution literally mixes sub-strategies.
/// </summary>
public sealed class NeuralBrain : IGAEvolutional<NeuralBrain>, IBrain
{
    public int Inputs { get; set; }
    public int Hidden { get; set; }

    /// <summary>Hidden × (Inputs + 1) row-major; the last column of each row is the bias.</summary>
    public double[] W1 { get; set; } = [];

    /// <summary>Hidden output weights followed by the output bias (Hidden + 1).</summary>
    public double[] W2 { get; set; } = [];

    public double EntryThreshold { get; set; }
    public double ExitThreshold { get; set; }

    /// <summary>Training fitness (NaN = not evaluated), cleared by mutation so elites are not re-evaluated.</summary>
    [JsonIgnore] public double CachedFitness { get; set; } = double.NaN;

    /// <summary>Generation (symbol bag) the cached fitness belongs to.</summary>
    [JsonIgnore] public int CachedGeneration { get; set; } = -1;

    [JsonIgnore] public int ScratchSize => Hidden;

    public static NeuralBrain Random(int inputs, int hidden)
    {
        var b = new NeuralBrain
        {
            Inputs = inputs,
            Hidden = hidden,
            W1 = new double[hidden * (inputs + 1)],
            W2 = new double[hidden + 1],
        };

        double s1 = 1.0 / Math.Sqrt(inputs);
        double s2 = 1.0 / Math.Sqrt(hidden);
        for (int i = 0; i < b.W1.Length; i++) b.W1[i] = Gaussian() * s1;
        for (int i = 0; i < b.W2.Length; i++) b.W2[i] = Gaussian() * s2;

        b.EntryThreshold = MyRandom.NextDouble(0.0, 0.5);
        b.ExitThreshold  = MyRandom.NextDouble(-0.5, 0.0);
        return b;
    }

    public double Output(ReadOnlySpan<double> x, Span<double> h)
    {
        int stride = Inputs + 1;
        double sum = W2[Hidden];
        for (int j = 0; j < Hidden; j++)
        {
            var row = W1.AsSpan(j * stride, stride);
            double a = row[Inputs];
            for (int i = 0; i < Inputs; i++)
                a += row[i] * x[i];
            h[j] = Math.Tanh(a);
            sum += W2[j] * h[j];
        }

        return Math.Tanh(sum);
    }

    public NeuralBrain Clone() => new()
    {
        Inputs = Inputs,
        Hidden = Hidden,
        W1 = (double[])W1.Clone(),
        W2 = (double[])W2.Clone(),
        EntryThreshold = EntryThreshold,
        ExitThreshold = ExitThreshold,
        CachedFitness = CachedFitness,
    };

    /// <summary>Mean squared weight, used as an L2 (weight decay) penalty.</summary>
    public double WeightEnergy()
    {
        double s = 0;
        foreach (var w in W1) s += w * w;
        foreach (var w in W2) s += w * w;
        return s / (W1.Length + W2.Length);
    }

    /// <summary>
    /// Which inputs this brain listens to: Σ_h |v_h| · |w_hi|, normalised to sum 1.
    /// </summary>
    public double[] InputImportance()
    {
        var imp = new double[Inputs];
        int stride = Inputs + 1;
        for (int j = 0; j < Hidden; j++)
            for (int i = 0; i < Inputs; i++)
                imp[i] += Math.Abs(W2[j]) * Math.Abs(W1[j * stride + i]);

        double total = imp.Sum();
        if (total > 0)
            for (int i = 0; i < imp.Length; i++)
                imp[i] /= total;
        return imp;
    }

    internal static double Gaussian()
    {
        double u1 = 1.0 - MyRandom.NextDouble();
        double u2 = MyRandom.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

/// <summary>
/// Majority vote: each member applies its own thresholds to the shared position (flat: "buy?",
/// long: "stay?") and the ensemble follows the majority. Averaging raw outputs does not work:
/// the members are different non-linear functions with different thresholds.
/// </summary>
public sealed class EnsembleBrain(IReadOnlyList<NeuralBrain> members) : IBrain
{
    public IReadOnlyList<NeuralBrain> Members { get; } = members;

    public double EntryThreshold => 0;
    public double ExitThreshold => 0;

    public int ScratchSize { get; } = members.Max(m => m.Hidden);

    /// <summary>Share of members that want to be long, mapped to [-1, 1].</summary>
    public double Output(ReadOnlySpan<double> inputs, Span<double> scratch)
    {
        bool inPosition = inputs[Features.MarketCount] > 0;
        int votes = 0;
        foreach (var m in Members)
        {
            double o = m.Output(inputs, scratch);
            bool wantsLong = inPosition ? o >= m.ExitThreshold : o > m.EntryThreshold;
            votes += wantsLong ? 1 : -1;
        }

        return (double)votes / Members.Count;
    }
}
