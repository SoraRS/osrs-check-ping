using OsrsWorldPing.Core;

namespace OsrsWorldPing.Core.Tests;

public sealed class FailureHandlingTests
{
    [Fact]
    public async Task MigrationWriteFailurePreservesReadableFavoritesAndCustomPreferences()
    {
        await using var h = new Harness();
        h.Store.SettingsReadError = new SettingsMigrationSaveException(new AppSettings
        { FavoriteWorldIds = [301], RefreshIntervalSeconds = 7, LatencyCutoffMs = 30 }, new IOException("Read-only folder"));
        await h.Engine.StartAsync();
        Assert.Equal([301], h.Engine.GetSnapshot().Settings.FavoriteWorldIds);
        Assert.Equal(7, h.Engine.GetSnapshot().Settings.RefreshIntervalSeconds);
        Assert.True(h.World().IsFavorite);
        Assert.Contains("Upgraded settings could not be saved", h.Engine.GetSnapshot().Notice);
    }

    [Fact]
    public async Task SettingsReadFailureNoticeSurvivesSuccessfulDiscovery()
    {
        await using var h = new Harness();
        h.Store.SettingsReadError = new IOException("Unreadable settings");
        await h.Engine.StartAsync();
        Assert.True(h.Engine.GetSnapshot().SettingsLoaded);
        Assert.Equal(4, h.Engine.GetSnapshot().Tracked.Count);
        Assert.Contains("Using defaults", h.Engine.GetSnapshot().Notice);
    }

    [Fact]
    public async Task DownloadFailureUsesCacheAndDisplaysNotice()
    {
        await using var h = new Harness();
        h.Store.Catalogue = new WorldCatalogue(h.Catalogue.Worlds, h.Clock.UtcNow.AddDays(-1));
        h.Catalogue.Error = new HttpRequestException("Offline");
        await h.Engine.StartAsync();
        Assert.Equal(4, h.Engine.GetSnapshot().Tracked.Count);
        Assert.Contains("saved world catalogue", h.Engine.GetSnapshot().Notice);
        Assert.Equal(h.Store.Catalogue.FetchedAt, h.Engine.GetSnapshot().CatalogueFetchedAt);
    }

    [Fact]
    public async Task ColdOfflineStartupKeepsFavoritesVisibleAndManualRetryRecovers()
    {
        await using var h = new Harness();
        h.Store.Settings = new AppSettings { FavoriteWorldIds = [301] };
        h.Catalogue.Error = new HttpRequestException("Offline");
        await h.Engine.StartAsync();
        Assert.True(h.Engine.GetSnapshot().SettingsLoaded);
        Assert.Null(h.World().World);
        Assert.Empty(h.Probe.Hosts);
        Assert.Contains("retry", h.Engine.GetSnapshot().Notice);
        h.Catalogue.Error = null;
        await h.Engine.RecheckAsync();
        Assert.Null(h.Engine.GetSnapshot().Notice);
        Assert.True(h.World().IsTracked);
        Assert.NotNull(h.Store.Catalogue);
    }

    [Fact]
    public async Task SettingsWriteFailureKeepsInMemoryFavoriteAndExplainsPersistenceFailure()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        h.Store.FailWrites = true;
        Assert.True(await h.Engine.SetFavoriteAsync(301, true));
        Assert.True(h.World().IsFavorite);
        Assert.Contains("could not be saved", h.Engine.GetSnapshot().Notice);
        Assert.Empty(h.Store.Settings.FavoriteWorldIds);
    }

    [Fact]
    public async Task UnexpectedProbeErrorDoesNotAbortOtherWorlds()
    {
        await using var h = new Harness();
        h.Probe.Handler = (host, _) => host == "supplied-301a.runescape.com"
            ? Task.FromException<ProbeResult>(new IOException("Probe failed"))
            : Task.FromResult(new ProbeResult(ProbeStatus.Success, 20));
        await h.Engine.StartAsync();
        Assert.Equal(ProbeStatus.Error, h.World().LatestResult!.Status);
        Assert.Equal(3, h.Engine.GetSnapshot().Tracked.Count);
        Assert.False(h.Engine.GetSnapshot().IsScanning);
    }
}
