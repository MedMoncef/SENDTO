using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Windows;

namespace DropRoom;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;

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
            catch (TimeoutException) { }
            Shutdown();
            return;
        }
        var mode = e.Args.Length > 0 ? e.Args[0] : string.Empty;
        var path = e.Args.Length > 1 ? e.Args[1] : null;
        var window = new MainWindow(mode, path);
        MainWindow = window;
        window.Show();
        _ = ListenForCommandsAsync(window);
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
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
