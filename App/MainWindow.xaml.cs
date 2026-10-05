using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Core;
using Transport.Lan;
using Transport.Cloud;
using MessageBox = System.Windows.MessageBox;

namespace DropRoom;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TransferRow> transfers = new();
    private readonly ICollectionView transferView;
    private readonly UserSettings settings;
    private IRoomTransport transport;
    private string destinationFolder;

    public MainWindow(string mode = "", string? path = null)
    {
        InitializeComponent();
        settings = SettingsStore.Load();
        destinationFolder = settings.DefaultSaveFolder;
        var roomId = Core.RoomKey.CreateRoomId(settings.RoomKey);
        var device = new DeviceSettings(settings.DeviceId, settings.DisplayName, roomId);
        var room = new RoomSettings(roomId, "My room", "local");
        transport = settings.DefaultTransport == "Cloud relay" &&
            Uri.TryCreate(settings.BackendUrl, UriKind.Absolute, out var backend)
            ? new CloudTransport(new CloudTransportOptions { Room = room, Device = device, BackendUrl = backend })
            : new LanTransport(new LanTransportOptions { Room = room, Device = device, Port = 41872 });
        if (transport is LanTransport lan)
        {
            try
            {
                lan.StartAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Local network mode could not start.\n\n{ex.Message}",
                    "DropRoom startup problem", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        TransfersList.ItemsSource = transfers;
        transferView = new ListCollectionView(transfers);
        transferView.Filter = item => item is TransferRow row && row.IsVisible;
        TransfersList.ItemsSource = transferView;
        RoomStatusText.Text = $"{settings.DisplayName}  •  room {roomId[..8]}  •  {settings.DefaultTransport}";
        ModeText.Text = settings.DefaultTransport;
        ModeDescription.Text = settings.DefaultTransport == "Cloud relay"
            ? "  Files use your configured backend and remain encrypted."
            : "  Files stay on your local network and expire automatically.";
        if (mode.Equals("send", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            _ = OpenSendAsync(path);
        if (mode.Equals("receive", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            destinationFolder = path;
    }

    public void HandleCommand(string mode, string? path)
    {
        if (mode.Equals("send", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            _ = OpenSendAsync(path);
        else if (mode.Equals("receive", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
        {
            destinationFolder = path;
            EmptyStateText.Text = $"Receive destination: {destinationFolder}";
            Activate();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _ = RecreateTransportAsync();
            destinationFolder = settings.DefaultSaveFolder;
            RoomStatusText.Text = $"{settings.DisplayName}  •  room {Core.RoomKey.CreateRoomId(settings.RoomKey)[..8]}  •  {settings.DefaultTransport}";
            ModeText.Text = settings.DefaultTransport;
            ModeDescription.Text = settings.DefaultTransport == "Cloud relay"
                ? "  Files use your configured backend and remain encrypted."
                : "  Files stay on your local network and expire automatically.";
        }
    }

    private async Task RecreateTransportAsync()
    {
        await transport.DisposeAsync();
        var roomId = Core.RoomKey.CreateRoomId(settings.RoomKey);
        var room = new RoomSettings(roomId, "My room", "local");
        var device = new DeviceSettings(settings.DeviceId, settings.DisplayName, roomId);
        transport = settings.DefaultTransport == "Cloud relay" &&
            Uri.TryCreate(settings.BackendUrl, UriKind.Absolute, out var backend)
            ? new CloudTransport(new CloudTransportOptions { Room = room, Device = device, BackendUrl = backend })
            : new LanTransport(new LanTransportOptions { Room = room, Device = device, Port = 41872 });
        if (transport is LanTransport lan)
            await lan.StartAsync();
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a file to send" };
        if (dialog.ShowDialog() == true)
            await OpenSendAsync(dialog.FileName);
    }

    private async Task OpenSendAsync(string path)
    {
        if (!File.Exists(path))
            return;
        var dialog = new SendDialog(path, settings, transport) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } descriptor)
        {
            transfers.Add(new TransferRow(descriptor, path, settings.DisplayName));
            EmptyStateText.Text = "Shared securely. Select it to test receiving with the PIN you chose.";
        }
    }

    private void Receive_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = destinationFolder };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            destinationFolder = dialog.SelectedPath;
            EmptyStateText.Text = $"Receive destination: {destinationFolder}";
        }
    }

    private async void TransfersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransfersList.SelectedItem is not TransferRow row)
            return;
        var pinDialog = new PinDialog(row.DisplayName) { Owner = this };
        if (pinDialog.ShowDialog() != true)
            return;
        try
        {
            var roomId = Core.RoomKey.CreateRoomId(settings.RoomKey);
            var received = await transport.FetchAsync(roomId, pinDialog.Pin, row.Metadata.TransferId);
            var target = Path.Combine(destinationFolder, received.Metadata.FileName);
            await using var output = File.Create(target);
            await received.Content.CopyToAsync(output);
            row.Status = "Received";
            row.Note = received.Metadata.Note;
            TransfersList.Items.Refresh();
            MessageBox.Show($"Saved to {target}\n\nNote: {received.Metadata.Note}", "Transfer complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"This transfer could not be unlocked.\n\n{ex.Message}", "Receive failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TransfersList.SelectedItem = null;
        }
    }

    private async void DeleteTransfer_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as System.Windows.Controls.Button)?.Tag is not TransferRow row)
            return;
        if (MessageBox.Show($"Delete '{row.DisplayName}' from this room?", "Delete transfer",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await transport.RevokeAsync(Core.RoomKey.CreateRoomId(settings.RoomKey), row.Metadata.TransferId);
            transfers.Remove(row);
            EmptyStateText.Text = transfers.Count == 0
                ? "No files yet. Send one to get started."
                : "The transfer was deleted.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The transfer could not be deleted.\n\n{ex.Message}", "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = SearchBox.Text.Trim();
        foreach (var item in transfers)
            item.IsVisible = string.IsNullOrWhiteSpace(filter) ||
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        transferView.Refresh();
    }

    private sealed class TransferRow : INotifyPropertyChanged
    {
        public TransferRow(TransferDescriptor descriptor, string sourcePath, string senderName)
        {
            Metadata = descriptor.Metadata;
            SourcePath = sourcePath;
            DisplayName = descriptor.Metadata.FileName;
            SenderName = senderName;
            SizeText = FormatSize(descriptor.Metadata.Length);
            ExpiresText = descriptor.Metadata.ExpiresAt?.LocalDateTime.ToString("g") ?? "No expiry";
            Status = "Available";
            Note = descriptor.Metadata.Note;
        }

        public TransferMetadata Metadata { get; }
        public string SourcePath { get; }
        public string DisplayName { get; }
        public string SenderName { get; }
        public string SizeText { get; }
        public string ExpiresText { get; }
        public string Note { get; set; }
        public bool IsVisible { get; set; } = true;
        private string status = "";
        public string Status { get => status; set { status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
        public event PropertyChangedEventHandler? PropertyChanged;

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024d:0.0} KB",
            _ => $"{bytes / (1024d * 1024):0.0} MB"
        };
    }
}
