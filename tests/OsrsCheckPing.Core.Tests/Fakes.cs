using System.Collections.Concurrent;
using System.Threading.Channels;
using OsrsCheckPing.Core;

namespace OsrsCheckPing.Core.Tests;

internal sealed class FakeCatalogue : IWorldCatalogueClient
{
    public IReadOnlyList<WorldInfo> Worlds { get; set; } = Enumerable.Range(301, 4)
        .Select(id => new WorldInfo(id, 1, $"supplied-{id}a.runescape.com", "Test activity", 0, 100)).ToArray();
    public Exception? Error { get; set; }
    public int Calls { get; private set; }
    public Task<IReadOnlyList<WorldInfo>> FetchAsync(CancellationToken cancellationToken)
    {
        Calls++;
        cancellationToken.ThrowIfCancellationRequested();
        return Error is null ? Task.FromResult(Worlds) : Task.FromException<IReadOnlyList<WorldInfo>>(Error);
    }
}

internal sealed class FakeProbe : ILatencyProbe
{
    public ConcurrentQueue<string> Hosts { get; } = [];
    public Func<string, CancellationToken, Task<ProbeResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(new ProbeResult(ProbeStatus.Success, 40));
    public Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        Hosts.Enqueue(host);
        return Handler(host, cancellationToken);
    }
    public void Return(ProbeResult result) => Handler = (_, _) => Task.FromResult(result);
}

internal sealed class MemoryStore : ISettingsStore
{
    public AppSettings Settings { get; set; } = new();
    public WorldCatalogue? Catalogue { get; set; }
    public Exception? SettingsReadError { get; set; }
    public bool FailWrites { get; set; }
    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken) => SettingsReadError is null
        ? Task.FromResult(Settings) : Task.FromException<AppSettings>(SettingsReadError);
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWrites) throw new IOException("Test disk error");
        Settings = settings;
        return Task.CompletedTask;
    }
    public Task<WorldCatalogue?> LoadCatalogueAsync(CancellationToken cancellationToken) => Task.FromResult(Catalogue);
    public Task SaveCatalogueAsync(WorldCatalogue catalogue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWrites) throw new IOException("Test disk error");
        Catalogue = catalogue;
        return Task.CompletedTask;
    }
}

internal sealed class ManualClock : IClock
{
    public sealed record DelayRequest(TimeSpan Delay, TaskCompletionSource Completion);
    private readonly Channel<DelayRequest> _delays = Channel.CreateUnbounded<DelayRequest>();
    private long _ticks;
    public DateTimeOffset UtcNow => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(Interlocked.Read(ref _ticks));
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _delays.Writer.TryWrite(new DelayRequest(delay, completion));
        return completion.Task.WaitAsync(cancellationToken);
    }
    public async Task<DelayRequest> NextDelayAsync() =>
        await _delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public void Advance(DelayRequest request)
    {
        Interlocked.Add(ref _ticks, request.Delay.Ticks);
        request.Completion.TrySetResult();
    }
}

internal sealed class Harness : IAsyncDisposable
{
    public FakeCatalogue Catalogue { get; } = new();
    public FakeProbe Probe { get; } = new();
    public MemoryStore Store { get; } = new();
    public ManualClock Clock { get; } = new();
    public MonitoringEngine Engine { get; }
    public Harness(int concurrency = 3) => Engine = new MonitoringEngine(Catalogue, Probe, Store, Clock, concurrency);
    public WorldSnapshot World(int id = 301) => Engine.GetSnapshot().Worlds.Single(w => w.Id == id);
    public ValueTask DisposeAsync() => Engine.DisposeAsync();
}
