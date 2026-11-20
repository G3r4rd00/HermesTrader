using HermesTrade.Configuration;
using HermesTrade.Core.Models;
using HermesTrade.Engine;
using HermesTrade.Interfaces;

namespace HermesTrade.Darwin;

/// <summary>One symbol over one calendar year of a backtest.</summary>
public sealed record YearStats(
    string Symbol, int Year, int Days, double Return, double MaxDrawdown,
    double BuyHold, double AssetVol, double Exposure, int Trades);

/// <summary>One symbol over a whole backtest window.</summary>
public sealed record PeriodStats(
    string Symbol, double Return, double Cagr, double MaxDrawdown, double BuyHold,
    double BuyHoldDrawdown, double Exposure, int Trades, double WinRate, double Sharpe);

/// <summary>Runs HermesTrade backtests and turns them into the fitness the GA maximises.</summary>
public sealed class Evaluator(BacktestEngine engine, FitnessSettings fitness)
{
    public FitnessSettings Fitness { get; } = fitness;

    public BacktestResult Run(IStrategy strategy, SymbolData d, DateTime from, DateTime to, decimal fees) =>
        engine.Run(strategy, d.Data, new BacktestConfig
        {
            Symbol = d.Symbol,
            From = from,
            To = to,
            Fees = fees,
            InitialCapital = Experiment.InitialCapital,
            PeriodsPerYear = d.PeriodsPerYear, // skips re-inferring it from the whole series on every run
        });

    /// <summary>Yearly stats of every symbol for <paramref name="create"/> over [from, to].</summary>
    public List<YearStats> YearlyStats(
        Func<SymbolData, IStrategy> create, IEnumerable<SymbolData> symbols, DateTime from, DateTime to, decimal fees)
    {
        var years = new List<YearStats>();
        foreach (var d in symbols)
        {
            if (d.LastDate < from || d.FirstDate > to)
                continue;
            var r = Run(create(d), d, from, to, fees);
            years.AddRange(SplitYears(r, d, fees));
        }

        return years;
    }

    /// <summary>
    /// Fitness of a brain: the robust score over every (symbol, year) of the window, minus
    /// weight decay. Used for training and, on its own period, for validation.
    /// </summary>
    public double BrainScore(NeuralBrain brain, IEnumerable<SymbolData> symbols, DateTime from, DateTime to, decimal fees) =>
        BrainScoreOf(brain, symbols, from, to, fees) - Fitness.WeightDecay * brain.WeightEnergy();

    /// <summary>Score without weight decay (works for ensembles too).</summary>
    public double BrainScoreOf(IBrain brain, IEnumerable<SymbolData> symbols, DateTime from, DateTime to, decimal fees) =>
        Score(YearlyStats(d => new NeuralStrategy(brain, d), symbols, from, to, fees));

    /// <summary>
    /// The anti-overfitting objective, computed over symbol-years so a single lucky year or
    /// asset cannot carry a strategy:
    /// <list type="number">
    /// <item>per symbol-year v = (ln(1+R) − λ·MaxDD) / σ_asset: return net of drawdown, in units of
    ///       that asset's long-run volatility, so BTC and a bond ETF weigh the same;</item>
    /// <item>losing years (v &lt; 0) are multiplied by the loss-aversion factor;</item>
    /// <item>u = v + κ·(ln(1+R) − ln(1+R_buy&amp;hold)) / σ_asset: staying out of a rally costs,
    ///       dodging a crash pays;</item>
    /// <item>score = mean(u) + w · CVaR(worst q of u): the tail term punishes the bad years explicitly;</item>
    /// <item>minus penalties for over-trading, under-trading and for hiding in cash.</item>
    /// </list>
    /// </summary>
    public double Score(IReadOnlyList<YearStats> years)
    {
        if (years.Count == 0)
            return -10;

        var u = new double[years.Count];
        double exposure = 0, trades = 0;
        for (int k = 0; k < years.Count; k++)
        {
            var y = years[k];
            double vol = Math.Max(y.AssetVol, 0.05);
            double growth = Math.Log(1 + y.Return) / vol;
            double v = growth - Fitness.DrawdownPenalty * y.MaxDrawdown / vol;
            if (v < 0) v *= Fitness.LossAversion;
            // Opportunity cost: sitting in cash through a rally is not free
            u[k] = v + Fitness.RelativeWeight * (growth - Math.Log(1 + y.BuyHold) / vol);
            exposure += y.Exposure;
            trades += y.Trades;
        }

        exposure /= years.Count;
        trades /= years.Count;

        Array.Sort(u);
        int tailCount = Math.Max(1, (int)Math.Ceiling(u.Length * Fitness.TailFraction));
        double tail = u.Take(tailCount).Average();

        return u.Average()
               + Fitness.TailWeight * tail
               - Math.Max(0, trades - Fitness.MaxTradesPerYear) * 0.02
               - Math.Max(0, Fitness.MinTradesPerYear - trades) * Fitness.MinTradesPenalty
               - Math.Max(0, Fitness.MinExposure - exposure) * 2;
    }

    /// <summary>
    /// Cuts a backtest into calendar years. Years with under 60 candles, or where the
    /// features were not warmed up for 90% of the candles, are left out.
    /// </summary>
    public static List<YearStats> SplitYears(BacktestResult r, SymbolData d, decimal fees)
    {
        var result = new List<YearStats>();
        var c = r.Candles;
        var eq = r.EquityCurve;
        if (c.Count == 0)
            return result;

        var inPos = PositionFlags(r);
        int offset = IndexOf(d.Data.Candles, c[0].Timestamp);

        // Volatility of the whole window, not of each year: normalising a crash year by its own
        // (huge) volatility would shrink exactly the losses the strategy should learn to avoid
        double sum = 0, sumSq = 0;
        for (int k = 1; k < c.Count; k++)
        {
            double lr = Math.Log((double)c[k].Close / (double)c[k - 1].Close);
            sum += lr;
            sumSq += lr * lr;
        }

        int nRet = Math.Max(1, c.Count - 1);
        double longVol = Math.Sqrt(Math.Max(0, sumSq / nRet - (sum / nRet) * (sum / nRet)) * d.PeriodsPerYear);
        double fee = (double)fees;

        int a = 0;
        while (a < c.Count)
        {
            int year = c[a].Timestamp.Year;
            int b = a;
            while (b + 1 < c.Count && c[b + 1].Timestamp.Year == year)
                b++;

            int days = b - a + 1;
            int readyDays = 0, posDays = 0;
            for (int k = a; k <= b; k++)
            {
                int f = offset + k - NeuralStrategy.ExecutionDelay;
                if (f >= 0 && d.Ready[f]) readyDays++;
                if (inPos[k]) posDays++;
            }

            if (days >= 60 && readyDays >= 0.9 * days)
            {
                double ePrev = a == 0 ? (double)Experiment.InitialCapital : (double)eq[a - 1];
                double peak = ePrev, dd = 0;
                for (int k = a; k <= b; k++)
                {
                    double e = (double)eq[k];
                    if (e > peak) peak = e;
                    dd = Math.Max(dd, 1 - e / peak);
                }

                double pPrev = a == 0 ? (double)c[a].Close * (1 + fee) : (double)c[a - 1].Close;
                int trades = r.Trades.Count(t => t.EntryTime.Year == year);

                result.Add(new YearStats(d.Symbol, year, days, (double)eq[b] / ePrev - 1, dd,
                    (double)c[b].Close / pPrev - 1, longVol, (double)posDays / days, trades));
            }

            a = b + 1;
        }

        return result;
    }

    public static PeriodStats Summarize(BacktestResult r, SymbolData d, decimal fees)
    {
        var c = r.Candles;
        var eq = r.EquityCurve;
        if (c.Count < 2)
            return new PeriodStats(d.Symbol, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        double fee = (double)fees;
        double ret = (double)eq[^1] / (double)Experiment.InitialCapital - 1;
        double years = Math.Max((c[^1].Timestamp - c[0].Timestamp).TotalDays / 365.25, 1.0 / 365);
        double cagr = Math.Pow(1 + ret, 1 / years) - 1;

        double peak = (double)Experiment.InitialCapital, dd = 0;
        foreach (var e in eq)
        {
            peak = Math.Max(peak, (double)e);
            dd = Math.Max(dd, 1 - (double)e / peak);
        }

        double bh = (double)c[^1].Close / ((double)c[0].Close * (1 + fee)) * (1 - fee) - 1;
        double bhPeak = 0, bhDd = 0;
        foreach (var candle in c)
        {
            double p = (double)candle.Close;
            bhPeak = Math.Max(bhPeak, p);
            bhDd = Math.Max(bhDd, 1 - p / bhPeak);
        }

        var flags = PositionFlags(r);
        double exposure = (double)flags.Count(x => x) / flags.Length;

        return new PeriodStats(d.Symbol, ret, cagr, dd, bh, bhDd, exposure, r.TotalTrades, r.WinRate, r.SharpeRatio);
    }

    /// <summary>true on each candle where the strategy held the asset at the close.</summary>
    public static bool[] PositionFlags(BacktestResult r)
    {
        var c = r.Candles;
        var flags = new bool[c.Count];
        foreach (var trade in r.Trades)
        {
            int a = IndexOf(c, trade.EntryTime);
            int b = IndexOf(c, trade.ExitTime);
            for (int k = Math.Max(a, 0); k < b && k < flags.Length; k++)
                flags[k] = true;
        }

        return flags;
    }

    private static int IndexOf(IReadOnlyList<Candle> candles, DateTime ts)
    {
        int lo = 0, hi = candles.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int cmp = candles[mid].Timestamp.CompareTo(ts);
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }

        return lo;
    }
}
