namespace HermesTrade.Darwin;

/// <summary>
/// The whole experiment design in one place. Three disjoint periods:
/// TRAIN (fitness) → VALIDATION (choose the champion, early stopping) → TEST (never seen,
/// only touched by the final report), plus symbols that are never used before the test.
/// </summary>
public static class Experiment
{
    public static readonly DateTime TrainFrom = Utc(2005, 1, 1);
    public static readonly DateTime TrainTo   = Utc(2017, 12, 31);
    public static readonly DateTime ValFrom   = Utc(2018, 1, 1);
    public static readonly DateTime ValTo     = Utc(2021, 12, 31);
    public static readonly DateTime TestFrom  = Utc(2022, 1, 1);
    public static readonly DateTime TestTo    = Utc(2026, 11, 13);

    /// <summary>
    /// Universe the GA learns from: indices, regions, sectors, bonds, commodities, bitcoin and
    /// single stocks, including some that collapsed in 2008 (C, AIG, GE, F) so that protecting
    /// capital is something it has to learn, not something survivorship hides.
    /// </summary>
    public static readonly string[] TrainSymbols =
    [
        "SPY", "QQQ", "IWM", "MDY", "EFA", "EEM", "EWZ", "FXI", "EWU",
        "GLD", "TLT", "IEF", "LQD", "TIP", "IYR", "DBC", "USO",
        "XLE", "XLF", "XLK", "XLV", "XLU", "XLB", "XLY",
        "AAPL", "MSFT", "JPM", "XOM", "WMT", "C", "GE", "AIG", "IBM", "F", "MRK", "BA", "ORCL",
        "BTC-USD",
    ];

    /// <summary>
    /// Never loaded by the GA, not even for validation. INTC and PFE are deliberate stress
    /// cases: they lost a lot during the test period, so they show whether the strategy
    /// protects capital instead of just riding a bull market.
    /// </summary>
    public static readonly string[] UnseenSymbols =
    [
        "DIA", "EWJ", "EWG", "SLV", "XLI", "XLP", "ETH-USD", "AMZN", "KO", "CAT", "INTC", "PFE",
    ];

    /// <summary>Training charges double fees: a strategy that survives 0.2% per side copes with 0.1%.</summary>
    public const decimal TrainFees = 0.002m;

    public const decimal RealFees = 0.001m;

    public const decimal InitialCapital = 10_000m;

    public static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);
}

/// <summary>Knobs of the anti-overfitting fitness (see <see cref="Evaluator.Score"/>).</summary>
public sealed record FitnessSettings
{
    /// <summary>Weight of the year's max drawdown against its log return.</summary>
    public double DrawdownPenalty { get; init; } = 0.25;

    /// <summary>Losing years count this many times more than winning ones.</summary>
    public double LossAversion { get; init; } = 2.0;

    /// <summary>Weight of the return relative to buy &amp; hold of the same asset and year.</summary>
    public double RelativeWeight { get; init; } = 0.5;

    /// <summary>Fraction of worst symbol-years averaged into the tail term (CVaR).</summary>
    public double TailFraction { get; init; } = 0.2;

    /// <summary>Weight of the CVaR tail term.</summary>
    public double TailWeight { get; init; } = 0.25;

    /// <summary>L2 weight decay on the network.</summary>
    public double WeightDecay { get; init; } = 0.01;

    /// <summary>Trades per symbol-year allowed before an extra penalty.</summary>
    public double MaxTradesPerYear { get; init; } = 12;

    /// <summary>Minimum average exposure before the "always in cash" penalty kicks in.</summary>
    public double MinExposure { get; init; } = 0.0;
}

public sealed record EvolutionSettings
{
    public int Islands { get; init; } = 4;
    public int PopulationPerIsland { get; init; } = 50;
    public int Generations { get; init; } = 120;
    public int Hidden { get; init; } = 6;
    public double MutationProbability { get; init; } = 0.05;
    public int MigrationInterval { get; init; } = 10;
    public int Migrants { get; init; } = 2;
    public int Seed { get; init; } = 2026;

    /// <summary>Per island, how many of each generation's best are scored on validation.</summary>
    public int ValidatePerIsland { get; init; } = 2;

    /// <summary>Share of TRAIN symbols each generation is scored on (resampled every generation).</summary>
    public double BagFraction { get; init; } = 0.7;

    /// <summary>Ensemble size: best-validation brains, at most one per island lineage first.</summary>
    public int EnsembleSize { get; init; } = 5;
}
