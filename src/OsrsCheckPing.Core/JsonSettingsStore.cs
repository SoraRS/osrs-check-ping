using System.Text.Json;

namespace OsrsCheckPing.Core;

public sealed class JsonSettingsStore(string directory) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsrsCheckPing");

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken) =>
        (await ReadAsync<AppSettings>("settings.json", cancellationToken).ConfigureAwait(false)
            ?? new AppSettings()).Sanitize();

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
