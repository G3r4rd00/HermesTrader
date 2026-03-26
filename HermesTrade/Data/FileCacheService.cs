using System.Text.Json;
using HermesTrade.Core.Models;

namespace HermesTrade.Data;

/// <summary>
/// Caches market data as JSON files on the local file system.
/// Each unique combination of provider, symbol, and date range maps to a single cache file,
/// preventing unnecessary API calls during repeated backtest runs.
/// </summary>
public sealed class FileCacheService
{
    private readonly string _cacheDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    /// <param name="cacheDirectory">
    /// Directory where cache files will be stored.
    /// Defaults to a <c>hermes_cache</c> subdirectory inside the system's temp folder.
    /// </param>
    public FileCacheService(string? cacheDirectory = null)
    {
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(Path.GetTempPath(), "hermes_cache")
            : cacheDirectory;

        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Attempts to load cached candles.  Returns <c>null</c> when no cache entry exists.
    /// </summary>
    public async Task<IEnumerable<Candle>?> TryLoadAsync(
        string   provider,
        string   symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(provider, symbol, from, to);
        if (!File.Exists(filePath))
            return null;

        await using var stream = File.OpenRead(filePath);
        var records = await JsonSerializer
            .DeserializeAsync<CandleRecord[]>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return records?.Select(r => r.ToCandle());
    }

    /// <summary>Persists a candle sequence to disk for the given cache key.</summary>
    public async Task SaveAsync(
        string               provider,
        string               symbol,
        DateTime             from,
        DateTime             to,
        IEnumerable<Candle>  candles,
        CancellationToken    cancellationToken = default)
    {
        var records  = candles.Select(CandleRecord.FromCandle).ToArray();
        var filePath = GetFilePath(provider, symbol, from, to);

        await using var stream = File.Create(filePath);
        await JsonSerializer
            .SerializeAsync(stream, records, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the cache entry for a given key, if it exists.</summary>
    public void Invalidate(string provider, string symbol, DateTime from, DateTime to)
    {
        var filePath = GetFilePath(provider, symbol, from, to);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetFilePath(string provider, string symbol, DateTime from, DateTime to)
    {
        var key      = $"{provider}_{symbol}_{from:yyyyMMdd}_{to:yyyyMMdd}".ToUpperInvariant();
        var fileName = $"{key}.json";
        return Path.Combine(_cacheDirectory, fileName);
    }

    // ── DTO used only for JSON serialisation ──────────────────────────────────

    private sealed record CandleRecord(
        long    Timestamp,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume)
    {
        public Candle ToCandle() =>
            new(DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).UtcDateTime,
                Open, High, Low, Close, Volume);

        public static CandleRecord FromCandle(Candle c) =>
            new(new DateTimeOffset(c.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                c.Open, c.High, c.Low, c.Close, c.Volume);
    }
}
