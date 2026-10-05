using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Core;
using Transport.Lan;
using MessageBox = System.Windows.MessageBox;

namespace DropRoom;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TransferRow> transfers = new();
    private readonly ICollectionView transferView;
    private readonly UserSettings settings;
    private readonly LanTransport transport;
    private string destinationFolder;

    public MainWindow(string mode = "", string? path = null)
    {
        InitializeComponent();
        settings = SettingsStore.Load();
        destinationFolder = settings.DefaultSaveFolder;
        var roomId = Core.RoomKey.CreateRoomId(settings.RoomKey);
        transport = new LanTransport(new LanTransportOptions
        {
            Room = new RoomSettings(roomId, "My room", "local"),
            Device = new DeviceSettings(settings.DeviceId, settings.DisplayName, roomId)
        });
        TransfersList.ItemsSource = transfers;
        transferView = new ListCollectionView(transfers);
        transferView.Filter = item => item is TransferRow row && row.IsVisible;
        TransfersList.ItemsSource = transferView;
        RoomStatusText.Text = $"{settings.DisplayName}  •  room {roomId[..8]}  •  LAN ready";
        if (mode.Equals("send", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            _ = OpenSendAsync(path);
        if (mode.Equals("receive", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            destinationFolder = path;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            destinationFolder = settings.DefaultSaveFolder;
            RoomStatusText.Text = $"{settings.DisplayName}  •  room {Core.RoomKey.CreateRoomId(settings.RoomKey)[..8]}  •  LAN ready";
        }
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
