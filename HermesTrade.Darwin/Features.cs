using HermesTrade.Core.Models;

namespace HermesTrade.Darwin;

/// <summary>
/// Turns candles + HermesTrade indicator snapshots into scale-free inputs for the network.
/// Every value is normalised by the asset's own volatility (or is a bounded oscillator or
/// a -1/0/+1 signal), so the same weights mean the same thing on BTC, gold or a bond ETF.
/// All features are causal: row i only uses candles 0..i.
/// </summary>
public static class Features
{
    /// <summary>Market features precomputed per candle (the portfolio features are appended at run time).</summary>
    public static readonly string[] MarketNames =
    [
        "rsi", "dist_sma20", "dist_sma50", "dist_sma200", "sma50_vs_sma200", "macd_hist",
        "bollinger_pos", "stochastic", "adx", "cci", "mom_1m", "mom_3m", "mom_6m", "mom_12m",
        "vol_regime", "drawdown_1y", "ichimoku_cloud", "obv_flow",
        // Classic trading signals (+1 buy / -1 sell / 0 neutral)
        "sig_rsi_30_70", "sig_golden_cross", "sig_macd_cross", "sig_bollinger_break",
        "sig_donchian_55", "sig_tenkan_kijun", "sig_trend_200", "sig_stoch_20_80",
    ];

    public static readonly string[] PortfolioNames = ["in_position", "unrealized_pnl", "days_in_trade"];

    public static int MarketCount => MarketNames.Length;

    public static int InputCount => MarketNames.Length + PortfolioNames.Length;

    public static IEnumerable<string> AllNames => MarketNames.Concat(PortfolioNames);

    /// <summary>Minimum candles of history before row i is usable (12-month momentum / 1-year high).</summary>
    public const int Lookback = 253;

    /// <summary>
    /// Builds the feature matrix. <paramref name="ready"/>[i] is false while any input is
    /// not warmed up yet; the strategy stays in cash on those candles.
    /// </summary>
    public static double[][] Build(IReadOnlyList<Candle> candles, IndicatorSnapshot[] snaps, out bool[] ready, out double[] sigma)
    {
        int n = candles.Count;
        var close  = new double[n];
        var high   = new double[n];
        var low    = new double[n];
        var volume = new double[n];
        for (int i = 0; i < n; i++)
        {
            close[i]  = (double)candles[i].Close;
            high[i]   = (double)candles[i].High;
            low[i]    = (double)candles[i].Low;
            volume[i] = (double)candles[i].Volume;
        }

        var logRet = new double[n];
        for (int i = 1; i < n; i++)
            logRet[i] = Math.Log(close[i] / close[i - 1]);

        var sigma20  = RollingStd(logRet, 20);
        var sigma100 = RollingStd(logRet, 100);
        sigma = sigma100;

        var rows = new double[n][];
        ready = new bool[n];

        for (int i = 0; i < n; i++)
        {
            var row = new double[MarketCount];
            rows[i] = row;

            var s = snaps[i];
            double sg = sigma100[i];
            if (i < Lookback || !(sg > 1e-6) || !(sigma20[i] > 1e-7) ||
                s.RSI is null || s.SMA20 is null || s.SMA50 is null || s.SMA200 is null ||
                s.MACD is null || s.MACDSignal is null || s.BollingerUpper is null || s.BollingerLower is null ||
                s.StochasticK is null || s.ADX is null || s.CCI is null || s.OBV is null ||
                s.SenkouSpanA is null || s.SenkouSpanB is null || s.TenkanSen is null || s.KijunSen is null)
                continue;

            double c      = close[i];
            double rsi    = (double)s.RSI.Value;
            double sma20  = (double)s.SMA20.Value;
            double sma50  = (double)s.SMA50.Value;
            double sma200 = (double)s.SMA200.Value;
            double macd   = (double)s.MACD.Value;
            double macdS  = (double)s.MACDSignal.Value;
            double bbU    = (double)s.BollingerUpper.Value;
            double bbL    = (double)s.BollingerLower.Value;
            double stoch  = (double)s.StochasticK.Value;
            double adx    = (double)s.ADX.Value;
            double cci    = (double)s.CCI.Value;
            double spanA  = (double)s.SenkouSpanA.Value;
            double spanB  = (double)s.SenkouSpanB.Value;
            double tenkan = (double)s.TenkanSen.Value;
            double kijun  = (double)s.KijunSen.Value;

            if (sma20 <= 0 || sma50 <= 0 || sma200 <= 0 || spanA <= 0 || spanB <= 0)
                continue;

            int k = 0;
            row[k++] = (rsi - 50) / 50;
            row[k++] = Math.Tanh(Math.Log(c / sma20)  / (sg * Math.Sqrt(20)));
            row[k++] = Math.Tanh(Math.Log(c / sma50)  / (sg * Math.Sqrt(50)));
            row[k++] = Math.Tanh(Math.Log(c / sma200) / (sg * Math.Sqrt(200)));
            row[k++] = Math.Tanh(Math.Log(sma50 / sma200) / (sg * Math.Sqrt(200)));
            row[k++] = Math.Tanh((macd - macdS) / (c * sg));
            row[k++] = bbU > bbL ? Math.Clamp(2 * (c - bbL) / (bbU - bbL) - 1, -1.5, 1.5) / 1.5 : 0;
            row[k++] = (stoch - 50) / 50;
            row[k++] = Math.Clamp((adx - 25) / 25, -1, 1);
            row[k++] = Math.Tanh(cci / 200);
            row[k++] = Momentum(close, i, 21, sg);
            row[k++] = Momentum(close, i, 63, sg);
            row[k++] = Momentum(close, i, 126, sg);
            row[k++] = Momentum(close, i, 252, sg);
            row[k++] = Math.Tanh(2 * Math.Log(sigma20[i] / sg));
            row[k++] = Math.Tanh(Math.Log(c / Max(close, i - 251, i)) / (sg * Math.Sqrt(63)));
            double top = Math.Max(spanA, spanB), bottom = Math.Min(spanA, spanB);
            row[k++] = c > top ? Math.Tanh(Math.Log(c / top) / (sg * Math.Sqrt(26)))
                     : c < bottom ? Math.Tanh(Math.Log(c / bottom) / (sg * Math.Sqrt(26)))
                     : 0;
            row[k++] = ObvFlow(snaps, volume, i);

            row[k++] = rsi < 30 ? 1 : rsi > 70 ? -1 : 0;
            row[k++] = sma50 > sma200 ? 1 : -1;
            row[k++] = macd > macdS ? 1 : -1;
            row[k++] = c > bbU ? 1 : c < bbL ? -1 : 0;
            row[k++] = c >= Max(high, i - 55, i - 1) ? 1 : c <= Min(low, i - 55, i - 1) ? -1 : 0;
            row[k++] = tenkan > kijun ? 1 : -1;
            row[k++] = c > sma200 ? 1 : -1;
            row[k++] = stoch < 20 ? 1 : stoch > 80 ? -1 : 0;

            ready[i] = row.All(double.IsFinite);
        }

        return rows;
    }

    private static double Momentum(double[] close, int i, int days, double sg) =>
        Math.Tanh(Math.Log(close[i] / close[i - days]) / (sg * Math.Sqrt(days)));

    // Net volume flow of the last 20 candles, in [-1, 1]
    private static double ObvFlow(IndicatorSnapshot[] snaps, double[] volume, int i)
    {
        if (snaps[i - 20].OBV is not decimal obvPast)
            return 0;

        double total = 0;
        for (int j = i - 19; j <= i; j++)
            total += volume[j];

        return total > 0 ? Math.Clamp(((double)snaps[i].OBV!.Value - (double)obvPast) / total, -1, 1) : 0;
    }

    private static double Max(double[] v, int from, int to)
    {
        double m = double.MinValue;
        for (int j = Math.Max(0, from); j <= to; j++)
            if (v[j] > m) m = v[j];
        return m;
    }

    private static double Min(double[] v, int from, int to)
    {
        double m = double.MaxValue;
        for (int j = Math.Max(0, from); j <= to; j++)
            if (v[j] < m) m = v[j];
        return m;
    }

    private static double[] RollingStd(double[] x, int window)
    {
        var result = new double[x.Length];
        double sum = 0, sumSq = 0;
        for (int i = 1; i < x.Length; i++)
        {
            sum += x[i];
            sumSq += x[i] * x[i];
            if (i > window)
            {
                sum -= x[i - window];
                sumSq -= x[i - window] * x[i - window];
            }

            if (i >= window)
            {
                double mean = sum / window;
                result[i] = Math.Sqrt(Math.Max(0, sumSq / window - mean * mean));
            }
        }

        return result;
    }
}
