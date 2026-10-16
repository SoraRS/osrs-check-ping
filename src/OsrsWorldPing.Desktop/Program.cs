using System.Windows;
using OsrsWorldPing.Core;

namespace OsrsWorldPing.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/OsrsWorldPing;component/Theme.xaml", UriKind.Relative)
        });
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OsrsWorldPing/1.3");
        var engine = new MonitoringEngine(new WorldCatalogueClient(http), new IcmpLatencyProbe(),
            new JsonSettingsStore(JsonSettingsStore.DefaultDirectory, JsonSettingsStore.LegacyDirectory));
        application.Run(new MainWindow(engine));
    }
}
