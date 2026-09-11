using MoqNet.Native;

namespace MoqNet.Client;

/// <summary>Produces tracks on a broadcast.</summary>
public sealed class BroadcastProducer : IDisposable
{
    private readonly Native.BroadcastProducer _native;

    /// <summary>Creates a standalone broadcast that is not attached to an origin.</summary>
        public static BroadcastProducer Create() => new(global::MoqNet.Native.BroadcastProducer.Create());

    internal BroadcastProducer(Native.BroadcastProducer native)
    {
        _native = native;
    }

    internal Native.BroadcastProducer Native => _native;

    /// <summary>Returns a consumer of this broadcast.</summary>
    public BroadcastConsumer Consume() => new(_native.Consume());

    /// <summary>Accepts on-demand track requests.</summary>
    public BroadcastDynamic Dynamic() => new(_native.Dynamic());

    /// <summary>Controls whether this broadcast is announced.</summary>
    public void SetAnnounce(bool announce) => _native.SetAnnounce(announce);

    /// <summary>Sets a JSON catalog section.</summary>
    public void SetCatalogSection(string name, string json) => _native.SetCatalogSection(name, json);

    /// <summary>Removes a catalog section.</summary>
    public void RemoveCatalogSection(string name) => _native.RemoveCatalogSection(name);

    /// <summary>Publishes a raw byte track.</summary>
    public TrackProducer PublishTrack(string name, TrackOptions? options = null) => new(_native.PublishTrack(name, options));

    /// <summary>Publishes an encoded media container or elementary track.</summary>
    public MediaProducer PublishMedia(string format, byte[]? init = null) => new(_native.PublishMedia(format, init));

    /// <summary>Publishes a streaming media container.</summary>
    public MediaStreamProducer PublishMediaStream(string format) => new(_native.PublishMediaStream(format));

    /// <summary>Publishes a JSON snapshot track.</summary>
    public JsonSnapshotProducer PublishJsonSnapshot(string name, uint deltaRatio = 0, bool compression = false) =>
        new(_native.PublishJsonSnapshot(name, deltaRatio, compression));

    /// <summary>Publishes a JSON stream track.</summary>
    public JsonStreamProducer PublishJsonStream(string name, bool compression = false) =>
        new(_native.PublishJsonStream(name, compression));

    /// <summary>Publishes a PCM audio track encoded to Opus inside the native library.</summary>
    public AudioProducer PublishAudio(string name, AudioFormat format, uint sampleRate, uint channels, long bitrate = -1, uint frameDurationMs = 20) =>
        new(_native.PublishAudio(name, format, sampleRate, channels, bitrate, frameDurationMs));

    /// <summary>Publishes a raw-video track encoded inside the native library.</summary>
    public VideoProducer PublishVideo(
        VideoPixelFormat format,
        uint width,
        uint height,
        uint framerate,
        VideoCodec codec = VideoCodec.H264,
        string? track = null,
        long bitrate = -1,
        int gop = -1,
        VideoEncoderKind kind = VideoEncoderKind.Auto,
        string? kindName = null) =>
        new(_native.PublishVideo(format, width, height, framerate, codec, track, bitrate, gop, kind, kindName));

    /// <summary>Finishes the broadcast.</summary>
    public void Finish() => _native.Finish();

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>On-demand track production for a broadcast.</summary>
public sealed class BroadcastDynamic : IDisposable
{
    private readonly Native.BroadcastDynamic _native;

    internal BroadcastDynamic(Native.BroadcastDynamic native)
    {
        _native = native;
    }

    /// <summary>Blocks until a subscriber requests a track.</summary>
    public async Task<TrackRequest> RequestedTrackAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        var request = await Task.Run(() => _native.RequestedTrack(), cancellationToken).ConfigureAwait(false);
        return new TrackRequest(request);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>A subscriber request for a track.</summary>
public sealed class TrackRequest : IDisposable
{
    private readonly Native.TrackRequest _native;

    internal TrackRequest(Native.TrackRequest native)
    {
        _native = native;
    }

    /// <summary>Requested track name.</summary>
    public string Name => _native.Name;

    /// <summary>Accepts the request and returns a producer.</summary>
    public TrackProducer Accept(TrackOptions? options = null) => new(_native.Accept(options));

    /// <summary>Rejects the request.</summary>
    public void Abort(ushort errorCode) => _native.Abort(errorCode);

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes frames on a raw track.</summary>
public sealed class TrackProducer : IDisposable
{
    private readonly Native.TrackProducer _native;

    internal TrackProducer(Native.TrackProducer native)
    {
        _native = native;
    }

    /// <summary>Track name.</summary>
    public string Name => _native.Name;

    /// <summary>Appends a new group.</summary>
    public GroupProducer AppendGroup() => new(_native.AppendGroup());

    /// <summary>Writes a timestamped frame.</summary>
    public void WriteFrame(byte[] payload, ulong timestampUs) => _native.WriteFrame(payload, timestampUs);

    /// <summary>Appends a datagram and returns its sequence number.</summary>
    public ulong AppendDatagram(byte[] payload, ulong timestampUs) => _native.AppendDatagram(payload, timestampUs);

    /// <summary>Waits until at least one subscriber is attached.</summary>
    public Task UsedAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => _native.Used(), cancellationToken);

    /// <summary>Waits until no subscribers remain.</summary>
    public Task UnusedAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => _native.Unused(), cancellationToken);

    /// <summary>Returns a local consumer of this track.</summary>
    public TrackConsumer Consume() => new(_native.Consume());

    /// <summary>Finishes the track.</summary>
    public void Finish() => _native.Finish();

    /// <summary>Aborts the track with an application error code.</summary>
    public void Abort(ushort errorCode) => _native.Abort(errorCode);

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes frames inside a single group.</summary>
public sealed class GroupProducer : IDisposable
{
    private readonly Native.GroupProducer _native;

    internal GroupProducer(Native.GroupProducer native)
    {
        _native = native;
    }

    /// <summary>Group sequence number.</summary>
    public ulong Sequence => _native.Sequence;

    /// <summary>Writes a timestamped frame into this group.</summary>
    public void WriteFrame(byte[] payload, ulong timestampUs) => _native.WriteFrame(payload, timestampUs);

    /// <summary>Finishes the group.</summary>
    public void Finish() => _native.Finish();

    /// <summary>Returns a local consumer of this group.</summary>
    public GroupConsumer Consume() => new(_native.Consume());

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes already-encoded media frames.</summary>
public sealed class MediaProducer : IDisposable
{
    private readonly Native.MediaProducer _native;

    internal MediaProducer(Native.MediaProducer native)
    {
        _native = native;
    }

    /// <summary>Track name, when the media is a single elementary track.</summary>
    public string Name => _native.Name;

    /// <summary>Decodes and publishes one encoded frame.</summary>
    public void WriteFrame(byte[] payload, ulong timestampUs) => _native.WriteFrame(payload, timestampUs);

    /// <summary>Finishes the media track or container.</summary>
    public void Finish() => _native.Finish();

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes a streaming media bitstream.</summary>
public sealed class MediaStreamProducer : IDisposable
{
    private readonly Native.MediaStreamProducer _native;

    internal MediaStreamProducer(Native.MediaStreamProducer native)
    {
        _native = native;
    }

    /// <summary>Pushes bitstream bytes.</summary>
    public void Write(byte[] payload) => _native.Write(payload);

    /// <summary>Finishes the stream.</summary>
    public void Finish() => _native.Finish();

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes JSON document snapshots.</summary>
public sealed class JsonSnapshotProducer : IDisposable
{
    private readonly Native.JsonSnapshotProducer _native;

    internal JsonSnapshotProducer(Native.JsonSnapshotProducer native)
    {
        _native = native;
    }

    /// <summary>Replaces the current snapshot with JSON text.</summary>
    public void Update(string json) => _native.Update(json);

    /// <summary>Finishes the snapshot track.</summary>
    public void Finish() => _native.Finish();

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes a JSON event stream.</summary>
public sealed class JsonStreamProducer : IDisposable
{
    private readonly Native.JsonStreamProducer _native;

    internal JsonStreamProducer(Native.JsonStreamProducer native)
    {
        _native = native;
    }

    /// <summary>Appends a JSON value.</summary>
    public void Append(string json) => _native.Append(json);

    /// <summary>Finishes the stream track.</summary>
    public void Finish() => _native.Finish();

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes PCM audio that the native library encodes.</summary>
public sealed class AudioProducer : IDisposable
{
    private readonly Native.AudioProducer _native;

    internal AudioProducer(Native.AudioProducer native)
    {
        _native = native;
    }

    /// <summary>Audio track name.</summary>
    public string Name => _native.Name;

    /// <summary>Writes one PCM frame.</summary>
    public void Write(byte[] payload, ulong timestampUs) => _native.Write(payload, timestampUs);

    /// <summary>Finishes the audio track.</summary>
    public void Finish() => _native.Finish();

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Publishes raw video that the native library encodes.</summary>
public sealed class VideoProducer : IDisposable
{
    private readonly Native.VideoProducer _native;

    internal VideoProducer(Native.VideoProducer native)
    {
        _native = native;
    }

    /// <summary>Video track name.</summary>
    public string Name => _native.Name;

    /// <summary>Encodes and publishes one raw picture.</summary>
    public void Write(byte[] payload, ulong timestampUs) => _native.Write(payload, timestampUs);

    /// <summary>Requests a keyframe on the next encoded picture.</summary>
    public void Cut() => _native.Cut();

    /// <summary>Updates the target bitrate in bits per second.</summary>
    public void SetBitrate(ulong bitrate) => _native.SetBitrate(bitrate);

    /// <summary>Drains the encoder and finishes the track.</summary>
    public void Finish() => _native.Finish();

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes tracks from a broadcast.</summary>
public sealed class BroadcastConsumer : IDisposable
{
    private readonly Native.BroadcastConsumer _native;

    internal BroadcastConsumer(Native.BroadcastConsumer native)
    {
        _native = native;
    }

    /// <summary>Current route as JSON.</summary>
    public string RouteJson => _native.RouteJson;

    /// <summary>Watches route updates.</summary>
    public RouteWatch RouteUpdates() => new(_native.RouteUpdates());

    /// <summary>Subscribes to the hang catalog.</summary>
    public async Task<CatalogConsumer> SubscribeCatalogAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await Task.Run(() => _native.SubscribeCatalog(), cancellationToken).ConfigureAwait(false);
        return new CatalogConsumer(catalog);
    }

    /// <summary>Subscribes to a raw track.</summary>
    public async Task<TrackConsumer> SubscribeTrackAsync(string name, SubscriptionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var track = await Task.Run(() => _native.SubscribeTrack(name, options), cancellationToken).ConfigureAwait(false);
        return new TrackConsumer(track);
    }

    /// <summary>Subscribes to encoded media.</summary>
    public async Task<MediaConsumer> SubscribeMediaAsync(string name, MediaContainerKind kind, byte[]? init = null, SubscriptionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var media = await Task.Run(() => _native.SubscribeMedia(name, kind, init, options), cancellationToken).ConfigureAwait(false);
        return new MediaConsumer(media);
    }

    /// <summary>Fetches a historical group by sequence.</summary>
    public async Task<GroupConsumer> FetchGroupAsync(string name, ulong sequence, byte priority = 128, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var group = await Task.Run(() => _native.FetchGroup(name, sequence, priority), cancellationToken).ConfigureAwait(false);
        return new GroupConsumer(group);
    }

    /// <summary>Subscribes to a JSON snapshot track.</summary>
    public async Task<JsonSnapshotConsumer> SubscribeJsonSnapshotAsync(string name, bool compression = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var consumer = await Task.Run(() => _native.SubscribeJsonSnapshot(name, compression), cancellationToken).ConfigureAwait(false);
        return new JsonSnapshotConsumer(consumer);
    }

    /// <summary>Subscribes to a JSON stream track.</summary>
    public async Task<JsonStreamConsumer> SubscribeJsonStreamAsync(string name, bool compression = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var consumer = await Task.Run(() => _native.SubscribeJsonStream(name, compression), cancellationToken).ConfigureAwait(false);
        return new JsonStreamConsumer(consumer);
    }

    /// <summary>Subscribes to an audio track and decodes PCM.</summary>
    public async Task<AudioConsumer> SubscribeAudioAsync(string name, string codec, uint sampleRate, uint channelCount, AudioFormat format, long latencyMaxMs = -1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(codec);
        var consumer = await Task.Run(() => _native.SubscribeAudio(name, codec, sampleRate, channelCount, format, latencyMaxMs), cancellationToken).ConfigureAwait(false);
        return new AudioConsumer(consumer);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Watches broadcast route changes.</summary>
public sealed class RouteWatch : IDisposable
{
    private readonly Native.RouteWatch _native;

    internal RouteWatch(Native.RouteWatch native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next route JSON, or returns null when dropped.</summary>
    public async Task<string?> NextAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        return await Task.Run(() => _native.Next(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes hang catalog updates as JSON.</summary>
public sealed class CatalogConsumer : IDisposable
{
    private readonly Native.CatalogConsumer _native;

    internal CatalogConsumer(Native.CatalogConsumer native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next catalog JSON, or returns null at end of stream.</summary>
    public async Task<string?> NextAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        return await Task.Run(() => _native.Next(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes frames from a raw track.</summary>
public sealed class TrackConsumer : IDisposable
{
    private readonly Native.TrackConsumer _native;

    internal TrackConsumer(Native.TrackConsumer native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next frame, or returns null when the track ends.</summary>
    public async Task<MoqFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        return await Task.Run(() => _native.ReadFrame(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Blocks until the next group, or returns null when the track ends.</summary>
    public async Task<GroupConsumer?> NextGroupAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        var group = await Task.Run(() => _native.NextGroup(), cancellationToken).ConfigureAwait(false);
        return group == null ? null : new GroupConsumer(group);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes frames from a single group.</summary>
public sealed class GroupConsumer : IDisposable
{
    private readonly Native.GroupConsumer _native;

    internal GroupConsumer(Native.GroupConsumer native)
    {
        _native = native;
    }

    /// <summary>Group sequence number.</summary>
    public ulong Sequence => _native.Sequence;

    /// <summary>Blocks until the next frame, or returns null when the group ends.</summary>
    public Task<MoqFrame?> ReadFrameAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => _native.ReadFrame(), cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes decoded media frames from a container.</summary>
public sealed class MediaConsumer : IDisposable
{
    private readonly Native.MediaConsumer _native;

    internal MediaConsumer(Native.MediaConsumer native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next media frame, or returns null at end of stream.</summary>
    public async Task<MoqFrame?> NextAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        return await Task.Run(() => _native.Next(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes JSON snapshot documents.</summary>
public sealed class JsonSnapshotConsumer : IDisposable
{
    private readonly Native.JsonSnapshotConsumer _native;

    internal JsonSnapshotConsumer(Native.JsonSnapshotConsumer native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next JSON document, or returns null at end of stream.</summary>
    public async Task<string?> NextAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        return await Task.Run(() => _native.Next(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes JSON stream events.</summary>
public sealed class JsonStreamConsumer : IDisposable
{
    private readonly Native.JsonStreamConsumer _native;

    internal JsonStreamConsumer(Native.JsonStreamConsumer native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next JSON value, or returns null at end of stream.</summary>
    public async Task<string?> NextAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        return await Task.Run(() => _native.Next(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}

/// <summary>Consumes decoded PCM audio.</summary>
public sealed class AudioConsumer : IDisposable
{
    private readonly Native.AudioConsumer _native;

    internal AudioConsumer(Native.AudioConsumer native)
    {
        _native = native;
    }

    /// <summary>Blocks until the next PCM frame, or returns null at end of stream.</summary>
    public async Task<MoqFrame?> NextAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _native.Cancel(); }
            catch (MoqException) { }
        });
        return await Task.Run(() => _native.Next(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _native.Dispose();
}
