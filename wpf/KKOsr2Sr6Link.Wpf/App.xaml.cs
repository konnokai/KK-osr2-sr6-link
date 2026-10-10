using System.Windows;

namespace KKOsr2Sr6Link.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string InstanceName = @"Local\KKOsr2Sr6Link.Wpf";

    // 欄位持有，避免 GC 回收後 Mutex 被釋放、讓第二個實例誤判可以啟動
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, InstanceName + ".Mutex", out bool firstInstance);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Activate");
        if (!firstInstance)
        {
            // 兩個實例會搶同一個 TCP port 跟 serial port，所以只叫醒已開的視窗
            _activateSignal.Set();
            Shutdown();
            return;
        }

        ThreadPool.RegisterWaitForSingleObject(_activateSignal,
            (_, _) => Dispatcher.BeginInvoke(ActivateMainWindow), null, Timeout.Infinite, false);

        base.OnStartup(e);
        new MainWindow().Show();
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not { } w) return;
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Show();
        w.Activate();
    }
}
