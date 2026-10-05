using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core;
using Crypto;

namespace Transport.Lan;

public sealed class LanTransportOptions
{
    public required RoomSettings Room { get; init; }
    public required DeviceSettings Device { get; init; }
    public Uri? PeerUri { get; init; }
    public int Port { get; init; } = 0;
}

public sealed class LanTransport : IRoomTransport
{
    private readonly LanTransportOptions _options;
    private readonly HttpClient _client;
    private readonly Dictionary<string, StoredTransfer> _transfers = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private HttpListener? _listener;
    private Task? _serverTask;

    public LanTransport(LanTransportOptions options, HttpClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public Uri? LocalUri { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null) return Task.CompletedTask;
        _listener = new HttpListener();
        var port = _options.Port == 0 ? GetFreePort() : _options.Port;
        LocalUri = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(LocalUri.ToString());
        _listener.Start();
        _serverTask = Task.Run(() => ServeAsync(_listener, cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DeviceSettings>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (_options.PeerUri is null) return new[] { _options.Device };
        var response = await _client.GetFromJsonAsync<List<DeviceSettings>>(
            new Uri(_options.PeerUri, "v1/discover"), cancellationToken);
        return response is null ? Array.Empty<DeviceSettings>() : response;
    }

    public async Task<IReadOnlyList<TransferDescriptor>> ListAsync(
        string roomId, string pin, CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync<List<TransferDescriptor>>(
            HttpMethod.Get, $"v1/rooms/{Uri.EscapeDataString(roomId)}/transfers?pin={Uri.EscapeDataString(pin)}",
            null, cancellationToken);
        return response is null ? Array.Empty<TransferDescriptor>() : response;
    }

    public async Task<TransferDescriptor> SendAsync(
        string roomId, string pin, SendRequest request, CancellationToken cancellationToken = default)
    {
        var expires = request.ExpiresAt ?? _options.Room.ExpiresAt;
        var metadata = new TransferMetadata(
            Guid.NewGuid().ToString("N"), request.FileName, request.Content.CanSeek ? request.Content.Length : -1,
            request.ContentType, request.SenderDeviceId, request.RecipientDeviceId,
            DateTimeOffset.UtcNow, expires, roomId);
        var encrypted = await FileEncryptor.EncryptAsync(request.Content, metadata, cancellationToken);
        var dto = new WireTransfer(metadata, encrypted.Key, encrypted.Nonce, encrypted.EncryptedMetadata, encrypted.Ciphertext);
        return await RequestAsync<TransferDescriptor>(
            HttpMethod.Post, $"v1/rooms/{Uri.EscapeDataString(roomId)}/transfers?pin={Uri.EscapeDataString(pin)}",
            dto, cancellationToken) ?? throw new IOException("Peer returned no transfer descriptor.");
    }

    public async Task<ReceivedTransfer> FetchAsync(
        string roomId, string pin, string transferId, CancellationToken cancellationToken = default)
    {
        var dto = await RequestAsync<WireTransfer>(
            HttpMethod.Get, $"v1/rooms/{Uri.EscapeDataString(roomId)}/transfers/{Uri.EscapeDataString(transferId)}?pin={Uri.EscapeDataString(pin)}",
            null, cancellationToken) ?? throw new FileNotFoundException("Transfer not found.");
        var encrypted = new EncryptedFile(dto.EncryptedMetadata, dto.Ciphertext, dto.Key, dto.Nonce);
        var metadata = FileEncryptor.DecryptMetadata(encrypted, transferId);
        TransferAuthorizer.Authorize(_options.Room, pin, metadata, _options.Device.DeviceId);
        return new ReceivedTransfer(metadata, new MemoryStream(FileEncryptor.Decrypt(encrypted, transferId), writable: false));
    }

    public async ValueTask DisposeAsync()
    {
        _listener?.Stop();
        _listener?.Close();
        if (_serverTask is not null) try { await _serverTask; } catch (OperationCanceledException) { }
        _client.Dispose();
    }

    private async Task<T?> RequestAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (_options.PeerUri is null) throw new InvalidOperationException("PeerUri is required for remote operations.");
        using var request = new HttpRequestMessage(method, new Uri(_options.PeerUri, path));
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"LAN peer returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private async Task ServeAsync(HttpListener listener, CancellationToken ct)
    {
        while (listener.IsListening && !ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); } catch { break; }
            _ = Task.Run(() => HandleAsync(context), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (path is ["v1", "discover"])
            {
                await WriteJsonAsync(context, new[] { _options.Device }); return;
            }
            if (path.Length >= 4 && path[0] == "v1" && path[1] == "rooms" && path[3] == "transfers")
            {
                var roomId = Uri.UnescapeDataString(path[2]);
                AuthorizeRequest(context, roomId);
                if (path.Length == 4 && context.Request.HttpMethod == "GET")
                {
                    List<TransferDescriptor> result;
                    lock (_gate) result = _transfers.Values.Where(t => t.Metadata.ExpiresAt > DateTimeOffset.UtcNow)
                        .Select(t => new TransferDescriptor(t.Metadata, new Uri(LocalUri!, $"v1/rooms/{roomId}/transfers/{t.Metadata.TransferId}"))).ToList();
                    await WriteJsonAsync(context, result); return;
                }
                if (path.Length == 4 && context.Request.HttpMethod == "POST")
                {
                    var dto = await JsonSerializer.DeserializeAsync<WireTransfer>(context.Request.InputStream);
                    if (dto is null) throw new InvalidDataException();
                    lock (_gate) _transfers[dto.Metadata.TransferId] = new StoredTransfer(dto.Metadata, dto.Key, dto.Nonce, dto.EncryptedMetadata, dto.Ciphertext);
                    await WriteJsonAsync(context, new TransferDescriptor(dto.Metadata, new Uri(LocalUri!, $"v1/rooms/{roomId}/transfers/{dto.Metadata.TransferId}"))); return;
                }
                if (path.Length == 5 && context.Request.HttpMethod == "GET")
                {
                    lock (_gate) if (_transfers.TryGetValue(path[4], out var value)) { WriteJsonAsync(context, value).GetAwaiter().GetResult(); return; }
                    context.Response.StatusCode = 404; return;
                }
            }
            context.Response.StatusCode = 404;
        }
        catch (UnauthorizedAccessException) { context.Response.StatusCode = 401; }
        catch { context.Response.StatusCode = 400; }
        finally { context.Response.Close(); }
    }

    private void AuthorizeRequest(HttpListenerContext context, string roomId)
    {
        var pin = context.Request.QueryString["pin"] ?? "";
        if (!string.Equals(roomId, _options.Room.RoomId, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        TransferAuthorizer.Authorize(_options.Room, pin, new TransferMetadata("", "", 0, "", "", null, DateTimeOffset.UtcNow, _options.Room.ExpiresAt, roomId));
    }

    private static async Task WriteJsonAsync(HttpListenerContext c, object value)
    {
        c.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(c.Response.OutputStream, value);
    }

    private static int GetFreePort()
    {
        using var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start(); return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    private sealed record StoredTransfer(TransferMetadata Metadata, byte[] Key, byte[] Nonce, byte[] EncryptedMetadata, byte[] Ciphertext);
    private sealed record WireTransfer(TransferMetadata Metadata, byte[] Key, byte[] Nonce, byte[] EncryptedMetadata, byte[] Ciphertext);
}
