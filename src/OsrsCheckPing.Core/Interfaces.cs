namespace OsrsCheckPing.Core;

public interface IWorldCatalogueClient
{
    Task<IReadOnlyList<WorldInfo>> FetchAsync(CancellationToken cancellationToken);
}

public interface ILatencyProbe
{
    Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellationToken);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken);
    Task<WorldCatalogue?> LoadCatalogueAsync(CancellationToken cancellationToken);
    Task SaveCatalogueAsync(WorldCatalogue catalogue, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
