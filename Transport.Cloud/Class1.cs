using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core;
using Crypto;

namespace Transport.Cloud;

public sealed class CloudTransportOptions
{
    public required RoomSettings Room { get; init; }
    public required DeviceSettings Device { get; init; }
    public required Uri BackendUrl { get; init; }
}

public sealed class CloudTransport : IRoomTransport
{
    private readonly CloudTransportOptions options;
    private readonly HttpClient client;

    public CloudTransport(CloudTransportOptions options, HttpClient? client = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        this.client = client;
    }

    public async Task<IReadOnlyList<DeviceSettings>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Array.Empty<DeviceSettings>();

    public async Task<IReadOnlyList<TransferDescriptor>> ListAsync(
        string roomId, string pin, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(
            Endpoint(roomId) + "?pin=" + Uri.EscapeDataString(pin) +
            "&deviceId=" + Uri.EscapeDataString(options.Device.DeviceId), cancellationToken);
        await EnsureSuccess(response);
        var result = await response.Content.ReadFromJsonAsync<List<TransferDescriptor>>(
            cancellationToken: cancellationToken);
        return result ?? new List<TransferDescriptor>();
    }

    public async Task<TransferDescriptor> SendAsync(
        string roomId, string pin, SendRequest request, CancellationToken cancellationToken = default)
    {
        var metadata = new TransferMetadata(
            Guid.NewGuid().ToString("N"), request.FileName,
            request.Content.CanSeek ? request.Content.Length : -1, request.ContentType,
            request.SenderDeviceId, request.RecipientDeviceId, DateTimeOffset.UtcNow,
            request.ExpiresAt ?? options.Room.ExpiresAt, roomId, Math.Max(1, request.RecipientCount),
            request.Note, request.Visibility, request.AllowedDeviceIds);
        var encrypted = await FileEncryptor.EncryptAsync(request.Content, metadata, cancellationToken);
        var payload = new CloudTransfer(metadata, FileEncryptor.ProtectFileKey(encrypted.Key, pin, roomId),
            encrypted.Nonce, encrypted.EncryptedMetadata, encrypted.Ciphertext);
        using var response = await client.PostAsJsonAsync(Endpoint(roomId) + "?pin=" +
            Uri.EscapeDataString(pin), payload, cancellationToken);
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<TransferDescriptor>(cancellationToken: cancellationToken)
            ?? throw new IOException("Cloud backend returned no transfer descriptor.");
    }

    public async Task<ReceivedTransfer> FetchAsync(
        string roomId, string pin, string transferId, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(Endpoint(roomId) + "/" +
            Uri.EscapeDataString(transferId) + "?pin=" + Uri.EscapeDataString(pin) +
            "&deviceId=" + Uri.EscapeDataString(options.Device.DeviceId), cancellationToken);
        await EnsureSuccess(response);
        var payload = await response.Content.ReadFromJsonAsync<CloudTransfer>(cancellationToken: cancellationToken)
            ?? throw new FileNotFoundException("Cloud transfer not found.");
        var key = FileEncryptor.UnprotectFileKey(payload.ProtectedKey, pin, roomId);
        var encrypted = new EncryptedFile(payload.EncryptedMetadata, payload.Ciphertext, key, payload.Nonce);
        var metadata = FileEncryptor.DecryptMetadata(encrypted, transferId);
        return new ReceivedTransfer(metadata,
            new MemoryStream(FileEncryptor.Decrypt(encrypted, transferId), writable: false));
    }

    public async Task RevokeAsync(string roomId, string transferId, CancellationToken cancellationToken = default)
    {
        using var response = await client.DeleteAsync(Endpoint(roomId) + "/" +
            Uri.EscapeDataString(transferId), cancellationToken);
        await EnsureSuccess(response);
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }

    private string Endpoint(string roomId) => new Uri(options.BackendUrl,
        "v1/rooms/" + Uri.EscapeDataString(roomId) + "/transfers").ToString();

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Cloud backend returned {(int)response.StatusCode} ({detail}).");
    }

    private sealed record CloudTransfer(TransferMetadata Metadata, byte[] ProtectedKey, byte[] Nonce,
        byte[] EncryptedMetadata, byte[] Ciphertext);
}
