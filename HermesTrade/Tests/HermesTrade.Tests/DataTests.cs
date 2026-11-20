using System.Collections.Concurrent;
using System.Globalization;
using HermesTrade.Core.Models;
using HermesTrade.Data;
using static HermesTrade.Tests.TestData;

namespace HermesTrade.Tests;

public sealed class FileCacheServiceTests : IDisposable
{
    private static readonly DateTime From = new(2020, 1, 1);
    private static readonly DateTime To   = new(2021, 5, 1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid().ToString("N"));

    public FileCacheServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Round_trips_candles_through_disk()
    {
        var candles = Walk(50);
        await new FileCacheService(_dir).SaveAsync("p", "sym", From, To, candles);

        var loaded = (await new FileCacheService(_dir).TryLoadAsync("p", "sym", From, To))!.ToList();

        Assert.Equal(candles.Select(c => (c.Timestamp, c.Close)), loaded.Select(c => (c.Timestamp, c.Close)));
    }

    [Fact]
    public async Task Corrupt_file_is_a_miss_and_is_deleted()
    {
        var path = Path.Combine(_dir, "P_SYM_20200101_20210501.json");
        File.WriteAllText(path, "");

        var loaded = await new FileCacheService(_dir).TryLoadAsync("p", "sym", From, To);

        Assert.Null(loaded);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Cancelled_save_leaves_no_file_behind()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FileCacheService(_dir).SaveAsync("c", "sym", From, To, Walk(10_000), cts.Token));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Concurrent_saves_and_loads_of_one_key_do_not_fail()
    {
        var candles = Walk(500);
        var errors  = new ConcurrentBag<Exception>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 64), async (k, ct) =>
        {
            try
            {
                var cache = new FileCacheService(_dir); // separate instances share the directory
                if (k % 2 == 0)
                {
                    await cache.SaveAsync("conc", "sym", From, To, candles, ct);
                }
                else
                {
                    var loaded = await cache.TryLoadAsync("conc", "sym", From, To, ct);
                    if (loaded is not null && loaded.Count() != candles.Count)
                        throw new InvalidOperationException("partial read");
                }
            }
            catch (Exception e) { errors.Add(e); }
        });

        Assert.Empty(errors);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task Local_timestamps_can_be_saved()
    {
        var local = new[] { new Candle(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Local), 1, 1, 1, 1, 1) };
        await new FileCacheService(_dir).SaveAsync("local", "x", From, To, local);

        var loaded = (await new FileCacheService(_dir).TryLoadAsync("local", "x", From, To))!.Single();

        Assert.Equal(local[0].Timestamp.Ticks, loaded.Timestamp.Ticks);
    }

    [Fact]
    public async Task Cache_key_does_not_depend_on_the_culture()
    {
        CultureInfo culture;
        try { culture = new CultureInfo("ar-SA"); }
        catch (CultureNotFoundException) { return; } // invariant-globalization environments

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            await new FileCacheService(_dir).SaveAsync("cult", "x", new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), Walk(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.True(File.Exists(Path.Combine(_dir, "CULT_X_20240101_20241231.json")));
    }

    [Fact]
    public async Task Memory_cache_keeps_its_own_copy()
    {
        var candles = Walk(10);
        var cache   = new FileCacheService(_dir);
        await cache.SaveAsync("mem", "x", From, To, candles);

        candles.Clear();
        var loaded = await cache.TryLoadAsync("mem", "x", From, To);

        var list = Assert.IsAssignableFrom<IList<Candle>>(loaded);
        Assert.Equal(10, list.Count);
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }
}

public class YahooSymbolTests
{
    [Theory]
    [InlineData("BTCUSDT", "BTC-USD")]
    [InlineData("btcusdt", "BTC-USD")]
    [InlineData("BTC-USDT", "BTC-USD")]
    [InlineData("BTC/USDT", "BTC-USD")]
    [InlineData("ETHUSDT", "ETH-USD")]
    [InlineData(" btc ", "BTC-USD")]
    [InlineData("ETH", "ETH-USD")]
    [InlineData("BTC-USD", "BTC-USD")]
    [InlineData("USDT-USD", "USDT-USD")]
    [InlineData("USDT", "USDT")]
    [InlineData("USDTRY=X", "USDTRY=X")]
    [InlineData("FBTC", "FBTC")]
    [InlineData("GBTC", "GBTC")]
    [InlineData("ETHE", "ETHE")]
    [InlineData("AAPL", "AAPL")]
    [InlineData("^GSPC", "^GSPC")]
    [InlineData("SAN.MC", "SAN.MC")]
    public void Only_known_crypto_formats_are_rewritten(string input, string expected)
    {
        Assert.Equal(expected, YahooFinanceDataProvider.NormalizeSymbol(input));
    }

    [Theory]
    [InlineData("EURUSD=X")]
    [InlineData("eur=x")]
    public async Task Forex_is_reported_as_not_supported(string symbol)
    {
        var provider = new YahooFinanceDataProvider(new FileCacheService(Path.Combine(Path.GetTempPath(), "hermes-tests-fx")));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.GetHistoricalDataAsync(symbol, new DateTime(2023, 1, 1), new DateTime(2023, 12, 31)));
    }
}
