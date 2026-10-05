using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
        LocalUri = new Uri($"http://+:{port}/");
        _listener.Prefixes.Add(LocalUri.ToString());
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            _listener.Prefixes.Clear();
            LocalUri = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(LocalUri.ToString());
            _listener.Start();
        }
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
            DateTimeOffset.UtcNow, expires, roomId, Math.Max(1, request.RecipientCount),
            request.Note, request.Visibility, request.AllowedDeviceIds);
        var encrypted = await FileEncryptor.EncryptAsync(request.Content, metadata, cancellationToken);
        var dto = new WireTransfer(metadata, FileEncryptor.ProtectFileKey(encrypted.Key, pin, roomId),
            encrypted.Nonce, encrypted.EncryptedMetadata, encrypted.Ciphertext);
        return await RequestAsync<TransferDescriptor>(
            HttpMethod.Post, $"v1/rooms/{Uri.EscapeDataString(roomId)}/transfers?pin={Uri.EscapeDataString(pin)}",
            dto, cancellationToken) ?? throw new IOException("Peer returned no transfer descriptor.");
    }

    public async Task<ReceivedTransfer> FetchAsync(
        string roomId, string pin, string transferId, CancellationToken cancellationToken = default)
    {
        var dto = await RequestAsync<WireTransfer>(
            HttpMethod.Get, $"v1/rooms/{Uri.EscapeDataString(roomId)}/transfers/{Uri.EscapeDataString(transferId)}?pin={Uri.EscapeDataString(pin)}&deviceId={Uri.EscapeDataString(_options.Device.DeviceId)}",
            null, cancellationToken) ?? throw new FileNotFoundException("Transfer not found.");
        var fileKey = FileEncryptor.UnprotectFileKey(dto.ProtectedKey, pin, roomId);
        var encrypted = new EncryptedFile(dto.EncryptedMetadata, dto.Ciphertext, fileKey, dto.Nonce);
        var metadata = FileEncryptor.DecryptMetadata(encrypted, transferId);
        TransferAuthorizer.Authorize(_options.Room, pin, metadata, _options.Device.DeviceId);
        return new ReceivedTransfer(metadata, new MemoryStream(FileEncryptor.Decrypt(encrypted, transferId), writable: false));
    }

    public async Task RevokeAsync(string roomId, string transferId, CancellationToken cancellationToken = default)
    {
        await RequestAsync<object>(HttpMethod.Delete,
            $"v1/rooms/{Uri.EscapeDataString(roomId)}/transfers/{Uri.EscapeDataString(transferId)}",
            null, cancellationToken);
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
        if (_options.PeerUri is null)
            return await HandleLocalAsync<T>(method, path, body, ct);
        using var request = new HttpRequestMessage(method, new Uri(_options.PeerUri, path));
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"LAN peer returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        if (response.StatusCode == HttpStatusCode.NoContent)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private Task<T?> HandleLocalAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var segments = path.Split('?', 2)[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            throw new InvalidOperationException("Invalid local transfer path.");
        if (method == HttpMethod.Post && body is WireTransfer upload)
        {
            lock (_gate)
                _transfers[upload.Metadata.TransferId] = new StoredTransfer(upload.Metadata, upload.ProtectedKey,
                    upload.Nonce, upload.EncryptedMetadata, upload.Ciphertext, GetQueryValue(path, "pin"));
            return Task.FromResult<T?>(ConvertResult<T>(new TransferDescriptor(upload.Metadata,
                new Uri("http://local/v1/rooms/" + upload.Metadata.RoomId + "/transfers/" + upload.Metadata.TransferId))));
        }
        if (method == HttpMethod.Get && segments.Length == 4)
        {
            lock (_gate)
            {
                var result = _transfers.Values
                    .Where(t => t.Metadata.ExpiresAt is null || t.Metadata.ExpiresAt > DateTimeOffset.UtcNow)
                    .Select(t => new TransferDescriptor(t.Metadata,
                        new Uri("http://local/v1/rooms/" + t.Metadata.RoomId + "/transfers/" + t.Metadata.TransferId)))
                    .ToList();
                return Task.FromResult<T?>(ConvertResult<T>(result));
            }
        }
        if (method == HttpMethod.Get && segments.Length == 5)
        {
            lock (_gate)
            {
                if (!_transfers.TryGetValue(segments[4], out var value))
                    throw new FileNotFoundException("Transfer not found.");
                var pin = GetQueryValue(path, "pin");
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(value.PinHash),
                        SHA256.HashData(Encoding.UTF8.GetBytes(pin))))
                    throw new UnauthorizedAccessException("Incorrect transfer PIN.");
                return Task.FromResult<T?>(ConvertResult<T>(new WireTransfer(value.Metadata, value.ProtectedKey,
                    value.Nonce, value.EncryptedMetadata, value.Ciphertext)));
            }
        }
        if (method == HttpMethod.Delete && segments.Length == 5)
        {
            lock (_gate)
                _transfers.Remove(segments[4]);
            return Task.FromResult<T?>(default);
        }
        throw new InvalidOperationException("Unsupported local transfer operation.");
    }

    private static T ConvertResult<T>(object value) => (T)value;

    private static string GetQueryValue(string path, string name)
    {
        var query = new Uri("http://local/" + path.TrimStart('/')).Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.Ordinal))
                return Uri.UnescapeDataString(parts[1]);
        }
        return string.Empty;
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
                    lock (_gate) _transfers[dto.Metadata.TransferId] = new StoredTransfer(dto.Metadata, dto.ProtectedKey,
                        dto.Nonce, dto.EncryptedMetadata, dto.Ciphertext,
                        context.Request.QueryString["pin"] ?? "");
                    await WriteJsonAsync(context, new TransferDescriptor(dto.Metadata, new Uri(LocalUri!, $"v1/rooms/{roomId}/transfers/{dto.Metadata.TransferId}"))); return;
                }
                if (path.Length == 5 && context.Request.HttpMethod == "DELETE")
                {
                    lock (_gate) _transfers.Remove(path[4]);
                    context.Response.StatusCode = 204;
                    return;
                }
                if (path.Length == 5 && context.Request.HttpMethod == "GET")
                {
                    lock (_gate) if (_transfers.TryGetValue(path[4], out var value))
                    {
                        var deviceId = context.Request.QueryString["deviceId"] ?? "unknown";
                        var pin = context.Request.QueryString["pin"] ?? "";
                        if (value.Attempts.TryGetValue(deviceId, out var attempts) &&
                            attempts >= _options.Room.MaxPinAttempts)
                            throw new UnauthorizedAccessException("PIN attempts exhausted for this recipient.");
                        try
                        {
                            TransferAuthorizer.Authorize(_options.Room, pin, value.Metadata, deviceId);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            value.Attempts[deviceId] = attempts + 1;
                            throw;
                        }
                        value.RemainingPickups--;
                        var response = new WireTransfer(value.Metadata, value.ProtectedKey, value.Nonce,
                            value.EncryptedMetadata, value.Ciphertext);
                        if (value.RemainingPickups <= 0)
                            _transfers.Remove(path[4]);
                        WriteJsonAsync(context, response).GetAwaiter().GetResult(); return;
                    }
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
        TransferAuthorizer.AuthorizeRoomPin(_options.Room, pin, roomId);
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

    private sealed class StoredTransfer
    {
        public StoredTransfer(TransferMetadata metadata, byte[] protectedKey, byte[] nonce,
            byte[] encryptedMetadata, byte[] ciphertext, string pin)
        {
            Metadata = metadata;
            ProtectedKey = protectedKey;
            Nonce = nonce;
            EncryptedMetadata = encryptedMetadata;
            Ciphertext = ciphertext;
            PinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
            RemainingPickups = metadata.RecipientCount;
        }

        public TransferMetadata Metadata { get; }
        public byte[] ProtectedKey { get; }
        public byte[] Nonce { get; }
        public byte[] EncryptedMetadata { get; }
        public byte[] Ciphertext { get; }
        public string PinHash { get; }
        public int RemainingPickups { get; set; }
        public Dictionary<string, int> Attempts { get; } = new(StringComparer.Ordinal);
    }
    private sealed record WireTransfer(TransferMetadata Metadata, byte[] ProtectedKey, byte[] Nonce,
        byte[] EncryptedMetadata, byte[] Ciphertext);
}
