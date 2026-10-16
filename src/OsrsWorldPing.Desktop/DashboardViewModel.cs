using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using OsrsWorldPing.Core;

namespace OsrsWorldPing.Desktop;

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
        ToggleFavorite = new UiCommand(() => owner.SetFavoriteAsync(this));
    }
    public int Id => _snapshot.Id;
    public WorldSnapshot Snapshot => _snapshot;
    public WorldMetadata Metadata => WorldMetadata.From(_snapshot.World);
    public string WorldLabel => $"World {Id}";
    public bool IsFavorite => _snapshot.IsFavorite;
    public bool IsTracked => _snapshot.IsTracked;
    public bool IsMembers => _snapshot.World?.Members == true;
    public bool HasWorld => _snapshot.World is not null;
    public string? ActivitySprite => WorldActivityArt.SpriteFor(_snapshot.World) is { } name
        ? $"pack://application:,,,/OsrsWorldPing;component/Assets/Osrs/{name}.png" : null;
    public bool HasActivitySprite => ActivitySprite is not null;
    public bool HasActivityFallback => !HasActivitySprite;
    public bool ShowScanResult => HasWorld && !IsTracked;
    public string FavoriteHint => IsFavorite ? $"Remove world {Id} from favorites" : $"Always track world {Id} (up to five favorites)";
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
    public string ReadingLabel => !HasWorld ? "Unavailable" : !_snapshot.IsTracked ? "Scan result" :
        _snapshot.IsScanResult ? "Initial reading" : "Live reading";
    public string ReadingDetails => $"{ReadingLabel} · {(_snapshot.LastChecked is { } at ? at.ToLocalTime().ToString("g") : "Not checked yet")}";
    public string AccessibleDescription => $"{WorldLabel}, {Region}, {Membership}, {Activity}, {Latency}" +
        (IsDangerous ? $", Warning: {DangerReason}" : "");
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
    private bool _disposed, _preferencesLoaded, _showSettings, _showAllWorlds;
    private int _queued, _selectedSettingsTab;
    private DateTimeOffset? _summarizedCatalogue;
    private string _cutoff = "30", _interval = "5", _feedback = "", _favoriteFeedback = "";
    private string _connectionSummary = "Waiting for the initial world check.";
    private bool _connectionHealthy;
    private bool _topmost, _minimizeToTray, _closeToTray;
    public DashboardViewModel(MonitoringEngine engine, Dispatcher dispatcher)
    {
        _engine = engine; _dispatcher = dispatcher;
        Snapshot = engine.GetSnapshot();
        OpenSettings = new UiCommand(() => { ShowSettings = true; return Task.CompletedTask; });
        OpenConnectionSettings = new UiCommand(() => { SelectedSettingsTab = 1; ShowSettings = true; return Task.CompletedTask; });
        BackToWorlds = new UiCommand(() => { ShowSettings = false; return Task.CompletedTask; });
        ToggleSettings = new UiCommand(() => { ShowSettings = !ShowSettings; return Task.CompletedTask; });
        CheckConnection = new UiCommand(async () =>
        {
            Feedback = "";
            try { await _engine.RecheckAsync(); Refresh(); UpdateConnectionSummary(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Feedback = $"Connection check failed: {error.Message}"; }
        });
        ResetDefaults = new UiCommand(ResetDefaultsAsync);
        ApplySettings = new UiCommand(ApplyAsync);
        _engine.Changed += OnEngineChanged;
        Refresh();
    }
    public MonitorSnapshot Snapshot { get; private set; }
    public ObservableCollection<WorldCardModel> AllWorlds { get; } = [];
    public ObservableCollection<WorldCardModel> Favorites { get; } = [];
    public ObservableCollection<WorldCardModel> Tracked { get; } = [];
    public ObservableCollection<WorldCardModel> Hidden { get; } = [];
    public bool ShowSettings { get => _showSettings; set { if (Set(ref _showSettings, value)) Notify(null); } }
    public bool ShowDashboard => !ShowSettings;
    public int SelectedSettingsTab { get => _selectedSettingsTab; set { if (Set(ref _selectedSettingsTab, value)) Notify(null); } }
    public bool ShowWorldTracking => ShowSettings && SelectedSettingsTab == 0;
    public bool ShowAllWorlds { get => _showAllWorlds; set { if (Set(ref _showAllWorlds, value)) Notify(null); } }
    public bool ShowHiddenWorlds => ShowWorldTracking && ShowAllWorlds;
    public string ShowAllLabel => ShowAllWorlds ? "Hide all" : "Show all";
    public string Cutoff { get => _cutoff; set { if (Set(ref _cutoff, value)) Notify(null); } }
    public string Interval { get => _interval; set { if (Set(ref _interval, value)) Notify(null); } }
    public string? CutoffError => int.TryParse(Cutoff, out int value) && value is >= 1 and <= 2000 ? null : "Enter a value from 1 to 2,000 ms.";
    public string? IntervalError => int.TryParse(Interval, out int value) && value is >= 5 and <= 600 ? null : "Enter a value from 5 to 600 seconds.";
    public bool CanApplySettings => Snapshot.SettingsLoaded && CutoffError is null && IntervalError is null;
    public bool HasCutoffError => CutoffError is not null;
    public bool HasIntervalError => IntervalError is not null;
    public bool AlwaysOnTop { get => _topmost; set => Set(ref _topmost, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }
    public bool CloseToTray { get => _closeToTray; set => Set(ref _closeToTray, value); }
    public string Feedback { get => _feedback; private set { if (Set(ref _feedback, value)) Notify(nameof(HasFeedback)); } }
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasTracked => Tracked.Count > 0;
    public bool HasFeedback => !string.IsNullOrEmpty(Feedback);
    public string FavoriteFeedback { get => _favoriteFeedback; private set { if (Set(ref _favoriteFeedback, value)) Notify(nameof(HasFavoriteFeedback)); } }
    public bool HasFavoriteFeedback => !string.IsNullOrEmpty(FavoriteFeedback);
    public bool HasNotice => Snapshot.Notice is not null;
    public string LiveCount => Snapshot.LiveCount.ToString();
    public string BestConnection => Snapshot.Worlds.Where(w => w.IsTracked && w.LatestResult?.Status == ProbeStatus.Success)
        .Select(w => w.LatestResult!.RoundTripMilliseconds).Where(ms => ms is >= 0).Min() is { } best ? $"{best} ms" : "—";
    public string EmptyTitle => !Snapshot.SettingsLoaded || Snapshot.IsScanning ? "Finding your worlds" :
        Snapshot.CatalogueFetchedAt is null ? "World catalogue unavailable" : "No worlds under your cutoff";
    public string EmptyDescription => Snapshot.IsScanning ? "Checking the world catalogue. Open settings for progress." :
        Snapshot.CatalogueFetchedAt is null ? "The world catalogue is unavailable. Open settings to check your connection and retry." :
        "Open settings to raise the latency cutoff, add a favorite, or check your connection again.";
    public string ScanStatus => Snapshot.IsScanning ? $"{(Snapshot.IsRecheckingHidden ? "Reviewing hidden worlds" : "Checking all worlds")} · {Snapshot.ScanCompleted} / {Snapshot.ScanTotal}" : "Ready";
    public bool CanCheckConnection => Snapshot.SettingsLoaded && !Snapshot.IsScanning;
    public double ScanMaximum => Math.Max(1, Snapshot.ScanTotal);
    public string WorldsUpdatedAt => Snapshot.CatalogueFetchedAt is { } fetched ? $"Worlds updated at {fetched.ToLocalTime():g}" : "Worlds have not been downloaded";
    public string ConnectionSummary => Snapshot.IsScanning ? ScanStatus : _connectionSummary;
    public bool ConnectionHealthy => !Snapshot.IsScanning && _connectionHealthy;
    public string Version => $"Version {typeof(DashboardViewModel).Assembly.GetName().Version?.ToString(3)} · Windows x64";
    public ICommand OpenSettings { get; }
    public ICommand OpenConnectionSettings { get; }
    public ICommand BackToWorlds { get; }
    public ICommand ToggleSettings { get; }
    public ICommand CheckConnection { get; }
    public ICommand ResetDefaults { get; }
    public ICommand ApplySettings { get; }

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
        Sync(Hidden, Snapshot.Worlds.Where(w => !w.IsTracked && !w.IsFavorite).OrderBy(w => w.Id));
        foreach (var id in _models.Keys.Except(Snapshot.Worlds.Select(w => w.Id)).ToArray()) _models.Remove(id);
        if (!Snapshot.IsScanning && Snapshot.CatalogueFetchedAt is { } at && at != _summarizedCatalogue)
        { _summarizedCatalogue = at; UpdateConnectionSummary(); }
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

    public async Task SetFavoriteAsync(WorldCardModel world)
    {
        try { FavoriteFeedback = await _engine.SetFavoriteAsync(world.Id, !world.IsFavorite) ? "" : "You can save up to five favorites. Remove one first."; }
        catch (OperationCanceledException) { }
        Refresh();
        world.Update(world.Snapshot, Snapshot.Settings.LatencyCutoffMs, force: true);
    }
    private void UpdateConnectionSummary()
    {
        var worlds = Snapshot.Worlds.Where(w => w.World is not null).ToArray();
        int responded = worlds.Count(w => w.LatestResult?.Status == ProbeStatus.Success);
        _connectionHealthy = worlds.Length > 0 && responded == worlds.Length;
        _connectionSummary = worlds.Length == 0 ? "World catalogue unavailable. Check your connection and retry." :
            _connectionHealthy ? $"All {worlds.Length} worlds replied." : $"{responded} of {worlds.Length} worlds replied. Others timed out or could not be reached.";
        Notify(nameof(ConnectionSummary)); Notify(nameof(ConnectionHealthy));
    }
    private async Task ResetDefaultsAsync()
    {
        try
        {
            await _engine.UpdatePreferencesAsync(30, 5, false, false, false);
            Cutoff = "30"; Interval = "5"; AlwaysOnTop = MinimizeToTray = CloseToTray = false;
            Feedback = "Default application preferences restored. Favorites and window placement are kept.";
        }
        catch (OperationCanceledException) { }
    }
    private async Task ApplyAsync()
    {
        if (!int.TryParse(Cutoff, out int cutoff) || cutoff is < 1 or > 2000 ||
            !int.TryParse(Interval, out int interval) || interval is < AppSettings.MinimumRefreshIntervalSeconds or > AppSettings.MaximumRefreshIntervalSeconds)
        { Feedback = "Enter a cutoff from 1–2,000 ms and an interval from 5–600 seconds."; return; }
        try
        {
            await _engine.UpdatePreferencesAsync(cutoff, interval, AlwaysOnTop, MinimizeToTray, CloseToTray);
            Feedback = "Settings applied. Check connection to find newly eligible worlds now.";
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { _disposed = true; _engine.Changed -= OnEngineChanged; }
}
