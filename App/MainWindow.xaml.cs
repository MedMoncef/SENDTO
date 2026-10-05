using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows;

namespace DropRoom;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<TransferRow> transfers = new();
    private readonly ICollectionView transferView;

    public MainWindow(string mode = "", string? path = null)
    {
        InitializeComponent();
        TransfersList.ItemsSource = transfers;
        transferView = new ListCollectionView(transfers);
        transferView.Filter = item => item is TransferRow row && row.IsVisible;
        RoomStatusText.Text = "Ready. Configure a room key in Settings to start sharing.";
        if (mode.Equals("send", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
            StageFile(path);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Room settings will be available in the next setup step.", "Settings",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a file to send" };
        if (dialog.ShowDialog() != true)
            return;

        StageFile(dialog.FileName);
        EmptyStateText.Text = "The file is staged. Configure a PIN and press Send in the transfer dialog.";
    }

    private void StageFile(string path)
    {
        if (!File.Exists(path))
            return;

        var name = Path.GetFileName(path);
        transfers.Add(new TransferRow(name, Environment.UserName, new FileInfo(path).Length,
            DateTimeOffset.Now.AddHours(1), "Ready to send"));
        EmptyStateText.Text = "The file is staged. Configure a PIN and press Send in the transfer dialog.";
    }

    private void Receive_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Choose a file from the list, then enter its PIN to receive it here.",
            "Receive here", MessageBoxButton.OK, MessageBoxImage.Information);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = SearchBox.Text.Trim();
        foreach (var item in TransfersList.Items)
            if (item is TransferRow row)
                row.IsVisible = string.IsNullOrWhiteSpace(filter) ||
                    row.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        transferView.Refresh();
    }

    private void TransfersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransfersList.SelectedItem is TransferRow row)
            MessageBox.Show($"Enter the PIN for '{row.DisplayName}' to download it.",
                "PIN required", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private sealed class TransferRow
    {
        public TransferRow(string displayName, string senderName, long size, DateTimeOffset expires, string status)
        {
            DisplayName = displayName;
            SenderName = senderName;
            SizeText = FormatSize(size);
            ExpiresText = expires.LocalDateTime.ToString("g");
            Status = status;
        }

        public string DisplayName { get; }
        public string SenderName { get; }
        public string SizeText { get; }
        public string ExpiresText { get; }
        public string Status { get; }
        public bool IsVisible { get; set; } = true;

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024d:0.0} KB",
            _ => $"{bytes / (1024d * 1024):0.0} MB"
        };
    }
}