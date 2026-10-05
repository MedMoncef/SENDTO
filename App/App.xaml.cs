using System.Configuration;
using System.Data;
using System.Windows;

namespace DropRoom;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mode = e.Args.Length > 0 ? e.Args[0] : string.Empty;
        var path = e.Args.Length > 1 ? e.Args[1] : null;
        var window = new MainWindow(mode, path);
        MainWindow = window;
        window.Show();
    }
}
