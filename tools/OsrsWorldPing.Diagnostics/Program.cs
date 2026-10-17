using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OsrsWorldPing.Core;

// Explicit developer investigation, separate from the app's once-per-world discovery and scheduled monitoring.
string destination = args.FirstOrDefault() ?? "artifacts/world-diagnostics.json";
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; deadline.Cancel(); };
using var http = new HttpClient();
var probe = new IcmpLatencyProbe();
var rows = new ConcurrentDictionary<int, WorldDiagnostic>();
string? error = null;
var started = DateTimeOffset.UtcNow;
var loopback = await probe.ProbeAsync("127.0.0.1", deadline.Token);
try
{
    var worlds = await new WorldCatalogueClient(http).FetchAsync(deadline.Token);
    foreach (var world in worlds) rows[world.Id] = new(world);
    for (int attempt = 0; attempt < 3; attempt++)
    {
        if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(5), deadline.Token);
        await Parallel.ForEachAsync(rows.Values.Where(row => row.Readings.LastOrDefault()?.Status != ProbeStatus.Success),
            new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = deadline.Token },
            async (row, token) => row.Readings.Add(await probe.ProbeAsync(row.World.Host, token)));
    }
    if (OperatingSystem.IsWindows())
        await Parallel.ForEachAsync(rows.Values.Where(row => row.Readings.LastOrDefault()?.Status != ProbeStatus.Success),
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = deadline.Token }, async (row, token) =>
            {
                string? address = row.Readings.LastOrDefault()?.Diagnostic?.Address;
                if (address is null) return;
                var start = new ProcessStartInfo("ping.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (string argument in new[] { "-n", "1", "-w", "2000", address }) start.ArgumentList.Add(argument);
                using var native = Process.Start(start)!;
                try
                {
                    Task<string> output = native.StandardOutput.ReadToEndAsync(token);
                    Task<string> stderr = native.StandardError.ReadToEndAsync(token);
                    await native.WaitForExitAsync(token);
                    row.NativeComparison = $"exit {native.ExitCode}: {await output} {await stderr}";
                }
                finally { if (!native.HasExited) native.Kill(); }
            });
}
catch (Exception exception) { error = $"{exception.GetType().Name}: {exception.Message}"; }
var summary = rows.Values.GroupBy(row => row.Readings.LastOrDefault()?.Status.ToString() ?? "Not checked").ToDictionary(g => g.Key, g => g.Count());
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(new
{
    Started = started, Completed = DateTimeOffset.UtcNow, OperatingSystem = RuntimeInformation.OSDescription,
    Runtime = RuntimeInformation.FrameworkDescription, Loopback = loopback, Error = error, Summary = summary,
    Interpretation = "No ICMP reply alone establishes neither a game-world outage nor ICMP filtering. Compare repeated readings, native ping, and catalogue population; local/network conditions can affect results.",
    Worlds = rows.Values.OrderBy(row => row.World.Id)
}, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
Console.WriteLine(JsonSerializer.Serialize(new { Worlds = rows.Count, Summary = summary, Error = error }));
if (error is not null) Environment.ExitCode = 1;

internal sealed record WorldDiagnostic(WorldInfo World)
{
    public List<ProbeResult> Readings { get; } = [];
    public string? NativeComparison { get; set; }
}
