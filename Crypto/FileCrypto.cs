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
        return hmac.ComputeHash(Encoding.UTF8.GetBytes("room:" + roomId)).Concat(salt).Take(32).ToArray();
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
    public static void Authorize(RoomSettings room, string pin, TransferMetadata metadata, string? requestingDeviceId = null)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(room.Pin), Encoding.UTF8.GetBytes(pin)))
            throw new UnauthorizedAccessException("Invalid room PIN.");
        if (!string.Equals(room.RoomId, metadata.RoomId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Transfer belongs to another room.");
        if (room.ExpiresAt <= DateTimeOffset.UtcNow || metadata.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Room or transfer has expired.");
        if (room.RequireRecipient && !string.Equals(metadata.RecipientDeviceId, requestingDeviceId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Transfer is addressed to another device.");
    }
}
