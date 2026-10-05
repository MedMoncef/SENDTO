using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Core;
using MessageBox = System.Windows.MessageBox;

namespace DropRoom;

public partial class ReceiveDialog : Window
{
    private readonly UserSettings settings;
    private readonly IRoomTransport transport;
    private readonly string initialFolder;
    private readonly ObservableCollection<ReceiveRow> rows = new();
    private readonly ICollectionView rowsView;

    public string? SelectedFolder { get; private set; }

    public ReceiveDialog(UserSettings settings, IRoomTransport transport, string folder)
    {
        InitializeComponent();
        this.settings = settings;
        this.transport = transport;
        initialFolder = folder;
        rowsView = new ListCollectionView(rows);
        rowsView.Filter = item => item is ReceiveRow row && row.IsVisible;
        TransfersList.ItemsSource = rowsView;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var roomId = RoomKey.CreateRoomId(settings.RoomKey);
            var descriptors = await transport.ListAsync(roomId, "local");
            rows.Clear();
            foreach (var descriptor in descriptors)
                rows.Add(new ReceiveRow(descriptor));
            StatusText.Text = rows.Count == 0 ? "No shared files are currently available." : $"{rows.Count} file(s) available";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load shared files: {ex.Message}";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = SearchBox.Text.Trim();
        foreach (var row in rows)
            row.IsVisible = string.IsNullOrWhiteSpace(filter) ||
                row.Metadata.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        rowsView.Refresh();
    }

    private async void TransfersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransfersList.SelectedItem is not ReceiveRow row)
            return;
        TransfersList.SelectedItem = null;
        var pinDialog = new PinDialog(row.Metadata.FileName) { Owner = this };
        if (pinDialog.ShowDialog() != true)
            return;
        try
        {
            var folder = initialFolder;
            if (!Directory.Exists(folder))
            {
                using var picker = new System.Windows.Forms.FolderBrowserDialog();
                if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                folder = picker.SelectedPath;
            }
            var received = await transport.FetchAsync(RoomKey.CreateRoomId(settings.RoomKey),
                pinDialog.Pin, row.Metadata.TransferId);
            var target = Path.Combine(folder, received.Metadata.FileName);
            await using var output = File.Create(target);
            await received.Content.CopyToAsync(output);
            SelectedFolder = folder;
            MessageBox.Show($"Saved to {target}", "Transfer complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"This transfer could not be unlocked.\n\n{ex.Message}", "Receive failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class ReceiveRow
    {
        public ReceiveRow(TransferDescriptor descriptor)
        {
            Metadata = descriptor.Metadata;
            SizeText = Metadata.Length < 1024 * 1024
                ? $"{Metadata.Length / 1024d:0.0} KB"
                : $"{Metadata.Length / (1024d * 1024):0.0} MB";
        }

        public TransferMetadata Metadata { get; }
        public string SizeText { get; }
        public bool IsVisible { get; set; } = true;
    }
}
