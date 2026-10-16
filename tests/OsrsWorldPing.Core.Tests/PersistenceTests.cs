using System.Text.Json;
using OsrsWorldPing.Core;

namespace OsrsWorldPing.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OsrsWorldPing-tests-" + Guid.NewGuid());

    [Fact]
    public async Task BrandingMigrationImportsPreferencesFavoritesPlacementAndCatalogueWithoutRemovingBackup()
    {
        string old = Path.Combine(_directory, "previous"), current = Path.Combine(_directory, "current");
        var previous = new JsonSettingsStore(old);
        var preferences = new AppSettings { LatencyCutoffMs = 60, RefreshIntervalSeconds = 600,
            FavoriteWorldIds = [301, 999], Window = new(50, 60, 1000, 700, true), CloseToTray = true };
        await previous.SaveSettingsAsync(preferences, CancellationToken.None);
        var catalogue = new WorldCatalogue([new(301, 1, "supplied-world.example", "Wintertodt", 0, 100)], DateTimeOffset.UtcNow);
        await previous.SaveCatalogueAsync(catalogue, CancellationToken.None);
        var upgraded = new JsonSettingsStore(current, old);
        var imported = await upgraded.LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(preferences with { FavoriteWorldIds = imported.FavoriteWorldIds }, imported);
        Assert.Equal([301, 999], imported.FavoriteWorldIds);
        Assert.Equal(catalogue.Worlds, (await upgraded.LoadCatalogueAsync(CancellationToken.None))!.Worlds);
        Assert.True(File.Exists(Path.Combine(old, "settings.json")));
        Assert.True(File.Exists(Path.Combine(old, "worlds.json")));
        var restarted = await new JsonSettingsStore(current).LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(60, restarted.LatencyCutoffMs); // A deliberate current-schema 60 is never treated as the old default.
        Assert.Equal([301, 999], restarted.FavoriteWorldIds);
    }

    [Fact]
    public async Task BrandingMigrationNeverReplacesFilesAlreadyInTheNewLocation()
    {
        string old = Path.Combine(_directory, "previous"), current = Path.Combine(_directory, "current");
        await new JsonSettingsStore(old).SaveSettingsAsync(new AppSettings { FavoriteWorldIds = [301] }, CancellationToken.None);
        var fresh = new JsonSettingsStore(current);
        await fresh.SaveSettingsAsync(new AppSettings { FavoriteWorldIds = [302], LatencyCutoffMs = 85 }, CancellationToken.None);
        var catalogue = new WorldCatalogue([new(302, 0, "new.example", "-", 0, 1)], DateTimeOffset.UtcNow);
        await fresh.SaveCatalogueAsync(catalogue, CancellationToken.None);
        var migrated = new JsonSettingsStore(current, old);
        Assert.Equal([302], (await migrated.LoadSettingsAsync(CancellationToken.None)).FavoriteWorldIds);
        Assert.Equal(85, (await migrated.LoadSettingsAsync(CancellationToken.None)).LatencyCutoffMs);
        Assert.Equal("new.example", (await migrated.LoadCatalogueAsync(CancellationToken.None))!.Worlds.Single().Host);
    }

    [Fact]
    public async Task FailedBrandingMigrationStillExposesReadableOldPreferencesAndCache()
    {
        string old = Path.Combine(_directory, "previous"), blocked = Path.Combine(_directory, "blocked");
        var previous = new JsonSettingsStore(old);
        await previous.SaveSettingsAsync(new AppSettings { FavoriteWorldIds = [301] }, CancellationToken.None);
        await previous.SaveCatalogueAsync(new([new(301, 1, "original.example", "-", 0, 1)], DateTimeOffset.UtcNow), CancellationToken.None);
        await File.WriteAllTextAsync(blocked, "A file prevents creating the new directory.");
        var store = new JsonSettingsStore(blocked, old);
        var error = await Assert.ThrowsAsync<SettingsMigrationSaveException>(() => store.LoadSettingsAsync(CancellationToken.None));
        Assert.Equal([301], error.Settings.FavoriteWorldIds);
        Assert.Equal("original.example", (await store.LoadCatalogueAsync(CancellationToken.None))!.Worlds.Single().Host);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(600, 600)]
    [InlineData(601, 600)]
    public async Task SavedRefreshIntervalUsesNewRangeAndSurvivesRestart(int input, int expected)
    {
        var store = new JsonSettingsStore(_directory);
        await store.SaveSettingsAsync(new AppSettings { RefreshIntervalSeconds = input }, CancellationToken.None);
        Assert.Equal(expected, (await new JsonSettingsStore(_directory).LoadSettingsAsync(CancellationToken.None)).RefreshIntervalSeconds);
    }

    [Fact]
    public async Task MissingSettingsUseDefaultsAndSettingsRoundTripWithoutRuntimeState()
    {
        var store = new JsonSettingsStore(_directory);
        var defaults = await store.LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(30, defaults.LatencyCutoffMs);
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
        Assert.Equal(600, settings.RefreshIntervalSeconds);
        Assert.Equal([301, 302, 303, 304, 305], settings.FavoriteWorldIds);
    }

    [Fact]
    public async Task MalformedFilesReportErrorsInsteadOfSilentlyOverwriting()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "broken json");
        var store = new JsonSettingsStore(_directory);
        await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadSettingsAsync(CancellationToken.None));
        await File.WriteAllTextAsync(Path.Combine(_directory, "worlds.json"), """{"Worlds":[],"FetchedAt":"2026-01-01T00:00:00Z"}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadCatalogueAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(60, 30)]
    [InlineData(85, 85)]
    [InlineData(30, 30)]
    public async Task LegacyCutoffMigratesOnceAndLaterSixtyIsRespected(int oldCutoff, int expected)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"),
            $$"""{"LatencyCutoffMs":{{oldCutoff}},"FavoriteWorldIds":[301],"AlwaysOnTop":true}""");
        var store = new JsonSettingsStore(_directory);
        var migrated = await store.LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(expected, migrated.LatencyCutoffMs);
        Assert.Equal(AppSettings.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.True(migrated.AlwaysOnTop);
        Assert.Equal([301], migrated.FavoriteWorldIds);
        var restart = await new JsonSettingsStore(_directory).LoadSettingsAsync(CancellationToken.None);
        Assert.Equal(expected, restart.LatencyCutoffMs);
        await store.SaveSettingsAsync(migrated with { LatencyCutoffMs = 60 }, CancellationToken.None);
        Assert.Equal(60, (await new JsonSettingsStore(_directory).LoadSettingsAsync(CancellationToken.None)).LatencyCutoffMs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
