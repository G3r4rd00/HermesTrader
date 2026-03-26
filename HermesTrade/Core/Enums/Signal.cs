namespace HermesTrade.Core.Enums;

/// <summary>
/// Trading signal produced by a strategy.
/// </summary>
public enum Signal
{
    /// <summary>Do not take any action.</summary>
    Hold,

    /// <summary>Open a long position (buy).</summary>
    Buy,

    /// <summary>Close the current long position (sell).</summary>
    Sell
}
