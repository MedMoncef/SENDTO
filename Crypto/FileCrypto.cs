using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core;
using Sodium;

namespace Crypto;

public sealed record EncryptedFile(
    byte[] EncryptedMetadata,
    byte[] Ciphertext,
    byte[] Key,
    byte[] Nonce);

public static class FileEncryptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<EncryptedFile> EncryptAsync(
        Stream input, TransferMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var plain = await ReadAllAsync(input, cancellationToken);
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
        var associatedData = Encoding.UTF8.GetBytes(metadata.TransferId);
        var ciphertext = SecretAeadXChaCha20Poly1305.Encrypt(plain, associatedData, nonce, key);
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions);
        var metadataNonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
        var encryptedMetadata = metadataNonce.Concat(
            SecretAeadXChaCha20Poly1305.Encrypt(metadataBytes, associatedData, metadataNonce, key)).ToArray();
        CryptographicOperations.ZeroMemory(plain);
        return new(encryptedMetadata, ciphertext, key, nonce);
    }

    public static byte[] Decrypt(EncryptedFile encrypted, string transferId)
    {
        var associatedData = Encoding.UTF8.GetBytes(transferId);
        var metadataNonce = encrypted.EncryptedMetadata[..24];
        var metadata = SecretAeadXChaCha20Poly1305.Decrypt(
            encrypted.EncryptedMetadata[24..], associatedData, metadataNonce, encrypted.Key);
        return SecretAeadXChaCha20Poly1305.Decrypt(encrypted.Ciphertext, associatedData, encrypted.Nonce, encrypted.Key);
    }

    public static TransferMetadata DecryptMetadata(EncryptedFile encrypted, string transferId)
    {
        var associatedData = Encoding.UTF8.GetBytes(transferId);
        var nonce = encrypted.EncryptedMetadata[..24];
        var bytes = SecretAeadXChaCha20Poly1305.Decrypt(
            encrypted.EncryptedMetadata[24..], associatedData, nonce, encrypted.Key);
        return JsonSerializer.Deserialize<TransferMetadata>(bytes, JsonOptions)
            ?? throw new CryptographicException("Invalid encrypted metadata.");
    }

    public static byte[] DeriveRoomKey(string pin, string roomId, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);
        ArgumentException.ThrowIfNullOrEmpty(roomId);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pin));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes("DropRoom room:" + roomId)
            .Concat(salt).ToArray());
    }

    public static byte[] ProtectFileKey(byte[] fileKey, string roomPin, string roomId)
    {
        var roomKey = DeriveRoomKey(roomPin, roomId, Array.Empty<byte>());
        var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
        var protectedKey = SecretAeadXChaCha20Poly1305.Encrypt(fileKey,
            Encoding.UTF8.GetBytes(roomId), nonce, roomKey);
        return nonce.Concat(protectedKey).ToArray();
    }

    public static byte[] UnprotectFileKey(byte[] protectedKey, string roomPin, string roomId)
    {
        if (protectedKey.Length <= 24)
            throw new CryptographicException("Invalid protected file key.");
        var roomKey = DeriveRoomKey(roomPin, roomId, Array.Empty<byte>());
        return SecretAeadXChaCha20Poly1305.Decrypt(protectedKey[24..],
            Encoding.UTF8.GetBytes(roomId), protectedKey[..24], roomKey);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }
}

public static class TransferAuthorizer
{
    public static void AuthorizeRoomPin(RoomSettings room, string pin, string roomId)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(room.Pin), Encoding.UTF8.GetBytes(pin)))
            throw new UnauthorizedAccessException("Invalid room PIN.");
        if (!string.Equals(room.RoomId, roomId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Transfer belongs to another room.");
        if (room.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Room has expired.");
    }

    public static void Authorize(RoomSettings room, string pin, TransferMetadata metadata, string? requestingDeviceId = null)
    {
        AuthorizeRoomPin(room, pin, metadata.RoomId);
        if (metadata.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Room or transfer has expired.");
        if (room.RequireRecipient && !string.Equals(metadata.RecipientDeviceId, requestingDeviceId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Transfer is addressed to another device.");
    }
}
