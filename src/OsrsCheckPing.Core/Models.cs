using System.Text.Json.Serialization;

namespace OsrsCheckPing.Core;

public sealed record WorldInfo(int Id, uint Flags, string Host, string Activity, int Location, int Population)
{
    [JsonIgnore] public bool Members => (Flags & 1) != 0;
    [JsonIgnore] public string Region => Location switch
    {
        0 => "United States",
        1 => "United Kingdom",
        3 => "Australia",
        7 => "Germany",
        _ => $"Unknown region ({Location})"
    };
}

public enum ProbeStatus { Success, TimedOut, Unreachable, DnsError, Error }

public sealed record ProbeResult(ProbeStatus Status, long? RoundTripMilliseconds = null)
{
    public bool IsGood(int cutoff) => Status == ProbeStatus.Success && RoundTripMilliseconds is >= 0 &&
        RoundTripMilliseconds <= cutoff;
}

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

public sealed record AppSettings
{
    public int LatencyCutoffMs { get; init; } = 60;
    public int RefreshIntervalSeconds { get; init; } = 5;
    public bool AlwaysOnTop { get; init; }
    public bool MinimizeToTray { get; init; }
    public bool CloseToTray { get; init; }
    public IReadOnlyList<int> FavoriteWorldIds { get; init; } = Array.Empty<int>();
    public WindowPlacement? Window { get; init; }
    [JsonIgnore] public int RequiredBadReadings => (int)Math.Ceiling(60d / RefreshIntervalSeconds);

    public AppSettings Sanitize() => this with
    {
        LatencyCutoffMs = Math.Clamp(LatencyCutoffMs, 1, 2000),
        RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 1, 60),
        FavoriteWorldIds = (FavoriteWorldIds ?? []).Where(id => id is > 0 and <= ushort.MaxValue)
            .Distinct().Take(5).ToArray()
    };
}

public sealed record WorldCatalogue(IReadOnlyList<WorldInfo> Worlds, DateTimeOffset FetchedAt);

public sealed record WorldSnapshot(
    int Id, WorldInfo? World, bool IsFavorite, bool IsTracked, bool? ManualTracking,
    bool AutomaticallyEligible, bool Removed, ProbeResult? LatestResult,
    DateTimeOffset? LastChecked, bool IsScanResult, int ConsecutiveBadReadings);

public sealed record MonitorSnapshot(
    AppSettings Settings, IReadOnlyList<WorldSnapshot> Worlds, bool SettingsLoaded,
    bool IsScanning, int ScanCompleted, int ScanTotal, string? Notice, DateTimeOffset? CatalogueFetchedAt)
{
    public IReadOnlyList<WorldSnapshot> Favorites => Sort(Worlds.Where(w => w.IsFavorite));
    public IReadOnlyList<WorldSnapshot> Tracked => Sort(Worlds.Where(w => w.IsTracked && !w.IsFavorite));

    private static WorldSnapshot[] Sort(IEnumerable<WorldSnapshot> worlds) => worlds
        .OrderBy(w => w.LatestResult?.Status == ProbeStatus.Success
            ? w.LatestResult.RoundTripMilliseconds ?? long.MaxValue : long.MaxValue)
        .ThenBy(w => w.Id).ToArray();
}
