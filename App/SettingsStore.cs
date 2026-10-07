using System.Text.Json;
using System.IO;
using Core;

namespace DropRoom;

public sealed class UserSettings
{
    public string RoomKey { get; set; } = RoomKeyGenerator.Generate();
    public string DisplayName { get; set; } = Environment.UserName;
    public string DeviceId { get; set; } = Core.RoomKey.GenerateDeviceId();
    public int ExpiryHours { get; set; } = 1;
    public string DefaultSaveFolder { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string BackendUrl { get; set; } = "";
    public string DefaultTransport { get; set; } = "Office sharing";
}

internal static class SettingsStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropRoom", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path)) ?? new UserSettings();
                if (settings.DefaultTransport is "Cloud relay" or "Cloud")
                    settings.DefaultTransport = "Public send";
                else if (settings.DefaultTransport is "Local network" or "Local network (LAN)")
                    settings.DefaultTransport = "Office sharing";
                return settings;
            }
        }
        catch (JsonException) { }
        return new UserSettings();
    }

    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class RoomKeyGenerator
{
    public static string Generate() => Core.RoomKey.Generate();
}
