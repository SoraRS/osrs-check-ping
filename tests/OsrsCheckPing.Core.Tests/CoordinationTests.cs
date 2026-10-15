using System.Collections.Concurrent;
using OsrsCheckPing.Core;

namespace OsrsCheckPing.Core.Tests;

public sealed class CoordinationTests
{
    [Fact]
    public async Task DiscoveryIsBoundedAndRejectsConcurrentScansAndLiveCycles()
    {
        await using var h = new Harness(concurrency: 2);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        var activeCounts = new ConcurrentQueue<int>();
        h.Probe.Handler = async (_, ct) =>
        {
            int current = Interlocked.Increment(ref active);
            activeCounts.Enqueue(current);
            if (current == 2) entered.TrySetResult();
            try { await release.Task.WaitAsync(ct); }
            finally { Interlocked.Decrement(ref active); }
            return new ProbeResult(ProbeStatus.Success, 30);
        };
        Task startup = h.Engine.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(h.Engine.GetSnapshot().IsScanning);
        Assert.False(await h.Engine.RecheckAsync());
        await h.Engine.CheckTrackedWorldsAsync();
        Assert.Equal(2, h.Probe.Hosts.Count);
        release.TrySetResult();
        await startup;
        Assert.Equal(2, activeCounts.Max());
        Assert.Equal(4, h.Probe.Hosts.Distinct().Count());
        Assert.Equal(4, h.Engine.GetSnapshot().ScanCompleted);
    }

    [Fact]
    public async Task ManualScanWaitsForLiveCycleWithoutOverlappingHosts()
    {
        await using var h = new Harness(concurrency: 2);
        await h.Engine.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = new ConcurrentDictionary<string, int>();
        bool overlap = false;
        h.Probe.Handler = async (host, ct) =>
        {
            if (active.AddOrUpdate(host, 1, (_, count) => count + 1) != 1) overlap = true;
            entered.TrySetResult();
            try { await release.Task.WaitAsync(ct); }
            finally { active.AddOrUpdate(host, 0, (_, count) => count - 1); }
            return new ProbeResult(ProbeStatus.Success, 30);
        };
        Task live = h.Engine.CheckTrackedWorldsAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task scan = h.Engine.RecheckAsync();
        Assert.Equal(1, h.Catalogue.Calls);
        await h.Engine.CheckTrackedWorldsAsync();
        release.TrySetResult();
        await Task.WhenAll(live, scan);
        Assert.False(overlap);
        Assert.Equal(2, h.Catalogue.Calls);
        Assert.All(h.Engine.GetSnapshot().Worlds, w => Assert.True(w.IsScanResult));
    }

    [Fact]
    public async Task CancelledScanPreservesPreviousSelectionAndCanBeRetried()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        h.Engine.SetTracking(301, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Probe.Handler = async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ProbeResult(ProbeStatus.Error);
        };
        using var cancel = new CancellationTokenSource();
        Task scan = h.Engine.RecheckAsync(cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.False(h.Engine.GetSnapshot().IsScanning);
        Assert.False(h.World().IsTracked);
        Assert.Equal(4, h.Engine.GetSnapshot().Worlds.Count);
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 20));
        Assert.True(await h.Engine.RecheckAsync());
    }

    [Fact]
    public async Task DisposeCancelsStartupAndDoesNotLeaveProbesRunning()
    {
        var h = new Harness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        h.Probe.Handler = async (_, ct) =>
        {
            Interlocked.Increment(ref active);
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { Interlocked.Decrement(ref active); }
            return new ProbeResult(ProbeStatus.Error);
        };
        Task startup = h.Engine.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await h.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        Assert.Equal(0, active);
        Assert.False(h.Engine.GetSnapshot().IsScanning);
    }

    [Fact]
    public async Task SettingChangeDuringProbeDiscardsOldPolicyResults()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Probe.Handler = async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new ProbeResult(ProbeStatus.Success, 100);
        };
        Task live = h.Engine.CheckTrackedWorldsAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await h.Engine.UpdatePreferencesAsync(120, 10, false, false, false);
        release.TrySetResult();
        await live;
        Assert.All(h.Engine.GetSnapshot().Worlds, w => Assert.Equal(0, w.ConsecutiveBadReadings));
        Assert.Equal(20, h.World().LatestResult!.RoundTripMilliseconds);
    }

    [Fact]
    public async Task SchedulerUsesClockAndNewIntervalWithoutRealTimeDelays()
    {
        await using var h = new Harness();
        await h.Engine.StartAsync();
        var first = await h.Clock.NextDelayAsync();
        Assert.Equal(TimeSpan.FromSeconds(5), first.Delay);
        h.Probe.Return(new ProbeResult(ProbeStatus.Success, 80));
        h.Clock.Advance(first);
        var second = await h.Clock.NextDelayAsync();
        Assert.Equal(1, h.World().ConsecutiveBadReadings);
        Assert.Equal(h.Clock.UtcNow, h.World().LastChecked);
        await h.Engine.UpdatePreferencesAsync(60, 10, false, false, false);
        h.Clock.Advance(second);
        var third = await h.Clock.NextDelayAsync();
        Assert.Equal(TimeSpan.FromSeconds(10), third.Delay);
        Assert.Equal(1, h.World().ConsecutiveBadReadings);
    }
}
