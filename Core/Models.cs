namespace Core;

public sealed record RoomSettings(
    string RoomId,
    string Name,
    string Pin,
    DateTimeOffset? ExpiresAt = null,
    bool RequireRecipient = false);

public sealed record DeviceSettings(
    string DeviceId,
    string DisplayName,
    string? RoomId = null,
    string? Endpoint = null);

public sealed record Room(
    RoomSettings Settings,
    IReadOnlyList<DeviceSettings> Devices);

public sealed record TransferMetadata(
    string TransferId,
    string FileName,
    long Length,
    string ContentType,
    string SenderDeviceId,
    string? RecipientDeviceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string RoomId);

public sealed record TransferDescriptor(
    TransferMetadata Metadata,
    Uri FetchUri);

public sealed record SendRequest(
    string FileName,
    Stream Content,
    string ContentType,
    string SenderDeviceId,
    string? RecipientDeviceId = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record ReceivedTransfer(TransferMetadata Metadata, Stream Content);

public interface IRoomTransport : IAsyncDisposable
{
    Task<IReadOnlyList<DeviceSettings>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransferDescriptor>> ListAsync(string roomId, string pin, CancellationToken cancellationToken = default);
    Task<TransferDescriptor> SendAsync(string roomId, string pin, SendRequest request, CancellationToken cancellationToken = default);
    Task<ReceivedTransfer> FetchAsync(string roomId, string pin, string transferId, CancellationToken cancellationToken = default);
}
