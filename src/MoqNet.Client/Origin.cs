using MoqNet.Native;

namespace MoqNet.Client;

/// <summary>Produces broadcasts under a MoQ origin.</summary>
public sealed class OriginProducer : IDisposable
{
    private readonly Native.OriginProducer _native;

    /// <summary>Creates a new origin. Pass a non-negative cache capacity in bytes, or -1 for the default pool.</summary>
    public OriginProducer(long cacheCapacityBytes = -1)
    {
        _native = new Native.OriginProducer(cacheCapacityBytes);
    }

    internal OriginProducer(Native.OriginProducer native)
    {
        _native = native;
    }

    internal Native.OriginProducer Native => _native;

    /// <summary>Returns a consumer handle for this origin.</summary>
    public OriginConsumer Consume() => new(_native.Consume());

    /// <summary>Creates and announces a broadcast at <paramref name="path"/>.</summary>
    public BroadcastProducer CreateBroadcast(string path) => new(_native.CreateBroadcast(path));

    /// <summary>Accepts on-demand broadcast requests for this origin.</summary>
    public OriginDynamic Dynamic() => new(_native.Dynamic());

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes announced broadcasts from an origin.</summary>
public sealed class OriginConsumer : IDisposable
{
    private readonly Native.OriginConsumer _native;

    internal OriginConsumer(Native.OriginConsumer native)
    {
        _native = native;
    }

    /// <summary>Watches announced broadcasts under <paramref name="prefix"/>.</summary>
    public Announced Announced(string prefix) => new(_native.Announced(prefix));

    /// <summary>Waits until the broadcast at <paramref name="path"/> is announced.</summary>
    public AnnouncedBroadcast AnnouncedBroadcast(string path) => new(_native.AnnouncedBroadcast(path));

    /// <summary>Requests a broadcast by path.</summary>
    public async Task<BroadcastConsumer> RequestBroadcastAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var consumer = await Task.Run(() => _native.RequestBroadcast(path), cancellationToken).ConfigureAwait(false);
        return new BroadcastConsumer(consumer);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Stream of announced broadcasts.</summary>
public sealed class Announced : IDisposable
{
    private readonly Native.Announced _native;

    internal Announced(Native.Announced native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next announced broadcast, or returns null when the stream ends.</summary>
    public async Task<(string Path, BroadcastConsumer Broadcast)?> NextAsync(CancellationToken cancellationToken = default)
    {
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
        return await Task.Run(() =>
        {
            if (!_native.Next(out var path, out var broadcast))
            {
                return ((string Path, BroadcastConsumer Broadcast)?)null;
            }

            return (path, new BroadcastConsumer(broadcast));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Waits for a single named broadcast to become available.</summary>
public sealed class AnnouncedBroadcast : IDisposable
{
    private readonly Native.AnnouncedBroadcast _native;

    internal AnnouncedBroadcast(Native.AnnouncedBroadcast native)
    {
        _native = native;
    }

    /// <summary>Blocks until the broadcast is announced.</summary>
    public async Task<BroadcastConsumer> AvailableAsync(CancellationToken cancellationToken = default)
    {
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
        var consumer = await Task.Run(() => _native.Available(), cancellationToken).ConfigureAwait(false);
        return new BroadcastConsumer(consumer);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Accepts on-demand broadcast production requests.</summary>
public sealed class OriginDynamic : IDisposable
{
    private readonly Native.OriginDynamic _native;

    internal OriginDynamic(Native.OriginDynamic native)
    {
        _native = native;
    }

    /// <summary>Blocks until a subscriber requests a broadcast.</summary>
    public async Task<BroadcastRequest> RequestedBroadcastAsync(CancellationToken cancellationToken = default)
    {
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
        var request = await Task.Run(() => _native.RequestedBroadcast(), cancellationToken).ConfigureAwait(false);
        return new BroadcastRequest(request);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>A subscriber request for a broadcast.</summary>
public sealed class BroadcastRequest : IDisposable
{
    private readonly Native.BroadcastRequest _native;

    internal BroadcastRequest(Native.BroadcastRequest native)
    {
        _native = native;
    }

    /// <summary>Requested broadcast path.</summary>
    public string Path => _native.Path;

    /// <summary>Serves this request with <paramref name="broadcast"/>.</summary>
    public void Accept(BroadcastProducer broadcast) => _native.Accept(broadcast.Native);

    /// <summary>Rejects the request with an application error code.</summary>
    public void Abort(ushort errorCode) => _native.Abort(errorCode);

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}
