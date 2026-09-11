using MoqNet.Native;

namespace MoqNet.Client;

/// <summary>Async QUIC / WebTransport server.</summary>
public sealed class Server : IDisposable
{
    private readonly Native.Server _native;
    private bool _disposed;

    /// <summary>Creates a server with default configuration.</summary>
    public Server()
    {
        _native = new Native.Server();
    }

    /// <summary>Sets the bind address.</summary>
    public void SetBind(string addr) => _native.SetBind(addr);

    /// <summary>Sets TLS certificate file paths, separated by <c>;</c>.</summary>
    public void SetTlsCert(string? paths) => _native.SetTlsCert(paths);

    /// <summary>Sets TLS private key file paths, separated by <c>;</c>.</summary>
    public void SetTlsKey(string? paths) => _native.SetTlsKey(paths);

    /// <summary>Generates a self-signed certificate for the given hostnames, separated by <c>;</c>.</summary>
    public void SetTlsGenerate(string? hostnames) => _native.SetTlsGenerate(hostnames);

    /// <summary>Uses an existing origin as the session publisher for accepted connections.</summary>
    public void SetPublish(OriginProducer? origin) => _native.SetPublish(origin?.Native);

    /// <summary>Uses an existing origin as the session subscriber for accepted connections.</summary>
    public void SetConsume(OriginProducer? origin) => _native.SetConsume(origin?.Native);

    /// <summary>Binds the server and returns the local address.</summary>
    public Task<string> ListenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() => _native.Listen(), cancellationToken);
    }

    /// <summary>Waits for the next connection request, or returns null when the listener ends.</summary>
    public async Task<Request?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var request = await Task.Run(() => _native.Accept(), cancellationToken).ConfigureAwait(false);
        return request == null ? null : new Request(request);
    }

    /// <summary>Returns served certificate fingerprints as JSON.</summary>
    public string CertFingerprintsJson() => _native.CertFingerprintsJson();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _native.Dispose();
    }
}

/// <summary>An incoming connection that can be accepted or rejected.</summary>
public sealed class Request : IDisposable
{
    private readonly Native.Request _native;

    internal Request(Native.Request native)
    {
        _native = native;
    }

    /// <summary>Request URL, if the transport provided one.</summary>
    public string? Url => _native.Url;

    /// <summary>Request path.</summary>
    public string Path => _native.Path;

    /// <summary>Query string, if present.</summary>
    public string? Query => _native.Query;

    /// <summary>Transport name.</summary>
    public string Transport => _native.Transport;

    /// <summary>Accepts the request and returns a session.</summary>
    public async Task<Session> AcceptAsync(CancellationToken cancellationToken = default)
    {
        var session = await Task.Run(() => _native.Accept(), cancellationToken).ConfigureAwait(false);
        return new Session(session);
    }

    /// <summary>Rejects the request with an HTTP-style status code.</summary>
    public Task RejectAsync(ushort code, CancellationToken cancellationToken = default) =>
        Task.Run(() => _native.Reject(code), cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}
