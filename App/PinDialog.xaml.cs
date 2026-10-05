using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DropRoom;

public partial class PinDialog : Window
{
    public string Pin => PinBox.Password;

    public PinDialog(string fileName)
    {
        InitializeComponent();
        Description.Text = $"PIN for {fileName}";
        PinBox.Focus();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (Pin.Length is < 4 or > 6 || !Pin.All(char.IsDigit))
        {
            MessageBox.Show("Use a PIN with 4 to 6 digits.", "Invalid PIN", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
