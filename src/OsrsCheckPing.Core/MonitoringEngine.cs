namespace OsrsCheckPing.Core;

public sealed class MonitoringEngine : IAsyncDisposable
{
    public const int MaximumAutomaticWorlds = 20;
    public const int MaximumFavoriteWorlds = 5;
    public static TimeSpan HiddenRecheckInterval => TimeSpan.FromMinutes(5);

    private sealed class WorldState(int id, WorldInfo? world)
    {
        public int Id { get; } = id;
        public WorldInfo? World { get; } = world;
        public bool AutomaticallyEligible { get; set; }
        public bool Removed { get; set; }
        public int BadReadings { get; set; }
        public int Generation { get; set; }
        public ProbeResult? Result { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public bool IsScanResult { get; set; }
    }

    private readonly object _sync = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stopToken;
    private readonly IWorldCatalogueClient _catalogueClient;
    private readonly ILatencyProbe _probe;
    private readonly ISettingsStore _store;
    private readonly IClock _clock;
    private readonly int _maximumConcurrency;
    private readonly Dictionary<int, bool> _manualTracking = [];
    private HashSet<int> _trackedWorldIds = [];
    private Dictionary<int, WorldState> _worlds = [];
    private AppSettings _settings = new();
    private Task? _startTask;
    private Task? _runner;
    private Task? _disposeTask;
    private bool _settingsLoaded;
    private bool _isScanning;
    private bool _isRecheckingHidden;
    private bool _disposed;
    private int _scanRequested;
    private int _scanCompleted;
    private int _scanTotal;
    private int _policyVersion;
    private string? _catalogueNotice;
    private string? _settingsNotice;
    private DateTimeOffset? _catalogueFetchedAt;
    private DateTimeOffset? _nextHiddenRecheckAt;

    public MonitoringEngine(IWorldCatalogueClient catalogueClient, ILatencyProbe probe, ISettingsStore store,
        IClock? clock = null, int maximumConcurrency = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        _stopToken = _lifetime.Token;
        _catalogueClient = catalogueClient;
        _probe = probe;
        _store = store;
        _clock = clock ?? new SystemClock();
        _maximumConcurrency = maximumConcurrency;
    }

    public event EventHandler? Changed;

    public MonitorSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var settings = _settings with { FavoriteWorldIds = Array.AsReadOnly(_settings.FavoriteWorldIds.ToArray()) };
            var worlds = _worlds.Values.Select(w => new WorldSnapshot(w.Id, w.World, IsFavorite(w.Id),
                IsTracked(w), _manualTracking.TryGetValue(w.Id, out bool enabled) ? enabled : null,
                w.AutomaticallyEligible, w.Removed, w.Result, w.CheckedAt, w.IsScanResult, w.BadReadings)).ToArray();
            string notice = string.Join(" ", new[] { _settingsNotice, _catalogueNotice }.OfType<string>());
            return new MonitorSnapshot(settings, worlds, _settingsLoaded, _isScanning, _scanCompleted,
                _scanTotal, notice.Length > 0 ? notice : null, _catalogueFetchedAt,
                _nextHiddenRecheckAt, _isRecheckingHidden, _clock.UtcNow);
        }
    }

    public Task StartAsync()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _startTask ??= StartCoreAsync();
        }
    }

    private async Task StartCoreAsync()
    {
        try
        {
            var settings = await _store.LoadSettingsAsync(_stopToken).ConfigureAwait(false);
            lock (_sync)
                _settings = settings.Sanitize();
        }
        catch (SettingsMigrationSaveException error)
        {
            lock (_sync) _settings = error.Settings.Sanitize();
            SetSettingsNotice("Upgraded settings could not be saved. Your saved choices are loaded for this session. " + error.InnerException?.Message);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            SetSettingsNotice($"Saved settings could not be loaded. Using defaults. {error.Message}");
        }
        lock (_sync)
        {
            _settingsLoaded = true;
            AddMissingFavorites();
        }
        NotifyChanged();
        _stopToken.ThrowIfCancellationRequested();
        await RecheckAsync(_stopToken).ConfigureAwait(false);
        _stopToken.ThrowIfCancellationRequested();
        _runner = RunLoopAsync(_stopToken);
    }

    public async Task<bool> RecheckAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginScan(hidden: false))
            return false;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);
        bool acquired = false;
        try
        {
            await _operation.WaitAsync(linked.Token).ConfigureAwait(false);
            acquired = true;
            var catalogue = await FetchCatalogueAsync(linked.Token).ConfigureAwait(false);
            if (catalogue is null)
                return true;
            lock (_sync)
                _scanTotal = catalogue.Worlds.Count;
            NotifyChanged();

            var discovered = new System.Collections.Concurrent.ConcurrentDictionary<int, WorldState>();
            await Parallel.ForEachAsync(catalogue.Worlds,
                new ParallelOptions { MaxDegreeOfParallelism = _maximumConcurrency, CancellationToken = linked.Token },
                async (world, token) =>
                {
                    var result = await ProbeSafelyAsync(world.Host, token).ConfigureAwait(false);
                    discovered[world.Id] = new WorldState(world.Id, world)
                    {
                        Result = result,
                        CheckedAt = _clock.UtcNow,
                        IsScanResult = true
                    };
                    lock (_sync)
                        _scanCompleted++;
                    NotifyChanged();
                }).ConfigureAwait(false);

            lock (_sync)
            {
                _worlds = discovered.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var world in _worlds.Values)
                    world.AutomaticallyEligible = world.Result!.IsGood(_settings.LatencyCutoffMs);
                _catalogueFetchedAt = catalogue.FetchedAt;
                AddMissingFavorites();
                SelectTrackedWorlds();
                _nextHiddenRecheckAt = _clock.UtcNow + HiddenRecheckInterval;
            }
            return true;
        }
        finally
        {
            EndScan(acquired);
        }
    }

    public async Task<bool> RecheckHiddenWorldsAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginScan(hidden: true))
            return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);
        bool acquired = false;
        try
        {
            await _operation.WaitAsync(linked.Token).ConfigureAwait(false);
            acquired = true;
            (WorldState State, int Generation)[] hidden;
            lock (_sync)
            {
                hidden = _worlds.Values.Where(w => w.World is not null && !IsTracked(w))
                    .Select(w => (w, w.Generation)).ToArray();
                _scanTotal = hidden.Length;
            }
            NotifyChanged();
            var results = new System.Collections.Concurrent.ConcurrentDictionary<int, (ProbeResult Result, DateTimeOffset CheckedAt)>();
            await Parallel.ForEachAsync(hidden,
                new ParallelOptions { MaxDegreeOfParallelism = _maximumConcurrency, CancellationToken = linked.Token },
                async (entry, token) =>
                {
                    var result = await ProbeSafelyAsync(entry.State.World!.Host, token).ConfigureAwait(false);
                    results[entry.State.Id] = (result, _clock.UtcNow);
                    lock (_sync)
                        _scanCompleted++;
                    NotifyChanged();
                }).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                foreach (var entry in hidden)
                {
                    var world = entry.State;
                    if (world.Generation != entry.Generation)
                        continue;
                    var result = results[world.Id];
                    world.Result = result.Result;
                    world.CheckedAt = result.CheckedAt;
                    world.IsScanResult = true;
                    world.AutomaticallyEligible = result.Result.IsGood(_settings.LatencyCutoffMs);
                    world.Removed = false;
                    world.BadReadings = 0;
                }
                SelectTrackedWorlds();
                if (_catalogueFetchedAt is not null)
                    _nextHiddenRecheckAt = _clock.UtcNow + HiddenRecheckInterval;
            }
            return true;
        }
        finally
        {
            EndScan(acquired);
        }
    }

    private bool TryBeginScan(bool hidden)
    {
        lock (_sync)
        {
            if (_disposed || Interlocked.CompareExchange(ref _scanRequested, 1, 0) != 0)
                return false;
            _isScanning = true;
            _isRecheckingHidden = hidden;
            _scanCompleted = 0;
            _scanTotal = 0;
        }
        NotifyChanged();
        return true;
    }

    private void EndScan(bool acquired)
    {
        lock (_sync)
        {
            _isScanning = false;
            _isRecheckingHidden = false;
            // Release inside the lock so another scan cannot enter between the two state changes.
            if (acquired)
                _operation.Release();
            Interlocked.Exchange(ref _scanRequested, 0);
        }
        NotifyChanged();
    }

    private async Task<WorldCatalogue?> FetchCatalogueAsync(CancellationToken cancellationToken)
    {
        WorldCatalogue catalogue;
        try
        {
            var worlds = await _catalogueClient.FetchAsync(cancellationToken).ConfigureAwait(false);
            catalogue = new WorldCatalogue(worlds, _clock.UtcNow);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cached = await _store.LoadCatalogueAsync(cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    SetNotice($"Using the saved world catalogue from {cached.FetchedAt.ToLocalTime():g}. " +
                        $"Download failed: {error.Message}");
                    return cached;
                }
            }
            catch (Exception cacheError) when (!cancellationToken.IsCancellationRequested)
            {
                SetNotice($"World download failed and the saved catalogue could not be read. " +
                    $"Use Recheck all worlds to retry. {cacheError.Message}");
                return null;
            }
            SetNotice($"The world catalogue could not be downloaded. Use Recheck all worlds to retry. {error.Message}");
            return null;
        }

        SetNotice(null);
        try
        {
            await _store.SaveCatalogueAsync(catalogue, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            SetNotice($"Worlds were downloaded, but the offline catalogue could not be saved. {error.Message}");
        }
        return catalogue;
    }

    // Public so a host or deterministic test can request one scheduled cycle.
    // Busy cycles are skipped rather than queued behind a scan or another cycle.
    public async Task CheckTrackedWorldsAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);
        if (!await _operation.WaitAsync(0, linked.Token).ConfigureAwait(false))
            return;
        try
        {
            (WorldState State, int Generation)[] tracked;
            int policyVersion;
            lock (_sync)
            {
                if (_isScanning)
                    return;
                tracked = _worlds.Values.Where(IsTracked).Select(w => (w, w.Generation)).ToArray();
                policyVersion = _policyVersion;
            }
            await Parallel.ForEachAsync(tracked,
                new ParallelOptions { MaxDegreeOfParallelism = _maximumConcurrency, CancellationToken = linked.Token },
                async (entry, token) =>
                {
                    var result = await ProbeSafelyAsync(entry.State.World!.Host, token).ConfigureAwait(false);
                    lock (_sync)
                    {
                        var world = entry.State;
                        if (policyVersion != _policyVersion || world.Generation != entry.Generation || !IsTracked(world))
                            return;
                        world.Result = result;
                        world.CheckedAt = _clock.UtcNow;
                        world.IsScanResult = false;
                        world.BadReadings = IsFavorite(world.Id) || result.IsGood(_settings.LatencyCutoffMs)
                            ? 0 : world.BadReadings + 1;
                        if (world.BadReadings >= _settings.RequiredBadReadings)
                        {
                            world.Removed = true;
                            _trackedWorldIds.Remove(world.Id);
                        }
                    }
                }).ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
            NotifyChanged();
        }
    }

    public bool SetTracking(int worldId, bool enabled)
    {
        lock (_sync)
        {
            if (_disposed || !_worlds.TryGetValue(worldId, out var world) || world.World is null || IsFavorite(worldId))
                return false;
            if (enabled && !_manualTracking.GetValueOrDefault(worldId) &&
                _worlds.Values.Count(w => w.World is not null && !IsFavorite(w.Id) && _manualTracking.GetValueOrDefault(w.Id))
                    >= MaximumAutomaticWorlds)
                return false;
            _manualTracking[worldId] = enabled;
            world.Removed = false;
            world.BadReadings = 0;
            world.Generation++;
            SelectTrackedWorlds();
        }
        NotifyChanged();
        return true;
    }

    public void ResetManualTracking()
    {
        lock (_sync)
        {
            _manualTracking.Clear();
            foreach (var world in _worlds.Values)
            {
                world.Removed = false;
                world.BadReadings = 0;
                world.Generation++;
            }
            SelectTrackedWorlds();
        }
        NotifyChanged();
    }

    public async Task<bool> SetFavoriteAsync(int worldId, bool favorite)
    {
        lock (_sync)
        {
            if (_disposed || !_worlds.ContainsKey(worldId))
                return false;
            var ids = _settings.FavoriteWorldIds.ToList();
            if (favorite && !ids.Contains(worldId))
            {
                if (ids.Count == MaximumFavoriteWorlds)
                    return false;
                ids.Add(worldId);
            }
            else if (!favorite)
                ids.Remove(worldId);
            _settings = _settings with { FavoriteWorldIds = ids.ToArray() };
            var world = _worlds[worldId];
            world.BadReadings = 0;
            world.Generation++;
            // Removed/automatic/manual state stays intact underneath the favorite.
            if (!favorite && world.World is null)
                _worlds.Remove(worldId);
            SelectTrackedWorlds();
        }
        NotifyChanged();
        await SaveSettingsAsync().ConfigureAwait(false);
        return true;
    }

    public async Task UpdatePreferencesAsync(int cutoffMs, int intervalSeconds, bool alwaysOnTop,
        bool minimizeToTray, bool closeToTray)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cutoffMs, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cutoffMs, 2000);
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(intervalSeconds, 60);
        lock (_sync)
        {
            bool monitoringChanged = _settings.LatencyCutoffMs != cutoffMs || _settings.RefreshIntervalSeconds != intervalSeconds;
            _settings = _settings with
            {
                LatencyCutoffMs = cutoffMs,
                RefreshIntervalSeconds = intervalSeconds,
                AlwaysOnTop = alwaysOnTop,
                MinimizeToTray = minimizeToTray,
                CloseToTray = closeToTray
            };
            if (monitoringChanged)
            {
                _policyVersion++;
                foreach (var world in _worlds.Values)
                    world.BadReadings = 0;
            }
        }
        NotifyChanged();
        await SaveSettingsAsync().ConfigureAwait(false);
    }

    public async Task SaveWindowPlacementAsync(WindowPlacement placement)
    {
        lock (_sync)
            _settings = _settings with { Window = placement };
        await SaveSettingsAsync().ConfigureAwait(false);
    }

    private async Task SaveSettingsAsync()
    {
        await _saveGate.WaitAsync(_stopToken).ConfigureAwait(false);
        try
        {
            AppSettings settings;
            lock (_sync)
                settings = _settings;
            await _store.SaveSettingsAsync(settings, _stopToken).ConfigureAwait(false);
            SetSettingsNotice(null);
        }
        catch (Exception error) when (!_lifetime.IsCancellationRequested)
        {
            SetSettingsNotice($"Settings could not be saved. Changes will last only for this session. {error.Message}");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            TimeSpan delay;
            lock (_sync)
                delay = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds);
            while (true)
            {
                await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                var started = _clock.UtcNow;
                await CheckTrackedWorldsAsync(cancellationToken).ConfigureAwait(false);
                bool hiddenDue;
                lock (_sync)
                    hiddenDue = !_isScanning && _nextHiddenRecheckAt is { } due && due <= _clock.UtcNow;
                if (hiddenDue)
                    await RecheckHiddenWorldsAsync(cancellationToken).ConfigureAwait(false);
                // Compensate the next delay for probe time without running catch-up bursts.
                // The bounded probe pool may make a cycle longer than the configured interval.
                var elapsed = _clock.UtcNow - started;
                TimeSpan interval;
                lock (_sync)
                    interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds);
                delay = elapsed < interval ? interval - elapsed : interval;
                lock (_sync)
                {
                    if (!_isScanning && _nextHiddenRecheckAt is { } next)
                    {
                        var untilRecheck = next - _clock.UtcNow;
                        if (untilRecheck > TimeSpan.Zero && untilRecheck < delay)
                            delay = untilRecheck;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task<ProbeResult> ProbeSafelyAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await _probe.ProbeAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(ProbeStatus.Error);
        }
    }

    private bool IsFavorite(int id) => _settings.FavoriteWorldIds.Contains(id);

    private bool IsTracked(WorldState world) => world.World is not null && _trackedWorldIds.Contains(world.Id);

    // Called only after discovery, a hidden-world review, or an explicit selection change.
    // Regular live readings cannot accidentally start polling a hidden world.
    private void SelectTrackedWorlds()
    {
        var selected = _worlds.Values.Where(w => w.World is not null && IsFavorite(w.Id)).Select(w => w.Id).ToHashSet();
        int automaticSlots = MaximumAutomaticWorlds;
        var candidates = _worlds.Values.Where(w => w.World is not null && !IsFavorite(w.Id) && !w.Removed &&
                (_manualTracking.TryGetValue(w.Id, out bool enabled) ? enabled : w.AutomaticallyEligible))
            .OrderByDescending(w => _manualTracking.GetValueOrDefault(w.Id))
            .ThenBy(w => w.Result?.Status == ProbeStatus.Success ? w.Result.RoundTripMilliseconds ?? long.MaxValue : long.MaxValue)
            .ThenBy(w => w.Id).Take(automaticSlots);
        selected.UnionWith(candidates.Select(w => w.Id));
        foreach (var world in _worlds.Values)
        {
            if (selected.Contains(world.Id) != _trackedWorldIds.Contains(world.Id))
            {
                world.BadReadings = 0;
                world.Generation++;
            }
        }
        _trackedWorldIds = selected;
    }

    private void AddMissingFavorites()
    {
        foreach (int id in _settings.FavoriteWorldIds)
            _worlds.TryAdd(id, new WorldState(id, null));
    }

    private void SetNotice(string? notice)
    {
        lock (_sync)
            _catalogueNotice = notice;
        NotifyChanged();
    }

    private void SetSettingsNotice(string? notice)
    {
        lock (_sync)
            _settingsNotice = notice;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        lock (_sync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        lock (_sync)
            _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_startTask is not null)
                await _startTask.ConfigureAwait(false);
            if (_runner is not null)
                await _runner.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
        await _saveGate.WaitAsync().ConfigureAwait(false);
        _saveGate.Release();
        _lifetime.Dispose();
    }
}
