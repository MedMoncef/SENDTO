using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DropRoom;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;
    private bool ownsInstanceMutex;
    private Forms.NotifyIcon? trayIcon;
    private MainWindow? mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instanceMutex = new Mutex(true, "DropRoom.SingleInstance", out var isOwner);
        if (!isOwner)
        {
            using var client = new NamedPipeClientStream(".", "DropRoom.Commands", PipeDirection.Out);
            try
            {
                client.Connect(500);
                using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: true);
                writer.WriteLine(string.Join("\t", e.Args.Select(arg => arg.Replace("\t", " "))));
                writer.Flush();
            }
            catch (Exception) { }
            instanceMutex.Dispose();
            instanceMutex = null;
            Shutdown();
            return;
        }
        ownsInstanceMutex = true;
        var mode = e.Args.Length > 0 ? e.Args[0] : string.Empty;
        var path = e.Args.Length > 1 ? e.Args[1] : null;
        var window = new MainWindow(mode, path);
        mainWindow = window;
        MainWindow = window;
        window.Show();
        InitializeTray(window);
        if (!string.IsNullOrWhiteSpace(mode))
            window.HandleCommand(mode, path);
        _ = ListenForCommandsAsync(window);
    }

    private void InitializeTray(MainWindow window)
    {
        trayIcon = new Forms.NotifyIcon
        {
            Text = "DropRoom",
            Visible = true,
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                ?? Drawing.SystemIcons.Application
        };
        trayIcon.DoubleClick += (_, _) =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open DropRoom", null, (_, _) =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit DropRoom", null, (_, _) =>
        {
            window.AllowShutdown();
            Shutdown();
        });
        trayIcon.ContextMenuStrip = menu;
    }

    private static async Task ListenForCommandsAsync(MainWindow window)
    {
        while (true)
        {
            await using var server = new NamedPipeServerStream("DropRoom.Commands", PipeDirection.In,
                1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8);
            var command = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(command))
                continue;
            var args = command.Split('\t');
            window.Dispatcher.Invoke(() =>
            {
                window.Activate();
                window.HandleCommand(args.ElementAtOrDefault(0) ?? "", args.ElementAtOrDefault(1));
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        trayIcon?.Dispose();
        if (ownsInstanceMutex)
            instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
