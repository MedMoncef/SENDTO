using System.Security.Cryptography;

namespace Core;

public static class RoomKey
{
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string CreateRoomId(string roomKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomKey);
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(roomKey));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    public static string GenerateDeviceId() => Guid.NewGuid().ToString("N");
}
