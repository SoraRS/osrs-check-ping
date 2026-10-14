using OsrsCheckPing.Core;

namespace OsrsCheckPing.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OsrsCheckPing/1.0");
        var engine = new MonitoringEngine(new WorldCatalogueClient(http), new IcmpLatencyProbe(),
            new JsonSettingsStore(JsonSettingsStore.DefaultDirectory));
        Application.Run(new MainForm(engine));
    }
}
