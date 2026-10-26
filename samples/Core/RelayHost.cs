using System.Collections.Concurrent;
using MoqException = MoqNet.Native.MoqException;
using MoqOrigin = MoqNet.Client.OriginProducer;
using MoqRequest = MoqNet.Client.Request;
using MoqServer = MoqNet.Client.Server;
using MoqSession = MoqNet.Client.Session;

namespace MoqNet.Samples;

/// <summary>
/// In-process MoQ relay that fans one publisher out to many subscribers.
/// </summary>
public sealed class RelayHost : IAsyncDisposable
{
    private readonly MoqServer _server = new();
    private readonly MoqOrigin _origin = new();
    private readonly ConcurrentDictionary<MoqSession, byte> _sessions = new();
    private CancellationTokenSource? _listenCts;
    private Task? _acceptLoop;
    private bool _started;

    /// <summary>Local address returned by <see cref="StartAsync"/>.</summary>
    public string? ListenAddress { get; private set; }

    /// <summary>Binds the relay and starts accepting sessions.</summary>
    public async Task<string> StartAsync(string bind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bind);
        if (_started)
        {
            throw new InvalidOperationException("Relay is already listening.");
        }

        _server.SetBind(bind);
        _server.SetTlsGenerate(SampleProtocol.TlsGenerateHosts);
        _server.SetPublish(_origin);
        _server.SetConsume(_origin);

        ListenAddress = await _server.ListenAsync(cancellationToken).ConfigureAwait(false);
        _started = true;
        _listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoop = AcceptLoopAsync(_listenCts.Token);
        return ListenAddress;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_listenCts is not null)
        {
            await _listenCts.CancelAsync().ConfigureAwait(false);
        }

        foreach (var session in _sessions.Keys)
        {
            session.Dispose();
        }

        _sessions.Clear();
        _server.Dispose();
        await AwaitLoopAsync(_acceptLoop).ConfigureAwait(false);
        _origin.Dispose();
        _listenCts?.Dispose();
    }

    private static async Task AwaitLoopAsync(Task? loop)
    {
        if (loop is null)
        {
            return;
        }

        try
        {
            await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (MoqException)
        {
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            MoqRequest? request;
            try
            {
                request = await _server.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (MoqException)
            {
                return;
            }

            if (request is null)
            {
                return;
            }

            _ = AcceptSessionAsync(request, cancellationToken);
        }
    }

    private async Task AcceptSessionAsync(MoqRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await request.AcceptAsync(cancellationToken).ConfigureAwait(false);
            _sessions[session] = 0;
            try
            {
                await session.ClosedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (MoqException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _sessions.TryRemove(session, out _);
                session.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (MoqException)
        {
        }
        finally
        {
            request.Dispose();
        }
    }
}
