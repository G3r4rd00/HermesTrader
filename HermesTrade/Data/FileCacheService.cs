using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, IReadOnlyList<Candle>> _memoryCache = new();

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
    /// Hits memory first; falls back to disk and populates memory on a cache miss.
    /// </summary>
    public async Task<IEnumerable<Candle>?> TryLoadAsync(
        string   provider,
        string   symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(provider, symbol, from, to);

        if (_memoryCache.TryGetValue(key, out var cached))
            return cached;

        var filePath = Path.Combine(_cacheDirectory, $"{key}.json");
        if (!File.Exists(filePath))
            return null;

        await using var stream = File.OpenRead(filePath);
        var records = await JsonSerializer
            .DeserializeAsync<CandleRecord[]>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (records is null)
            return null;

        var candles = records.Select(r => r.ToCandle()).ToArray();
        _memoryCache[key] = candles;
        return candles;
    }

    /// <summary>Persists a candle sequence to disk and memory for the given cache key.</summary>
    public async Task SaveAsync(
        string               provider,
        string               symbol,
        DateTime             from,
        DateTime             to,
        IEnumerable<Candle>  candles,
        CancellationToken    cancellationToken = default)
    {
        var key      = GetKey(provider, symbol, from, to);
        var snapshot = candles as IReadOnlyList<Candle> ?? candles.ToArray();
        var records  = snapshot.Select(CandleRecord.FromCandle).ToArray();
        var filePath = Path.Combine(_cacheDirectory, $"{key}.json");

        await using var stream = File.Create(filePath);
        await JsonSerializer
            .SerializeAsync(stream, records, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        _memoryCache[key] = snapshot;
    }

    /// <summary>Removes the cache entry from memory and disk, if it exists.</summary>
    public void Invalidate(string provider, string symbol, DateTime from, DateTime to)
    {
        var key      = GetKey(provider, symbol, from, to);
        var filePath = Path.Combine(_cacheDirectory, $"{key}.json");

        _memoryCache.TryRemove(key, out _);

        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetKey(string provider, string symbol, DateTime from, DateTime to) =>
        $"{provider}_{symbol}_{from:yyyyMMdd}_{to:yyyyMMdd}".ToUpperInvariant();

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
