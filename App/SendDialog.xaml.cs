using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Core;
using MessageBox = System.Windows.MessageBox;

namespace DropRoom;

public partial class SendDialog : Window
{
    private readonly string filePath;
    private readonly UserSettings settings;
    private readonly IRoomTransport transport;
    public TransferDescriptor? Result { get; private set; }

    public SendDialog(string path, UserSettings userSettings, IRoomTransport roomTransport)
    {
        InitializeComponent();
        filePath = path;
        settings = userSettings;
        transport = roomTransport;
        var file = new FileInfo(path);
        FileSummary.Text = $"{file.Name}  •  {FormatSize(file.Length)}";
        NameBox.Text = file.Name;
        PinBox.Focus();
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var pin = (VisiblePinBox.Visibility == Visibility.Visible
            ? VisiblePinBox.Text
            : PinBox.Password).Trim();
        if (pin.Length is < 4 or > 6 || !pin.All(char.IsDigit))
        {
            MessageBox.Show("Use a PIN with 4 to 6 digits.", "PIN required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(RecipientCountBox.Text, out var recipients) || recipients is < 1 or > 100)
        {
            MessageBox.Show("Recipients must be a number from 1 to 100.", "Check recipients",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var info = new FileInfo(filePath);
        if (info.Length > 50 * 1024 * 1024)
        {
            MessageBox.Show("Files are limited to 50 MB in the MVP.", "File too large",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var roomId = Core.RoomKey.CreateRoomId(settings.RoomKey);
            var room = new RoomSettings(roomId, "My room", pin,
                DateTimeOffset.UtcNow.AddHours(settings.ExpiryHours));
            var fileName = PreserveSourceExtension(NameBox.Text, info.Extension);
            await using var stream = File.OpenRead(filePath);
            Result = await transport.SendAsync(roomId, pin, new SendRequest(
                fileName, stream, "application/octet-stream", settings.DeviceId,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(settings.ExpiryHours),
                RecipientCount: recipients, Note: NoteBox.Text.Trim(),
                Visibility: OfficeOnlyRadio.IsChecked == true ? "Office only" : "Anyone in the room"));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The file could not be shared.\n\n{ex.Message}", "Send failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void PinBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(System.Windows.DataFormats.UnicodeText) as string;
        if (string.IsNullOrEmpty(text) || text.Any(character => !char.IsDigit(character)))
            e.CancelCommand();
    }

    private void ShowPin_Click(object sender, RoutedEventArgs e)
    {
        if (VisiblePinBox.Visibility == Visibility.Visible)
        {
            PinBox.Password = VisiblePinBox.Text;
            VisiblePinBox.Visibility = Visibility.Collapsed;
            PinBox.Visibility = Visibility.Visible;
            ShowPinButton.ToolTip = "Show PIN";
            PinBox.Focus();
        }
        else
        {
            VisiblePinBox.Text = PinBox.Password;
            PinBox.Visibility = Visibility.Collapsed;
            VisiblePinBox.Visibility = Visibility.Visible;
            ShowPinButton.ToolTip = "Hide PIN";
            VisiblePinBox.Focus();
            VisiblePinBox.CaretIndex = VisiblePinBox.Text.Length;
        }
    }

    private void VisiblePinBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (VisiblePinBox.Text.Any(character => !char.IsDigit(character)))
            VisiblePinBox.Text = new string(VisiblePinBox.Text.Where(char.IsDigit).ToArray());
    }

    private static string PreserveSourceExtension(string name, string sourceExtension)
    {
        name = Path.GetFileName(name.Trim());
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Enter a display name for the file.");

        var baseName = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrEmpty(sourceExtension) || name.EndsWith(sourceExtension, StringComparison.OrdinalIgnoreCase)
            ? name
            : baseName + sourceExtension;
    }

    private static string FormatSize(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024d:0.0} KB" : $"{bytes / (1024d * 1024):0.0} MB";
}
