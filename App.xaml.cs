using System.Threading;
using System.Windows;

namespace MetroCharms;

public partial class App : Application
{
    Mutex _mutex;
    MainWindow _win;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "MetroCharmsBar_SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }
        base.OnStartup(e);
        _win = new MainWindow(); // tidak di-Show: baru muncul saat hotspot disentuh
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _win?.Cleanup();
        base.OnExit(e);
    }
}
