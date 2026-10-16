using System.Text.Json;

namespace OsrsWorldPing.Core;

public sealed class JsonSettingsStore(string directory, string? legacyDirectory = null) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsrsWorldPing");
    // The old name is retained only to import existing installations without losing saved choices.
    public static string LegacyDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsrsCheckPing");

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        string path = ReadPath("settings.json");
        if (!File.Exists(path))
        {
            var defaults = new AppSettings();
            try { await ImportCatalogueAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException)
            { throw new SettingsMigrationSaveException(defaults, error); }
            return defaults;
        }
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
        if (legacy || path != Path.Combine(directory, "settings.json"))
        {
            try { await SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException)
            { throw new SettingsMigrationSaveException(settings, error); }
        }
        try { await ImportCatalogueAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        { throw new SettingsMigrationSaveException(settings, error); }
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
        string path = ReadPath(fileName);
        if (!File.Exists(path))
            return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"{fileName} is empty.");
    }

    private string ReadPath(string fileName)
    {
        string current = Path.Combine(directory, fileName);
        return File.Exists(current) || legacyDirectory is null ? current : Path.Combine(legacyDirectory, fileName);
    }

    private async Task ImportCatalogueAsync(CancellationToken cancellationToken)
    {
        if (legacyDirectory is null) return;
        string destination = Path.Combine(directory, "worlds.json"), source = Path.Combine(legacyDirectory, "worlds.json");
        if (File.Exists(destination) || !File.Exists(source)) return;
        Directory.CreateDirectory(directory);
        string temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            try { File.Move(temporary, destination, overwrite: false); }
            catch (IOException) when (File.Exists(destination)) { } // A concurrent startup must never replace newer data.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
