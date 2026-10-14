using System.Text.Json;
using OsrsCheckPing.Core;

namespace OsrsCheckPing.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OsrsCheckPing-tests-" + Guid.NewGuid());

    [Fact]
    public async Task MissingSettingsUseDefaultsAndSettingsRoundTripWithoutRuntimeState()
    {
        var store = new JsonSettingsStore(_directory);
        var defaults = await store.LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(60, defaults.LatencyCutoffMs);
        Assert.Equal(5, defaults.RefreshIntervalSeconds);
        Assert.Null(await store.LoadCatalogueAsync(CancellationToken.None));
        var settings = new AppSettings
        {
            LatencyCutoffMs = 80, RefreshIntervalSeconds = 10, FavoriteWorldIds = [301, 552],
            AlwaysOnTop = true, CloseToTray = true, MinimizeToTray = true,
            Window = new WindowPlacement(-800, 0, 1000, 600, true)
        };
        await store.SaveSettingsAsync(settings, CancellationToken.None);
        var loaded = await new JsonSettingsStore(_directory).LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(settings with { FavoriteWorldIds = loaded.FavoriteWorldIds }, loaded);
        Assert.Equal(settings.FavoriteWorldIds, loaded.FavoriteWorldIds);
        string json = await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json"));
        Assert.DoesNotContain("ManualTracking", json);
        Assert.DoesNotContain("RoundTrip", json);
    }

    [Fact]
    public async Task SavedCatalogueRoundTripsSuppliedMetadata()
    {
        var store = new JsonSettingsStore(_directory);
        var catalogue = new WorldCatalogue([new WorldInfo(552, 1, "oldschool252a.runescape.com", "PvP", 7, -1)],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await store.SaveCatalogueAsync(catalogue, CancellationToken.None);
        var loaded = await store.LoadCatalogueAsync(CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(catalogue.FetchedAt, loaded.FetchedAt);
        Assert.Equal(catalogue.Worlds, loaded.Worlds);
    }

    [Fact]
    public async Task ConcurrentWritesRemainValidAndCancellationPreservesLastSettings()
    {
        var store = new JsonSettingsStore(_directory);
        await Task.WhenAll(Enumerable.Range(1, 12).Select(value => store.SaveSettingsAsync(
            new AppSettings { LatencyCutoffMs = value }, CancellationToken.None)));
        var before = await store.LoadSettingsAsync(CancellationToken.None);
        Assert.InRange(before.LatencyCutoffMs, 1, 12);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveSettingsAsync(new AppSettings(), cancel.Token));
        Assert.Equal(before.LatencyCutoffMs, (await store.LoadSettingsAsync(CancellationToken.None)).LatencyCutoffMs);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task InvalidSettingsAreBoundedAndFavoriteIdsAreDeduplicated()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"),
            """{"LatencyCutoffMs":0,"RefreshIntervalSeconds":999,"FavoriteWorldIds":[301,301,-2,302,303,304,305,306]}""");
        var settings = await new JsonSettingsStore(_directory).LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(1, settings.LatencyCutoffMs);
        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal([301, 302, 303, 304, 305], settings.FavoriteWorldIds);
    }

    [Fact]
    public async Task MalformedFilesReportErrorsInsteadOfSilentlyOverwriting()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "broken json");
        var store = new JsonSettingsStore(_directory);
        await Assert.ThrowsAsync<JsonException>(() => store.LoadSettingsAsync(CancellationToken.None));
        await File.WriteAllTextAsync(Path.Combine(_directory, "worlds.json"), """{"Worlds":[],"FetchedAt":"2026-01-01T00:00:00Z"}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadCatalogueAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
