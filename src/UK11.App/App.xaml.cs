using System.Windows;
using System.IO;
namespace UK11.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 2 && e.Args[0] == "--ui-smoke")
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            DispatcherUnhandledException += (_, failure) =>
            {
                try { File.WriteAllText(Path.GetFullPath(e.Args[1]) + ".error.txt", failure.Exception.ToString()); }
                finally { failure.Handled = true; Shutdown(1); }
            };
        }
        base.OnStartup(e);
    }
}
