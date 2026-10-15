using System.Windows;
using OsrsCheckPing.Core;

namespace OsrsCheckPing.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/OsrsCheckPing;component/Theme.xaml", UriKind.Relative)
        });
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OsrsCheckPing/1.2");
        var engine = new MonitoringEngine(new WorldCatalogueClient(http), new IcmpLatencyProbe(),
            new JsonSettingsStore(JsonSettingsStore.DefaultDirectory));
        application.Run(new MainWindow(engine));
    }
}
