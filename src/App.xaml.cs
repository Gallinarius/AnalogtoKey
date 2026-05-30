using System.Threading;
using System.Windows;

namespace AnalogtoKey;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "AnalogtoKey_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "AnalogtoKey is already running.\nCheck the system tray.",
                "Already running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
