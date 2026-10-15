using OsrsCheckPing.Core;

namespace OsrsCheckPing.Core.Tests;

public sealed class TopWorldsTests
{
    private static void Configure(Harness h, int count = 40)
    {
        h.Catalogue.Worlds = Enumerable.Range(301, count)
            .Select(id => new WorldInfo(id, 1, $"world-{id}.runescape.com", "-", 0, 100)).ToArray();
        h.Probe.Handler = (host, _) => Task.FromResult(new ProbeResult(ProbeStatus.Success, Id(host) - 300));
    }

    private static int Id(string host) => int.Parse(host.Split('.')[0].AsSpan("world-".Length));

    [Fact]
    public async Task LatencySelectsMembershipWhileWorldNumberControlsDisplay()
    {
        await using var h = new Harness();
        Configure(h);
        h.Probe.Handler = (host, _) => Task.FromResult(new ProbeResult(ProbeStatus.Success, 341 - Id(host)));
        await h.Engine.StartAsync();
        var ids = Enumerable.Range(321, 20).ToArray();
        Assert.Equal(ids, h.Engine.GetSnapshot().Tracked.Select(w => w.Id));
        Assert.Equal(Enumerable.Range(1, 20).Reverse().Select(n => (long?)n),
            h.Engine.GetSnapshot().Tracked.Select(w => w.LatestResult!.RoundTripMilliseconds));
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 30));
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(ids, h.Engine.GetSnapshot().Tracked.Select(w => w.Id));
    }

    [Fact]
    public async Task FiveFavoritesDoNotConsumeOrdinarySlotsAndCardsRemainDistinct()
    {
        await using var h = new Harness();
        Configure(h);
        h.Store.Settings = new AppSettings { FavoriteWorldIds = [301, 302, 303, 304, 305] };
        await h.Engine.StartAsync();
        var snapshot = h.Engine.GetSnapshot();
        Assert.Equal(25, snapshot.LiveCount);
        Assert.Equal(Enumerable.Range(306, 20), snapshot.Tracked.Select(w => w.Id));
        Assert.Equal(25, snapshot.VisibleWorlds.Select(w => w.Id).Distinct().Count());
        h.Probe.Hosts.Clear();
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(25, h.Probe.Hosts.Distinct().Count());
    }

    [Fact]
    public async Task DiscoveryShowsAndLiveProbesOnlyTwentyFastestWorlds()
    {
        await using var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        var snapshot = h.Engine.GetSnapshot();
        Assert.Equal(40, h.Probe.Hosts.Count);
        Assert.Equal(20, snapshot.LiveCount);
        Assert.Equal(20, snapshot.HiddenCount);
        Assert.Equal(Enumerable.Range(301, 20), snapshot.VisibleWorlds.Select(w => w.Id));
        h.Probe.Hosts.Clear();
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(Enumerable.Range(301, 20), h.Probe.Hosts.Select(Id).Order());
    }

    [Fact]
    public async Task FavoritesIncludingUnavailableOnesHaveSlotsOutsideTwentyWorldLimit()
    {
        await using var h = new Harness();
        Configure(h);
        h.Store.Settings = new AppSettings { FavoriteWorldIds = [337, 338, 339, 340, 999] };
        await h.Engine.StartAsync();
        var snapshot = h.Engine.GetSnapshot();
        Assert.Equal(24, snapshot.LiveCount);
        Assert.Equal(25, snapshot.VisibleWorlds.Count);
        Assert.Equal(5, snapshot.Favorites.Count);
        Assert.Equal(Enumerable.Range(301, 20), snapshot.Tracked.Select(w => w.Id));
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut));
        for (int i = 0; i < 12; i++) await h.Engine.CheckTrackedWorldsAsync();
        Assert.All(h.Engine.GetSnapshot().Favorites.Where(w => w.World is not null), w => Assert.True(w.IsTracked));
        Assert.Null(h.Engine.GetSnapshot().Favorites.Single(w => w.Id == 999).World);
    }

    [Fact]
    public async Task HiddenReviewOnlyProbesHiddenHostsAndPromotesFasterWorlds()
    {
        await using var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        h.Probe.Hosts.Clear();
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 0));
        await h.Engine.RecheckHiddenWorldsAsync();
        Assert.Equal(Enumerable.Range(321, 20), h.Probe.Hosts.Select(Id).Order());
        Assert.Equal(Enumerable.Range(321, 20), h.Engine.GetSnapshot().VisibleWorlds.Select(w => w.Id));
        Assert.Equal(1, h.Catalogue.Calls);
        h.Probe.Hosts.Clear();
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(Enumerable.Range(321, 20), h.Probe.Hosts.Select(Id).Order());
    }

    [Fact]
    public async Task FailedHiddenWorldsCannotDisplaceHealthyLiveWorlds()
    {
        await using var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut));
        await h.Engine.RecheckHiddenWorldsAsync();
        Assert.Equal(Enumerable.Range(301, 20), h.Engine.GetSnapshot().VisibleWorlds.Select(w => w.Id));
        Assert.All(h.Engine.GetSnapshot().Worlds.Where(w => w.Id > 320), w => Assert.False(w.AutomaticallyEligible));
    }

    [Fact]
    public async Task SchedulerLeavesHiddenWorldsAloneUntilFiveMinutesThenUpdatesSelection()
    {
        await using var h = new Harness();
        Configure(h, 22);
        await h.Engine.StartAsync();
        h.Probe.Hosts.Clear();
        var request = await h.Clock.NextDelayAsync();
        for (int i = 0; i < 59; i++)
        {
            h.Clock.Advance(request);
            request = await h.Clock.NextDelayAsync();
        }
        Assert.Equal(295, (h.Clock.UtcNow - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds);
        Assert.All(h.Probe.Hosts, host => Assert.True(Id(host) <= 320));
        h.Probe.Hosts.Clear();
        h.Probe.Handler = (host, _) => Task.FromResult(new ProbeResult(ProbeStatus.Success, Id(host) > 320 ? 0 : Id(host) - 300));
        h.Clock.Advance(request);
        _ = await h.Clock.NextDelayAsync();
        Assert.Equal(22, h.Probe.Hosts.Count);
        Assert.Equal(1, h.Probe.Hosts.Count(host => Id(host) == 321));
        Assert.Equal(h.Clock.UtcNow.AddMinutes(5), h.Engine.GetSnapshot().NextHiddenRecheckAt);
        Assert.True(h.World(321).IsTracked);
        Assert.False(h.World(320).IsTracked);
        Assert.Equal(20, h.Engine.GetSnapshot().LiveCount);
        Assert.Equal(1, h.Catalogue.Calls);
    }

    [Fact]
    public async Task SchedulerHonorsFiveMinuteDeadlineWhenLiveIntervalDoesNotDivideIt()
    {
        await using var h = new Harness();
        Configure(h, 22);
        await h.Engine.StartAsync();
        var deadline = h.Engine.GetSnapshot().NextHiddenRecheckAt!.Value;
        await h.Engine.UpdatePreferencesAsync(60, 7, false, false, false);
        bool shortenedDelay = false;
        var request = await h.Clock.NextDelayAsync();
        while (h.Clock.UtcNow < deadline)
        {
            shortenedDelay |= request.Delay == TimeSpan.FromSeconds(1);
            h.Clock.Advance(request);
            request = await h.Clock.NextDelayAsync();
        }
        Assert.True(shortenedDelay);
        Assert.Equal(deadline, h.Clock.UtcNow);
        Assert.Equal(deadline.AddMinutes(5), h.Engine.GetSnapshot().NextHiddenRecheckAt);
    }

    [Fact]
    public async Task ManualSelectionsAndFavoriteChangesCannotExceedCap()
    {
        await using var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        Assert.True(h.Engine.SetTracking(340, true));
        Assert.True(h.World(340).IsTracked);
        Assert.False(h.World(320).IsTracked);
        Assert.True(await h.Engine.SetFavoriteAsync(339, true));
        Assert.Equal(21, h.Engine.GetSnapshot().LiveCount);
        Assert.True(h.World(339).IsTracked);
        Assert.True(h.Engine.SetTracking(340, false));
        Assert.False(h.World(340).IsTracked);
        for (int id = 301; id < 340; id++)
        {
            h.Engine.SetTracking(id, true);
            Assert.InRange(h.Engine.GetSnapshot().LiveCount, 0, 21);
        }
        Assert.False(h.Engine.SetTracking(340, true));
    }

    [Fact]
    public async Task CancelledHiddenReviewPreservesReadingsMembershipAndDeadline()
    {
        await using var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        var before = h.Engine.GetSnapshot();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Probe.Handler = async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ProbeResult(ProbeStatus.Error);
        };
        using var cancel = new CancellationTokenSource();
        Task review = h.Engine.RecheckHiddenWorldsAsync(cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(h.Engine.GetSnapshot().IsRecheckingHidden);
        Assert.False(await h.Engine.RecheckAsync());
        int calls = h.Probe.Hosts.Count;
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(calls, h.Probe.Hosts.Count);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => review);
        var after = h.Engine.GetSnapshot();
        Assert.False(after.IsScanning);
        Assert.False(after.IsRecheckingHidden);
        Assert.Equal(before.VisibleWorlds, after.VisibleWorlds);
        Assert.Equal(before.NextHiddenRecheckAt, after.NextHiddenRecheckAt);
    }

    [Fact]
    public async Task DisposeCancelsHiddenReviewAndReleasesTheScanGate()
    {
        var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Probe.Handler = async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ProbeResult(ProbeStatus.Error);
        };
        Task review = h.Engine.RecheckHiddenWorldsAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await h.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => review);
        Assert.False(h.Engine.GetSnapshot().IsScanning);
    }

    [Fact]
    public async Task ManualFullScanResetsHiddenReviewDeadline()
    {
        await using var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        h.Clock.AdvanceBy(TimeSpan.FromSeconds(200));
        await h.Engine.RecheckAsync();
        Assert.Equal(h.Clock.UtcNow.AddMinutes(5), h.Engine.GetSnapshot().NextHiddenRecheckAt);
    }

    [Fact]
    public async Task RemovedWorldsStayHiddenUntilReviewAndCanRecover()
    {
        await using var h = new Harness();
        Configure(h);
        await h.Engine.StartAsync();
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut));
        for (int i = 0; i < 12; i++) await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(0, h.Engine.GetSnapshot().LiveCount);
        h.Probe.Hosts.Clear();
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Empty(h.Probe.Hosts);
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 20));
        await h.Engine.RecheckHiddenWorldsAsync();
        Assert.Equal(40, h.Probe.Hosts.Count);
        Assert.Equal(20, h.Engine.GetSnapshot().LiveCount);
    }
}
