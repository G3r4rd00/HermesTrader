using System.Text.Json;

namespace HermesTrade.Darwin;

/// <summary>The evolved strategy on disk: ensemble weights plus how it was obtained.</summary>
public sealed class ModelFile
{
    public string Name { get; set; } = "Darwin Neural Ensemble";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string[] Features { get; set; } = [];
    public List<NeuralBrain> Members { get; set; } = [];
    public List<MemberInfo> MemberInfo { get; set; } = [];
    public string TrainPeriod { get; set; } = "";
    public string ValidationPeriod { get; set; } = "";
    public string TestPeriod { get; set; } = "";
    public string[] TrainSymbols { get; set; } = [];
    public string[] UnseenSymbols { get; set; } = [];
    public EvolutionSettings? Evolution { get; set; }
    public FitnessSettings? Fitness { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public EnsembleBrain ToBrain()
    {
        if (!Features.SequenceEqual(HermesTrade.Darwin.Features.AllNames))
            throw new InvalidOperationException("The model was trained with a different feature set.");
        return new EnsembleBrain(Members);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public static ModelFile Load(string path) =>
        JsonSerializer.Deserialize<ModelFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidOperationException($"Could not read {path}.");
}

public sealed record MemberInfo(int Island, int Generation, double TrainScore, double ValidationScore);
