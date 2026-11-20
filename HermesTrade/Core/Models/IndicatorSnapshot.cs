namespace HermesTrade.Core.Models;

/// <summary>
/// A snapshot of all pre-computed technical indicator values for a given candle index.
/// </summary>
public sealed class IndicatorSnapshot
{
    // ── Momentum ─────────────────────────────────────────────────────────────

    /// <summary>Relative Strength Index (default period 14).</summary>
    public decimal? RSI { get; set; }

    // ── Moving Averages ───────────────────────────────────────────────────────

    public decimal? SMA10 { get; set; }
    public decimal? SMA20 { get; set; }
    public decimal? SMA50 { get; set; }
    public decimal? SMA100 { get; set; }
    public decimal? SMA200 { get; set; }

    /// <summary>Exponential Moving Average (default period 20).</summary>
    public decimal? EMA { get; set; }

    // ── MACD ─────────────────────────────────────────────────────────────────

    public decimal? MACD { get; set; }
    public decimal? MACDSignal { get; set; }

    // ── Bollinger Bands ───────────────────────────────────────────────────────

    public decimal? BollingerUpper { get; set; }
    public decimal? BollingerLower { get; set; }
    public decimal? BollingerMiddle { get; set; }

    // ── Volatility ────────────────────────────────────────────────────────────

    /// <summary>Average True Range (default period 14).</summary>
    public decimal? ATR { get; set; }

    // ── Ichimoku ─────────────────────────────────────────────────────────────

    public decimal? TenkanSen { get; set; }
    public decimal? KijunSen { get; set; }
    public decimal? SenkouSpanA { get; set; }
    public decimal? SenkouSpanB { get; set; }

    // ── Additional ───────────────────────────────────────────────────────────

    /// <summary>Average Directional Index (default period 14).</summary>
    public decimal? ADX { get; set; }

    /// <summary>Commodity Channel Index (default period 20).</summary>
    public decimal? CCI { get; set; }

    /// <summary>On-Balance Volume.</summary>
    public decimal? OBV { get; set; }

    public decimal? StochasticK { get; set; }
    public decimal? StochasticD { get; set; }

    // ── Extensibility ────────────────────────────────────────────────────────

    private Dictionary<string, decimal>? _custom;

    /// <summary>Arbitrary named indicators added by custom strategies.</summary>
    public Dictionary<string, decimal> Custom
    {
        get => _custom ??= new();
        set => _custom = value;
    }

    /// <summary>
    /// Returns an independent copy (including <see cref="Custom"/>), so changes made
    /// by one backtest run never leak into another run over the same data.
    /// </summary>
    public IndicatorSnapshot Clone()
    {
        var copy = (IndicatorSnapshot)MemberwiseClone();
        copy._custom = _custom is { Count: > 0 } ? new Dictionary<string, decimal>(_custom) : null;
        return copy;
    }
}
