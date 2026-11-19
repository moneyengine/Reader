using System.Configuration;
using System.Data;
using System.Windows;

namespace LightReader;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // NotifyIcon must not replace WPF's dispatcher synchronization context.
        System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=false;
        base.OnStartup(e);
    }
}

