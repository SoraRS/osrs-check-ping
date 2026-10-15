using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using OsrsCheckPing.Core;

namespace OsrsCheckPing.Desktop;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Notify(name); return true;
    }
}

public sealed class UiCommand(Func<Task> execute) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running;
    public async void Execute(object? parameter)
    {
        if (_running) return;
        _running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}

public sealed class WorldCardModel : ObservableModel
{
    private WorldSnapshot _snapshot;
    private int _cutoff;
    public WorldCardModel(WorldSnapshot snapshot, DashboardViewModel owner)
    {
        _snapshot = snapshot;
        ToggleTracking = new UiCommand(() => { owner.SetTracking(this); return Task.CompletedTask; });
        ToggleFavorite = new UiCommand(() => owner.SetFavoriteAsync(this));
    }
    public int Id => _snapshot.Id;
    public WorldSnapshot Snapshot => _snapshot;
    public WorldMetadata Metadata => WorldMetadata.From(_snapshot.World);
    public string WorldLabel => $"World {Id}";
    public bool IsFavorite => _snapshot.IsFavorite;
    public bool IsTracked => _snapshot.IsTracked;
    public bool CanTrack => !_snapshot.IsFavorite && _snapshot.World is not null;
    public bool IsMembers => _snapshot.World?.Members == true;
    public bool HasWorld => _snapshot.World is not null;
    public string CountryCode => Metadata.CountryCode;
    public string Region => Metadata.Region;
    public string Activity => Metadata.Activity;
    public ActivityIcon ActivityIcon => Metadata.Icon;
    public bool IsDangerous => Metadata.IsDangerous;
    public string? DangerReason => Metadata.DangerReason;
    public string Membership => !HasWorld ? "Membership unavailable" : IsMembers ? "Members world" : "Free-to-play world";
    public string Latency => !HasWorld ? "Unavailable" : _snapshot.LatestResult switch
    {
        { Status: ProbeStatus.Success, RoundTripMilliseconds: >= 0 } result => $"{result.RoundTripMilliseconds} ms",
        { Status: ProbeStatus.TimedOut } => "Timeout",
        { Status: ProbeStatus.DnsError } => "DNS error",
        { Status: ProbeStatus.Unreachable } => "Unreachable",
        { Status: ProbeStatus.Error } => "Probe failed",
        _ => "Waiting"
    };
    public bool IsGood => HasWorld && _snapshot.LatestResult?.IsGood(_cutoff) == true;
    public int Signal => !HasWorld || _snapshot.LatestResult is not { Status: ProbeStatus.Success, RoundTripMilliseconds: >= 0 } ? 0 :
        _snapshot.LatestResult.RoundTripMilliseconds <= _cutoff / 2d ? 5 : IsGood ? 4 :
        _snapshot.LatestResult.RoundTripMilliseconds <= _cutoff * 2 ? 3 : 1;
    public string ReadingLabel => !HasWorld ? "Unavailable" : !_snapshot.IsTracked ? "Scan result" :
        _snapshot.IsScanResult ? "Initial reading" : "Live reading";
    public string ReadingDetails => $"{ReadingLabel} · {(_snapshot.LastChecked is { } at ? at.ToLocalTime().ToString("g") : "Not checked yet")}";
    public string AccessibleDescription => $"{WorldLabel}, {Region}, {Membership}, {Activity}, {Latency}" +
        (IsDangerous ? $", Warning: {DangerReason}" : "");
    public ICommand ToggleTracking { get; }
    public ICommand ToggleFavorite { get; }
    public void Update(WorldSnapshot snapshot, int cutoff, bool force = false)
    {
        if (!force && _snapshot == snapshot && _cutoff == cutoff) return;
        _snapshot = snapshot; _cutoff = cutoff; Notify(null);
    }
}

public sealed class DashboardViewModel : ObservableModel, IDisposable
{
    private readonly MonitoringEngine _engine;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<int, WorldCardModel> _models = [];
    private bool _disposed, _preferencesLoaded, _showSettings, _browseHidden;
    private int _queued;
    private string _filterMembership = "";
    private string _search = "", _cutoff = "30", _interval = "5", _feedback = "";
    private bool _topmost, _minimizeToTray, _closeToTray;
    public DashboardViewModel(MonitoringEngine engine, Dispatcher dispatcher)
    {
        _engine = engine; _dispatcher = dispatcher;
        Snapshot = engine.GetSnapshot();
        WorldView = CollectionViewSource.GetDefaultView(AllWorlds);
        WorldView.Filter = item => Matches((WorldCardModel)item);
        OpenSettings = new UiCommand(() => { ShowSettings = true; return Task.CompletedTask; });
        BackToWorlds = new UiCommand(() => { ShowSettings = false; return Task.CompletedTask; });
        CheckConnection = new UiCommand(async () =>
        {
            Feedback = "";
            try { await _engine.RecheckAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Feedback = $"Connection check failed: {error.Message}"; }
        });
        ResetTracking = new UiCommand(() => { _engine.ResetManualTracking(); Feedback = "Session choices reset."; return Task.CompletedTask; });
        ApplySettings = new UiCommand(ApplyAsync);
        _engine.Changed += OnEngineChanged;
        Refresh();
    }
    public MonitorSnapshot Snapshot { get; private set; }
    public ObservableCollection<WorldCardModel> AllWorlds { get; } = [];
    public ObservableCollection<WorldCardModel> Favorites { get; } = [];
    public ObservableCollection<WorldCardModel> Tracked { get; } = [];
    public ICollectionView WorldView { get; }
    public bool ShowSettings { get => _showSettings; set { if (Set(ref _showSettings, value)) Notify(nameof(ShowDashboard)); } }
    public bool ShowDashboard => !ShowSettings;
    public bool BrowseHidden { get => _browseHidden; set { if (Set(ref _browseHidden, value)) WorldView.Refresh(); } }
    public string Search { get => _search; set { if (Set(ref _search, value)) WorldView.Refresh(); } }
    public string Cutoff { get => _cutoff; set => Set(ref _cutoff, value); }
    public string Interval { get => _interval; set => Set(ref _interval, value); }
    public bool AlwaysOnTop { get => _topmost; set => Set(ref _topmost, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }
    public bool CloseToTray { get => _closeToTray; set => Set(ref _closeToTray, value); }
    public string Feedback { get => _feedback; private set => Set(ref _feedback, value); }
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasTracked => Tracked.Count > 0;
    public bool HasNotice => Snapshot.Notice is not null;
    public string LiveCount => Snapshot.LiveCount.ToString();
    public string BestConnection => Snapshot.Worlds.Where(w => w.IsTracked && w.LatestResult?.Status == ProbeStatus.Success)
        .Select(w => w.LatestResult!.RoundTripMilliseconds).Where(ms => ms is >= 0).Min() is { } best ? $"{best} ms" : "—";
    public string EmptyTitle => !Snapshot.SettingsLoaded || Snapshot.IsScanning ? "Finding your worlds" : "No worlds under your cutoff";
    public string EmptyDescription => Snapshot.IsScanning ? "Checking the world catalogue. Open settings for progress." :
        Snapshot.CatalogueFetchedAt is null ? "The world catalogue is unavailable. Open settings to check your connection and retry." :
        "Open settings to raise the latency cutoff, pin a favorite, or check your connection again.";
    public string ScanStatus => Snapshot.IsScanning ? $"{(Snapshot.IsRecheckingHidden ? "Reviewing hidden worlds" : "Checking all worlds")} · {Snapshot.ScanCompleted} / {Snapshot.ScanTotal}" : "Ready";
    public bool CanCheckConnection => Snapshot.SettingsLoaded && !Snapshot.IsScanning;
    public double ScanMaximum => Math.Max(1, Snapshot.ScanTotal);
    public string CatalogueStatus => Snapshot.CatalogueFetchedAt is { } fetched ? $"World catalogue saved {fetched.ToLocalTime():g}" : "No saved world catalogue";
    public string MonitoringStatus => $"{Snapshot.LiveCount} live · {Snapshot.HiddenCount} hidden · {Snapshot.Settings.RefreshIntervalSeconds}s refresh · hidden worlds reviewed every 5 minutes";
    public string Version => $"Version {typeof(DashboardViewModel).Assembly.GetName().Version?.ToString(3)} · Windows x64";
    public ICommand OpenSettings { get; }
    public ICommand BackToWorlds { get; }
    public ICommand CheckConnection { get; }
    public ICommand ResetTracking { get; }
    public ICommand ApplySettings { get; }

    private bool Matches(WorldCardModel world) => (BrowseHidden || world.IsTracked || world.IsFavorite) &&
        (string.IsNullOrWhiteSpace(Search) || $"{world.Id} {world.Region} {world.Activity} {world.Membership}"
            .Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase));

    private void OnEngineChanged(object? sender, EventArgs args)
    {
        if (_disposed || Interlocked.Exchange(ref _queued, 1) != 0) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref _queued, 0);
            if (!_disposed) Refresh();
        }));
    }

    public void Refresh()
    {
        Snapshot = _engine.GetSnapshot();
        if (Snapshot.SettingsLoaded && !_preferencesLoaded)
        {
            _preferencesLoaded = true;
            Cutoff = Snapshot.Settings.LatencyCutoffMs.ToString(); Interval = Snapshot.Settings.RefreshIntervalSeconds.ToString();
            AlwaysOnTop = Snapshot.Settings.AlwaysOnTop; MinimizeToTray = Snapshot.Settings.MinimizeToTray; CloseToTray = Snapshot.Settings.CloseToTray;
        }
        foreach (var world in Snapshot.Worlds)
        {
            if (!_models.TryGetValue(world.Id, out var model)) _models[world.Id] = model = new(world, this);
            model.Update(world, Snapshot.Settings.LatencyCutoffMs);
        }
        Sync(AllWorlds, Snapshot.Worlds.OrderBy(w => w.Id));
        Sync(Favorites, Snapshot.Favorites); Sync(Tracked, Snapshot.Tracked);
        foreach (var id in _models.Keys.Except(Snapshot.Worlds.Select(w => w.Id)).ToArray()) _models.Remove(id);
        string membership = string.Join(",", Snapshot.Worlds.Where(w => w.IsTracked || w.IsFavorite).Select(w => w.Id).Order());
        if (_filterMembership != membership) { _filterMembership = membership; WorldView.Refresh(); }
        // No window activation, selection, or scroll operations belong in this path.
        Notify(null);
    }

    private void Sync(ObservableCollection<WorldCardModel> collection, IEnumerable<WorldSnapshot> worlds)
    {
        var desired = worlds.Select(w => _models[w.Id]).ToArray();
        for (int i = collection.Count - 1; i >= 0; i--) if (!desired.Contains(collection[i])) collection.RemoveAt(i);
        for (int i = 0; i < desired.Length; i++)
        {
            if (i < collection.Count && collection[i] == desired[i]) continue;
            int previous = collection.IndexOf(desired[i]);
            if (previous >= 0) collection.Move(previous, i); else collection.Insert(i, desired[i]);
        }
    }

    public void SetTracking(WorldCardModel world)
    {
        Feedback = _engine.SetTracking(world.Id, !world.IsTracked) ? "Tracking updated for this session." :
            "All 20 ordinary slots are explicitly assigned. Turn off a session choice first. Pinned worlds stay tracked.";
        Refresh();
        world.Update(world.Snapshot, Snapshot.Settings.LatencyCutoffMs, force: true);
    }
    public async Task SetFavoriteAsync(WorldCardModel world)
    {
        try { Feedback = await _engine.SetFavoriteAsync(world.Id, !world.IsFavorite) ? "Always-tracked worlds updated." : "You can pin up to five worlds. Unpin one first."; }
        catch (OperationCanceledException) { }
        Refresh();
        world.Update(world.Snapshot, Snapshot.Settings.LatencyCutoffMs, force: true);
    }
    private async Task ApplyAsync()
    {
        if (!int.TryParse(Cutoff, out int cutoff) || cutoff is < 1 or > 2000 ||
            !int.TryParse(Interval, out int interval) || interval is < 1 or > 60)
        { Feedback = "Enter a cutoff from 1–2,000 ms and an interval from 1–60 seconds."; return; }
        try
        {
            await _engine.UpdatePreferencesAsync(cutoff, interval, AlwaysOnTop, MinimizeToTray, CloseToTray);
            Feedback = "Settings applied. Check connection to find newly eligible worlds now.";
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { _disposed = true; _engine.Changed -= OnEngineChanged; }
}
