using OsrsWorldPing.Core;

namespace OsrsWorldPing.Core.Tests;

public sealed class MonitoringTests
{
    [Theory]
    [InlineData(5, 12)]
    [InlineData(7, 9)]
    [InlineData(60, 1)]
    [InlineData(600, 1)]
    public async Task RefreshIntervalBoundariesAreAcceptedAndDetermineRemovalStreak(int interval, int requiredReadings)
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        await h.Engine.UpdatePreferencesAsync(30, interval, false, false, false);
        Assert.Equal(interval, h.Engine.GetSnapshot().Settings.RefreshIntervalSeconds);
        Assert.Equal(requiredReadings, h.Engine.GetSnapshot().Settings.RequiredBadReadings);
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut));
        for (int i = 1; i < requiredReadings; i++) await h.Engine.CheckTrackedWorldsAsync();
        Assert.True(h.World().IsTracked);
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.False(h.World().IsTracked);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(601)]
    public async Task InvalidRefreshIntervalsLeaveExistingPreferencesIntact(int interval)
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Engine.UpdatePreferencesAsync(40, interval, true, true, true));
        Assert.Equal(30, h.Engine.GetSnapshot().Settings.LatencyCutoffMs);
        Assert.Equal(5, h.Engine.GetSnapshot().Settings.RefreshIntervalSeconds);
        Assert.False(h.Store.Settings.AlwaysOnTop);
    }

    [Fact]
    public async Task DiscoveryChecksEachSuppliedHostOnceAndIncludesCutoffBoundary()
    {
        await using var h = new Harness();
        h.Probe.Handler = (host, _) => Task.FromResult(host switch
        {
            "supplied-301a.runescape.com" => new ProbeResult(ProbeStatus.Success, 30),
            "supplied-302a.runescape.com" => new ProbeResult(ProbeStatus.Success, 31),
            "supplied-303a.runescape.com" => new ProbeResult(ProbeStatus.Success, 0),
            _ => new ProbeResult(ProbeStatus.TimedOut)
        });
        await h.Engine.StartAsync();
        Assert.Equal(h.Catalogue.Worlds.Select(w => w.Host).Order(), h.Probe.Hosts.Order());
        Assert.Equal([301, 303], h.Engine.GetSnapshot().Tracked.Select(w => w.Id));
        Assert.All(h.Engine.GetSnapshot().Worlds, w => Assert.True(w.IsScanResult));
        Assert.All(h.Engine.GetSnapshot().Worlds, w => Assert.Equal(0, w.ConsecutiveBadReadings));
    }

    [Fact]
    public async Task TwelfthBadLiveReadingRemovesWorldAndStopsFurtherProbes()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 31));
        for (int i = 0; i < 11; i++) await h.Engine.CheckTrackedWorldsAsync();
        Assert.True(h.World().IsTracked);
        Assert.Equal(11, h.World().ConsecutiveBadReadings);
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.False(h.World().IsTracked);
        Assert.True(h.World().Removed);
        int calls = h.Probe.Hosts.Count;
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(calls, h.Probe.Hosts.Count);
        Assert.True(h.Engine.SetTracking(301, true));
        Assert.True(h.World().IsTracked);
        Assert.Equal(0, h.World().ConsecutiveBadReadings);
    }

    [Fact]
    public async Task FailuresCountAndGoodBoundaryReadingResetsStreak()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        foreach (var status in new[] { ProbeStatus.TimedOut, ProbeStatus.Unreachable, ProbeStatus.DnsError, ProbeStatus.Error })
        {
            h.Probe.Return(new ProbeResult(status));
            await h.Engine.CheckTrackedWorldsAsync();
        }
        Assert.Equal(4, h.World().ConsecutiveBadReadings);
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 30));
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(0, h.World().ConsecutiveBadReadings);
        Assert.False(h.World().IsScanResult);
    }

    [Fact]
    public async Task MonitoringChangesResetStreaksAndAdjustRequiredReadingCount()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut));
        await h.Engine.CheckTrackedWorldsAsync();
        await h.Engine.UpdatePreferencesAsync(60, 7, true, false, false);
        Assert.Equal(0, h.World().ConsecutiveBadReadings);
        Assert.Equal(9, h.Engine.GetSnapshot().Settings.RequiredBadReadings);
        for (int i = 0; i < 8; i++) await h.Engine.CheckTrackedWorldsAsync();
        Assert.True(h.World().IsTracked);
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.False(h.World().IsTracked);
    }

    [Fact]
    public async Task ManuallyEnabledSlowWorldIsStillSubjectToRemoval()
    {
        await using var h = new Harness();
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 300));
        await h.Engine.StartAsync();
        Assert.False(h.World().IsTracked);
        h.Engine.SetTracking(301, true);
        for (int i = 0; i < 12; i++) await h.Engine.CheckTrackedWorldsAsync();
        Assert.False(h.World().IsTracked);
        Assert.True(h.World().ManualTracking);
    }

    [Fact]
    public async Task SessionOverridesSurviveRescanButNotRestart()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        h.Engine.SetTracking(301, false);
        h.Engine.SetTracking(302, true);
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 300));
        await h.Engine.RecheckAsync();
        Assert.False(h.World().IsTracked);
        Assert.True(h.World(302).IsTracked);
        Assert.Empty(h.Store.Settings.FavoriteWorldIds);

        await using var restarted = new MonitoringEngine(h.Catalogue, h.Probe, h.Store, new ManualClock());
        await restarted.StartAsync();
        Assert.All(restarted.GetSnapshot().Worlds, w => Assert.Null(w.ManualTracking));
        Assert.Empty(restarted.GetSnapshot().Tracked);
    }

    [Fact]
    public async Task FavoritesPersistStayTrackedAndRestoreUnderlyingChoiceWhenUnpinned()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        h.Engine.SetTracking(301, false);
        Assert.True(await h.Engine.SetFavoriteAsync(301, true));
        Assert.False(h.Engine.SetTracking(301, false));
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut));
        for (int i = 0; i < 15; i++) await h.Engine.CheckTrackedWorldsAsync();
        Assert.True(h.World().IsTracked);
        Assert.Equal(0, h.World().ConsecutiveBadReadings);
        Assert.Contains(301, h.Store.Settings.FavoriteWorldIds);
        await using (var restarted = new MonitoringEngine(h.Catalogue, h.Probe, h.Store, new ManualClock()))
        {
            await restarted.StartAsync();
            Assert.True(restarted.GetSnapshot().Favorites.Single().IsTracked);
        }
        Assert.True(await h.Engine.SetFavoriteAsync(301, false));
        Assert.False(h.World().IsTracked);
        Assert.False(h.World().ManualTracking);
    }

    [Fact]
    public async Task SixthFavoriteIsRejectedAndMissingFavoriteStaysVisibleWithoutProbe()
    {
        await using var h = new Harness();
        h.Catalogue.Worlds = Enumerable.Range(301, 7)
            .Select(id => new WorldInfo(id, 0, $"world-{id}.runescape.com", "-", 0, 5)).ToArray();
        await h.Engine.StartAsync();
        for (int id = 301; id <= 305; id++) Assert.True(await h.Engine.SetFavoriteAsync(id, true));
        Assert.False(await h.Engine.SetFavoriteAsync(306, true));
        Assert.Equal([301, 302, 303, 304, 305], h.Store.Settings.FavoriteWorldIds);
        h.Catalogue.Worlds = h.Catalogue.Worlds.Where(w => w.Id != 301).ToArray();
        h.Probe.Hosts.Clear();
        await h.Engine.RecheckAsync();
        Assert.True(h.World().IsFavorite);
        Assert.Null(h.World().World);
        Assert.False(h.World().IsTracked);
        Assert.DoesNotContain("world-301.runescape.com", h.Probe.Hosts);
        Assert.True(await h.Engine.SetFavoriteAsync(301, false));
        Assert.DoesNotContain(h.Engine.GetSnapshot().Worlds, w => w.Id == 301);
    }

    [Fact]
    public async Task RemovedWorldRequalifiesOnRescanAndResetRestoresScanSelection()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut));
        for (int i = 0; i < 12; i++) await h.Engine.CheckTrackedWorldsAsync();
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 30));
        await h.Engine.RecheckAsync();
        Assert.True(h.World().IsTracked);
        h.Engine.SetTracking(301, false);
        h.Engine.ResetManualTracking();
        Assert.True(h.World().IsTracked);
        Assert.Null(h.World().ManualTracking);
    }

    [Fact]
    public async Task FavoritesAndOtherWorldsKeepNumericOrderRegardlessOfLatency()
    {
        await using var h = new Harness();
        h.Store.Settings = new AppSettings { FavoriteWorldIds = [301, 302] };
        h.Probe.Handler = (host, _) => Task.FromResult(host switch
        {
            "supplied-301a.runescape.com" => new ProbeResult(ProbeStatus.TimedOut),
            "supplied-302a.runescape.com" => new ProbeResult(ProbeStatus.Success, 300),
            _ => new ProbeResult(ProbeStatus.Success, 20)
        });
        await h.Engine.StartAsync();
        Assert.Equal([301, 302], h.Engine.GetSnapshot().Favorites.Select(w => w.Id));
        Assert.Equal([303, 304], h.Engine.GetSnapshot().Tracked.Select(w => w.Id));
    }
}
