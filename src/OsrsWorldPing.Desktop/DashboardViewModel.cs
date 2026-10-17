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
    public bool HasActivitySprite => HasActivity && ActivitySprite is not null;
    public bool HasActivityFallback => HasActivity && !HasActivitySprite;
    public bool HasBestActivityFallback => !HasActivitySprite;
    public bool HasActivity => HasWorld && !string.IsNullOrWhiteSpace(_snapshot.World!.Activity) && _snapshot.World.Activity.Trim() != "-";
    public string ActivityLabel => HasActivity ? Activity : HasWorld ? "—" : "Unavailable";
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
        { Status: ProbeStatus.TimedOut } => "No reply",
        { Status: ProbeStatus.DnsError } => "DNS error",
        { Status: ProbeStatus.Unreachable } => "Unreachable",
        { Status: ProbeStatus.PermissionDenied } => "Permission denied",
        { Status: ProbeStatus.Unsupported } => "ICMP unavailable",
        { Status: ProbeStatus.NetworkError } => "Network error",
        { Status: ProbeStatus.Error } => "Probe failed",
        _ => "Waiting"
    };
    public bool IsGood => HasWorld && _snapshot.LatestResult?.IsGood(_cutoff) == true;
    public string ReadingLabel => !HasWorld ? "Unavailable" : !_snapshot.IsTracked ? "Scan result" :
        _snapshot.IsScanResult ? "Initial reading" : "Live reading";
    public string ReadingDetails => $"{ReadingLabel} · {(_snapshot.LastChecked is { } at ? at.ToLocalTime().ToString("g") : "Not checked yet")}" +
        (_snapshot.LatestResult?.Diagnostic is { } detail ? $" · {detail.Stage}: {detail.NativeCode}" : "") +
        (_snapshot.World?.Population < 0 ? " · Catalogue reports this world offline" : "");
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
    private readonly IClock _feedbackClock;
    private CancellationTokenSource? _feedbackTimer, _favoriteTimer, _connectionTimer;
    private bool _showConnectionFeedback;
    private readonly Dictionary<int, WorldCardModel> _models = [];
    private bool _disposed, _preferencesLoaded, _showSettings, _showAllWorlds;
    private int _queued, _selectedSettingsTab;
    private DateTimeOffset? _summarizedCatalogue;
    private string _cutoff = "30", _interval = "5", _feedback = "", _favoriteFeedback = "";
    private string _connectionSummary = "Waiting for the initial world check.";
    private bool _connectionHealthy;
    private bool _topmost, _minimizeToTray, _closeToTray;
    public DashboardViewModel(MonitoringEngine engine, Dispatcher dispatcher, IClock? feedbackClock = null)
    {
        _engine = engine; _dispatcher = dispatcher; _feedbackClock = feedbackClock ?? new SystemClock();
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
        CopyDiagnostics = new UiCommand(() =>
        {
            try { System.Windows.Clipboard.SetText(ConnectionDiagnostics); Feedback = "Diagnostics copied."; }
            catch (System.Runtime.InteropServices.ExternalException) { Feedback = "The clipboard is busy. Try again."; }
            return Task.CompletedTask;
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
    public ObservableCollection<WorldCardModel> Members { get; } = [];
    public ObservableCollection<WorldCardModel> FreeToPlay { get; } = [];
    public bool HasMembers => Members.Count != 0;
    public bool HasFreeToPlay => FreeToPlay.Count != 0;
    public bool ShowSettings { get => _showSettings; set { if (Set(ref _showSettings, value)) { ClearFeedback(); Notify(null); } } }
    public bool ShowDashboard => !ShowSettings;
    public int SelectedSettingsTab { get => _selectedSettingsTab; set { if (Set(ref _selectedSettingsTab, value)) { ClearFeedback(); Notify(null); } } }
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
    public string Feedback { get => _feedback; private set { if (Set(ref _feedback, value)) { Notify(nameof(HasFeedback)); ScheduleFeedback(false); } } }
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasTracked => Tracked.Count > 0;
    public bool HasFeedback => !string.IsNullOrEmpty(Feedback);
    public string FavoriteFeedback { get => _favoriteFeedback; private set { if (Set(ref _favoriteFeedback, value)) { Notify(nameof(HasFavoriteFeedback)); ScheduleFeedback(true); } } }
    public bool HasFavoriteFeedback => !string.IsNullOrEmpty(FavoriteFeedback);
    public bool HasNotice => Snapshot.Notice is not null;
    public string LiveCount => Snapshot.LiveCount.ToString();
    public WorldCardModel? BestWorld => Snapshot.Worlds.Where(w => w.IsTracked && w.LatestResult is { Status: ProbeStatus.Success, RoundTripMilliseconds: >= 0 })
        .OrderBy(w => w.LatestResult!.RoundTripMilliseconds).ThenBy(w => w.Id)
        .Select(w => _models.GetValueOrDefault(w.Id)).FirstOrDefault();
    public bool HasBestWorld => BestWorld is not null;
    public string BestConnection => BestWorld?.Latency ?? "—";
    public string DiagnosticSummary => string.Join(" · ", Snapshot.Worlds.Where(w => w.World is not null)
        .GroupBy(w => w.LatestResult?.Status).OrderBy(g => g.Key)
        .Select(g => $"{g.Count()} {g.Key switch { ProbeStatus.Success => "replied", ProbeStatus.TimedOut => "no ICMP reply", ProbeStatus.Unreachable => "destination unreachable", ProbeStatus.DnsError => "DNS failed", ProbeStatus.PermissionDenied => "permission denied", ProbeStatus.Unsupported => "ICMP unavailable", ProbeStatus.NetworkError => "network errors", ProbeStatus.Error => "probe errors", _ => "waiting" }}"));
    public string ConnectionDiagnostics => $"OSRS World Ping · {Version}\n{System.Runtime.InteropServices.RuntimeInformation.OSDescription}\nCatalogue: {Snapshot.CatalogueFetchedAt:O}\n{DiagnosticSummary}\n" +
        string.Join("\n", Snapshot.Worlds.OrderBy(w => w.Id).Select(w => $"{w.Id} | {w.World?.Host ?? "unavailable"} | population {w.World?.Population} | {w.LatestResult?.Status} | {w.LatestResult?.Diagnostic?.Stage} | {w.LatestResult?.Diagnostic?.NativeCode} | {w.LatestResult?.Diagnostic?.Address} | {w.LastChecked:O}"));
    public string EmptyTitle => !Snapshot.SettingsLoaded || Snapshot.IsScanning ? "Finding your worlds" :
        Snapshot.CatalogueFetchedAt is null ? "World catalogue unavailable" : "No worlds under your cutoff";
    public string EmptyDescription => !Snapshot.SettingsLoaded ? "Loading your preferences and preparing the world check." : Snapshot.IsScanning ? "Checking the world catalogue. Open settings for progress." :
        Snapshot.CatalogueFetchedAt is null ? "The world catalogue is unavailable. Open settings to check your connection and retry." :
        "Open settings to raise the latency cutoff, add a favorite, or check your connection again.";
    public string ScanStatus => Snapshot.IsScanning ? $"{(Snapshot.IsRecheckingHidden ? "Reviewing hidden worlds" : "Checking all worlds")} · {Snapshot.ScanCompleted} / {Snapshot.ScanTotal}" : "Ready";
    public bool CanCheckConnection => Snapshot.SettingsLoaded && !Snapshot.IsScanning;
    public double ScanMaximum => Math.Max(1, Snapshot.ScanTotal);
    public string WorldsUpdatedAt => Snapshot.CatalogueFetchedAt is { } fetched ? $"Worlds updated at {fetched.ToLocalTime():g}" : "Worlds have not been downloaded";
    public bool HasConnectionFeedback => Snapshot.IsScanning || _showConnectionFeedback;
    public string ConnectionSummary => Snapshot.IsScanning ? ScanStatus : _showConnectionFeedback ? _connectionSummary : "";
    public bool ConnectionPartial => !Snapshot.IsScanning && Snapshot.Worlds.Any(w => w.LatestResult?.Status == ProbeStatus.Success) && !_connectionHealthy;
    public bool ConnectionHealthy => !Snapshot.IsScanning && _connectionHealthy;
    public string Version => $"Version {typeof(DashboardViewModel).Assembly.GetName().Version?.ToString(3)} · Windows x64";
    public ICommand OpenSettings { get; }
    public ICommand OpenConnectionSettings { get; }
    public ICommand BackToWorlds { get; }
    public ICommand ToggleSettings { get; }
    public ICommand CheckConnection { get; }
    public ICommand CopyDiagnostics { get; }
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
        Sync(Members, Snapshot.Tracked.Where(w => w.World!.Members));
        Sync(FreeToPlay, Snapshot.Tracked.Where(w => !w.World!.Members));
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
            _connectionHealthy ? $"All {worlds.Length} worlds replied." : $"Check complete · {responded} of {worlds.Length} worlds replied. See Diagnostics for other results.";
        _showConnectionFeedback = true;
        _connectionTimer?.Cancel(); _connectionTimer?.Dispose(); _connectionTimer = new CancellationTokenSource();
        _ = ExpireConnectionFeedbackAsync(_connectionTimer.Token);
        Notify(nameof(ConnectionSummary)); Notify(nameof(ConnectionHealthy)); Notify(nameof(ConnectionPartial)); Notify(nameof(HasConnectionFeedback));
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
    private void ClearFeedback()
    {
        Feedback = ""; FavoriteFeedback = "";
        _connectionTimer?.Cancel(); _showConnectionFeedback = false;
        Notify(nameof(ConnectionSummary)); Notify(nameof(HasConnectionFeedback));
    }
    private async Task ExpireConnectionFeedbackAsync(CancellationToken token)
    {
        try
        {
            await _feedbackClock.DelayAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                if (!_disposed && !token.IsCancellationRequested) { _showConnectionFeedback = false; Notify(nameof(ConnectionSummary)); Notify(nameof(HasConnectionFeedback)); }
            });
        }
        catch (OperationCanceledException) { }
    }
    private void ScheduleFeedback(bool favorite)
    {
        var previous = favorite ? _favoriteTimer : _feedbackTimer;
        previous?.Cancel(); previous?.Dispose();
        var source = new CancellationTokenSource();
        if (favorite) _favoriteTimer = source; else _feedbackTimer = source;
        if (!string.IsNullOrEmpty(favorite ? FavoriteFeedback : Feedback)) _ = ExpireFeedbackAsync(favorite, source.Token);
    }
    private async Task ExpireFeedbackAsync(bool favorite, CancellationToken token)
    {
        try
        {
            await _feedbackClock.DelayAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() => { if (!_disposed && !token.IsCancellationRequested) { if (favorite) FavoriteFeedback = ""; else Feedback = ""; } });
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        _disposed = true; _engine.Changed -= OnEngineChanged;
        _feedbackTimer?.Cancel(); _feedbackTimer?.Dispose(); _favoriteTimer?.Cancel(); _favoriteTimer?.Dispose(); _connectionTimer?.Cancel(); _connectionTimer?.Dispose();
    }
}
