using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DropRoom;

public partial class SettingsWindow : Window
{
    private readonly UserSettings settings;

    public SettingsWindow(UserSettings current)
    {
        InitializeComponent();
        settings = current;
        DisplayNameBox.Text = settings.DisplayName;
        RoomKeyBox.Text = settings.RoomKey;
        SaveFolderBox.Text = settings.DefaultSaveFolder;
        ExpiryBox.Text = settings.ExpiryHours.ToString();
        BackendUrlBox.Text = settings.BackendUrl;
        TransportBox.SelectedIndex = settings.DefaultTransport == "Cloud relay" ? 1 : 0;
    }

    private void CreateRoom_Click(object sender, RoutedEventArgs e) => RoomKeyBox.Text = RoomKeyGenerator.Generate();
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Rotating the key hides existing room transfers. Continue?", "Rotate room key",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            RoomKeyBox.Text = RoomKeyGenerator.Generate();
    }

    private void JoinRoom_Click(object sender, RoutedEventArgs e)
    {
        var key = Microsoft.VisualBasic.Interaction.InputBox("Paste the room key:", "Join room", RoomKeyBox.Text);
        if (!string.IsNullOrWhiteSpace(key))
            RoomKeyBox.Text = key.Trim();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = SaveFolderBox.Text };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SaveFolderBox.Text = dialog.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) || string.IsNullOrWhiteSpace(RoomKeyBox.Text))
        {
            MessageBox.Show("Display name and room key are required.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(ExpiryBox.Text, out var hours) || hours is < 1 or > 24)
        {
            MessageBox.Show("Expiry must be between 1 and 24 hours.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        settings.DisplayName = DisplayNameBox.Text.Trim();
        settings.RoomKey = RoomKeyBox.Text.Trim();
        settings.DefaultSaveFolder = SaveFolderBox.Text.Trim();
        settings.ExpiryHours = hours;
        settings.BackendUrl = BackendUrlBox.Text.Trim().TrimEnd('/');
        settings.DefaultTransport = (TransportBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()
            ?? "Local network (LAN)";
        if (settings.DefaultTransport == "Cloud relay" &&
            (!Uri.TryCreate(settings.BackendUrl, UriKind.Absolute, out var backend) ||
             backend.Scheme is not ("http" or "https")))
        {
            MessageBox.Show("Enter a valid http:// or https:// cloud backend URL.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SettingsStore.Save(settings);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
