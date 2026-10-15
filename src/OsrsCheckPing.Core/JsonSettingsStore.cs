using System.Text.Json;

namespace OsrsCheckPing.Core;

public sealed class JsonSettingsStore(string directory) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsrsCheckPing");

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return new AppSettings();
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
        var settings = document.RootElement.Deserialize<AppSettings>(JsonOptions)
            ?? throw new InvalidDataException("settings.json is empty.");
        bool legacy = !document.RootElement.TryGetProperty(nameof(AppSettings.SchemaVersion), out var version) ||
            version.GetInt32() < AppSettings.CurrentSchemaVersion;
        settings = (settings with
        {
            SchemaVersion = Math.Max(settings.SchemaVersion, AppSettings.CurrentSchemaVersion),
            LatencyCutoffMs = legacy && settings.LatencyCutoffMs == 60 ? 30 : settings.LatencyCutoffMs
        }).Sanitize();
        // Commit the marker before returning, including when the user never opens settings.
        if (legacy)
        {
            try { await SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException)
            { throw new SettingsMigrationSaveException(settings, error); }
        }
        return settings;
    }

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken) =>
        WriteAsync("settings.json", settings.Sanitize(), cancellationToken);

    public async Task<WorldCatalogue?> LoadCatalogueAsync(CancellationToken cancellationToken)
    {
        var catalogue = await ReadAsync<WorldCatalogue>("worlds.json", cancellationToken).ConfigureAwait(false);
        if (catalogue is not null && (catalogue.Worlds is null || catalogue.Worlds.Count == 0 ||
            catalogue.Worlds.Any(w => w is null || w.Id is <= 0 or > ushort.MaxValue ||
                Uri.CheckHostName(w.Host) == UriHostNameType.Unknown || w.Activity is null) ||
            catalogue.Worlds.Select(w => w.Id).Distinct().Count() != catalogue.Worlds.Count))
            throw new InvalidDataException("The saved world catalogue is invalid.");
        return catalogue;
    }

    public Task SaveCatalogueAsync(WorldCatalogue catalogue, CancellationToken cancellationToken) =>
        WriteAsync("worlds.json", catalogue, cancellationToken);

    private async Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"{fileName} is empty.");
    }

    private async Task WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporary = Path.Combine(directory, fileName + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, Path.Combine(directory, fileName), overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }
}

// A readable legacy file must not lose its preferences just because the upgraded file cannot be written.
public sealed class SettingsMigrationSaveException(AppSettings settings, Exception innerException)
    : IOException("The upgraded settings could not be saved.", innerException)
{
    public AppSettings Settings { get; } = settings;
}
