using HermesTrade.Core.Models;
using HermesTrade.Interfaces;
using Skender.Stock.Indicators;

namespace HermesTrade.Indicators;

/// <summary>
/// Default implementation of <see cref="IIndicatorService"/> that uses
/// Skender.Stock.Indicators to pre-compute all standard indicators once
/// from the full candle history, then serves them via O(1) index lookups.
/// </summary>
public sealed class DefaultIndicatorService : IIndicatorService
{
    private IndicatorSnapshot[] _snapshots = [];

    /// <inheritdoc />
    public void Initialize(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (candles.Count == 0)
        {
            _snapshots = [];
            return;
        }

        var quotes = candles.Select(ToQuote).ToList();

        // ── Pre-compute all series ────────────────────────────────────────────

        var rsiResults    = quotes.GetRsi(14).ToArray();
        var sma10Results  = quotes.GetSma(10).ToArray();
        var sma20Results  = quotes.GetSma(20).ToArray();
        var sma50Results  = quotes.GetSma(50).ToArray();
        var sma100Results = quotes.GetSma(100).ToArray();
        var sma200Results = quotes.GetSma(200).ToArray();
        var ema20Results  = quotes.GetEma(20).ToArray();
        var macdResults   = quotes.GetMacd(12, 26, 9).ToArray();
        var bbResults     = quotes.GetBollingerBands(20, 2).ToArray();
        var atrResults    = quotes.GetAtr(14).ToArray();
        var ichResults    = quotes.GetIchimoku(9, 26, 52).ToArray();
        var adxResults    = quotes.GetAdx(14).ToArray();
        var cciResults    = quotes.GetCci(20).ToArray();
        var obvResults    = quotes.GetObv().ToArray();
        var stochResults  = quotes.GetStoch(14, 3, 3).ToArray();

        // ── Build per-candle snapshots ────────────────────────────────────────

        _snapshots = new IndicatorSnapshot[candles.Count];

        for (int i = 0; i < candles.Count; i++)
        {
            _snapshots[i] = new IndicatorSnapshot
            {
                RSI           = ToDecimal(rsiResults[i].Rsi),
                SMA10         = ToDecimal(sma10Results[i].Sma),
                SMA20         = ToDecimal(sma20Results[i].Sma),
                SMA50         = ToDecimal(sma50Results[i].Sma),
                SMA100        = ToDecimal(sma100Results[i].Sma),
                SMA200        = ToDecimal(sma200Results[i].Sma),
                EMA           = ToDecimal(ema20Results[i].Ema),
                MACD          = ToDecimal(macdResults[i].Macd),
                MACDSignal    = ToDecimal(macdResults[i].Signal),
                BollingerUpper  = ToDecimal(bbResults[i].UpperBand),
                BollingerMiddle = ToDecimal(bbResults[i].Sma),
                BollingerLower  = ToDecimal(bbResults[i].LowerBand),
                ATR           = ToDecimal(atrResults[i].Atr),
                TenkanSen     = ichResults[i].TenkanSen,
                KijunSen      = ichResults[i].KijunSen,
                SenkouSpanA   = ichResults[i].SenkouSpanA,
                SenkouSpanB   = ichResults[i].SenkouSpanB,
                ADX           = ToDecimal(adxResults[i].Adx),
                CCI           = ToDecimal(cciResults[i].Cci),
                OBV           = (decimal)obvResults[i].Obv,
                StochasticK   = ToDecimal(stochResults[i].K),
                StochasticD   = ToDecimal(stochResults[i].D),
            };
        }
    }

    /// <inheritdoc />
    public IndicatorSnapshot GetSnapshot(int index)
    {
        if (_snapshots.Length == 0)
            throw new InvalidOperationException(
                $"{nameof(DefaultIndicatorService)} has not been initialized. Call {nameof(Initialize)} first.");

        if ((uint)index >= (uint)_snapshots.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"Index must be in range [0, {_snapshots.Length - 1}].");

        return _snapshots[index];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Quote ToQuote(Candle c) =>
        new()
        {
            Date   = c.Timestamp,
            Open   = c.Open,
            High   = c.High,
            Low    = c.Low,
            Close  = c.Close,
            Volume = c.Volume,
        };

    private static decimal? ToDecimal(double? value) =>
        value.HasValue ? (decimal)value.Value : null;
}
