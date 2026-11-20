using System.Collections.Concurrent;
using System.Globalization;
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

    // Per-file locks shared by all instances (several may point at the same directory).
    // On Windows a file cannot be replaced while it is open for reading, nor by two
    // File.Move calls at once, so disk reads and replacements of one file are serialised.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

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
    /// Attempts to load cached candles.  Returns <c>null</c> when no usable cache entry
    /// exists (missing, locked or corrupt file; a corrupt file is deleted).
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

        CandleRecord[]? records;
        var fileLock = LockFor(filePath);
        await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            records = await JsonSerializer
                .DeserializeAsync<CandleRecord[]>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // missing or locked (e.g. by another process): treat as a cache miss
        }
        catch (JsonException)
        {
            TryDelete(filePath); // empty or truncated file (e.g. an interrupted save)
            return null;
        }
        finally
        {
            fileLock.Release();
        }

        if (records is null)
            return null;

        // Read-only, so callers cannot change what later cache hits return
        var candles = Array.AsReadOnly(records.Select(r => r.ToCandle()).ToArray());
        _memoryCache[key] = candles;
        return candles;
    }

    /// <summary>
    /// Persists a candle sequence to disk and memory for the given cache key.
    /// A private copy is stored, so later changes to <paramref name="candles"/> do not
    /// affect the cache. The file is written to a temporary path and then moved into
    /// place, so an interrupted save never leaves a partial cache file behind.
    /// </summary>
    public async Task SaveAsync(
        string               provider,
        string               symbol,
        DateTime             from,
        DateTime             to,
        IEnumerable<Candle>  candles,
        CancellationToken    cancellationToken = default)
    {
        var key      = GetKey(provider, symbol, from, to);
        var snapshot = Array.AsReadOnly(candles.ToArray());
        var records  = snapshot.Select(CandleRecord.FromCandle).ToArray();
        var filePath = Path.Combine(_cacheDirectory, $"{key}.json");
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        _memoryCache[key] = snapshot;

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer
                    .SerializeAsync(stream, records, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            var fileLock = LockFor(filePath);
            await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await MoveWithRetryAsync(tempPath, filePath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                fileLock.Release();
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
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
        string.Create(CultureInfo.InvariantCulture, $"{provider}_{symbol}_{from:yyyyMMdd}_{to:yyyyMMdd}")
            .ToUpperInvariant();

    // Another process may have the destination open or be replacing it: retry briefly
    private static async Task MoveWithRetryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 5 && (ex is IOException or UnauthorizedAccessException))
            {
                await Task.Delay(25 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static SemaphoreSlim LockFor(string filePath) =>
        FileLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new SemaphoreSlim(1, 1));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
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

        // Stores the raw ticks as UTC whatever the Kind, so it never throws for Local
        // timestamps and loaded candles keep the same ticks as fresh ones.
        public static CandleRecord FromCandle(Candle c) =>
            new(new DateTimeOffset(c.Timestamp.Ticks, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                c.Open, c.High, c.Low, c.Close, c.Volume);
    }
}
