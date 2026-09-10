using MoqNet.Native;

namespace MoqNet.Client;

/// <summary>
/// Async MoQ client for .NET 8. Transport stays inside the native library.
/// </summary>
public sealed class Client : IDisposable
{
    private readonly Native.Client _native;
    private bool _disposed;

    /// <summary>Creates a client with TLS verification enabled.</summary>
    public Client()
    {
        _native = new Native.Client();
    }

    internal Native.Client Native => _native;

    /// <summary>
    /// Disables TLS certificate verification. Development only; leave off in production.
    /// </summary>
    public void SetTlsDisableVerify(bool disable) => _native.SetTlsDisableVerify(disable);

    /// <summary>Sets extra TLS root certificate file paths, separated by <c>;</c>.</summary>
    public void SetTlsRoots(string? paths) => _native.SetTlsRoots(paths);

    /// <summary>Enables or disables the platform TLS root store.</summary>
    public void SetTlsSystemRoots(bool systemRoots) => _native.SetTlsSystemRoots(systemRoots);

    /// <summary>Pins server certificate SHA-256 fingerprints, separated by <c>;</c>.</summary>
    public void SetTlsFingerprints(string? fingerprints) => _native.SetTlsFingerprints(fingerprints);

    /// <summary>Sets the client TLS certificate file path.</summary>
    public void SetTlsCert(string? path) => _native.SetTlsCert(path);

    /// <summary>Sets the client TLS private key file path.</summary>
    public void SetTlsKey(string? path) => _native.SetTlsKey(path);

    /// <summary>Sets the local bind address.</summary>
    public void SetBind(string addr) => _native.SetBind(addr);

    /// <summary>Uses an existing origin as the session publisher.</summary>
    public void SetPublish(OriginProducer? origin) => _native.SetPublish(origin?.Native);

    /// <summary>Uses an existing origin as the session subscriber.</summary>
    public void SetConsume(OriginProducer? origin) => _native.SetConsume(origin?.Native);

    /// <summary>Connects to a MoQ relay on the thread pool.</summary>
    public async Task<Session> ConnectAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                _native.Cancel();
            }
            catch (MoqException)
            {
            }
        });
        var session = await Task.Run(() => _native.Connect(url), cancellationToken).ConfigureAwait(false);
        return new Session(session);
    }

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

/// <summary>An established MoQ session.</summary>
public sealed class Session : IAsyncDisposable, IDisposable
{
    private readonly Native.Session _native;
    private bool _disposed;

    internal Session(Native.Session native)
    {
        _native = native;
    }

    /// <summary>Origin used to publish broadcasts.</summary>
    public OriginProducer Publisher => new(_native.Publisher);

    /// <summary>Origin used to consume broadcasts.</summary>
    public OriginConsumer Consumer => new(_native.Consumer);

    /// <summary>Gracefully shuts the session down.</summary>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() => _native.Shutdown(), cancellationToken);
    }

    /// <summary>Aborts the session with an application error code.</summary>
    public Task CancelAsync(uint code = 0, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() => _native.Cancel(code), cancellationToken);
    }

    /// <summary>Returns session statistics as JSON.</summary>
    public Task<string> StatsJsonAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() => _native.StatsJson(), cancellationToken);
    }

    /// <summary>Waits until the session closes, then throws with the close error.</summary>
    public Task ClosedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() => _native.Closed(), cancellationToken);
    }

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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Task.Run(() => _native.Dispose()).ConfigureAwait(false);
    }
}
