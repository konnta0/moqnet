using MoqClient = MoqNet.Client.Client;
using MoqSession = MoqNet.Client.Session;
using MoqBroadcast = MoqNet.Client.BroadcastProducer;
using MoqAudio = MoqNet.Client.AudioProducer;
using MoqVideo = MoqNet.Client.VideoProducer;
using MoqTrack = MoqNet.Client.TrackProducer;
using MoqException = MoqNet.Native.MoqException;
using AudioFormat = MoqNet.Native.AudioFormat;
using VideoPixelFormat = MoqNet.Native.VideoPixelFormat;
using VideoCodec = MoqNet.Native.VideoCodec;
using VideoEncoderKind = MoqNet.Native.VideoEncoderKind;

namespace MoqNet.Samples;

/// <summary>
/// Publishes Opus audio, optional H.264, and a JPEG preview on <see cref="SampleProtocol.BroadcastPath"/>.
/// </summary>
public sealed class LivePublisherSession : IAsyncDisposable
{
    private MoqClient? _client;
    private MoqSession? _session;
    private MoqBroadcast? _broadcast;
    private MoqAudio? _audio;
    private MoqVideo? _video;
    private MoqTrack? _preview;

    /// <summary>True after <see cref="StartAsync"/> succeeds.</summary>
    public bool IsConnected => _session is not null;

    /// <summary>True when the native H.264 encoder started.</summary>
    public bool VideoEncoderAvailable { get; private set; }

    /// <summary>Connects to the relay and announces the live broadcast.</summary>
    public async Task StartAsync(string url, bool disableTlsVerify, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (_session is not null)
        {
            throw new InvalidOperationException("Publisher is already connected.");
        }

        var client = new MoqClient();
        MoqSession? session = null;
        MoqBroadcast? broadcast = null;
        MoqAudio? audio = null;
        MoqTrack? preview = null;
        MoqVideo? video = null;
        try
        {
            if (disableTlsVerify)
            {
                client.SetTlsDisableVerify(true);
            }

            session = await client.ConnectAsync(url, cancellationToken).ConfigureAwait(false);
            broadcast = session.Publisher.CreateBroadcast(SampleProtocol.BroadcastPath);
            audio = broadcast.PublishAudio(
                SampleProtocol.AudioTrack,
                AudioFormat.F32,
                SampleProtocol.AudioSampleRate,
                SampleProtocol.AudioChannels,
                bitrate: -1,
                SampleProtocol.AudioFrameDurationMs);
            preview = broadcast.PublishTrack(SampleProtocol.PreviewTrack);
            try
            {
                video = broadcast.PublishVideo(
                    VideoPixelFormat.Rgba,
                    SampleProtocol.VideoWidth,
                    SampleProtocol.VideoHeight,
                    SampleProtocol.VideoFps,
                    VideoCodec.H264,
                    track: "video",
                    bitrate: 250_000,
                    gop: 15,
                    kind: VideoEncoderKind.Software);
            }
            catch (MoqException)
            {
                video = null;
            }
        }
        catch
        {
            video?.Dispose();
            preview?.Dispose();
            audio?.Dispose();
            broadcast?.Dispose();
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            client.Dispose();
            throw;
        }

        _client = client;
        _session = session;
        _broadcast = broadcast;
        _audio = audio;
        _preview = preview;
        _video = video;
        VideoEncoderAvailable = video is not null;
    }

    /// <summary>Encodes one PCM frame.</summary>
    public void WritePcm(byte[] payload)
    {
        if (!IsValidForWrite() || !SampleProtocol.IsValidPcm(payload))
        {
            return;
        }

        _audio!.Write(payload, SampleProtocol.TimestampUs());
    }

    /// <summary>Encodes one RGBA picture when the video encoder is available.</summary>
    public void WriteRgba(byte[] payload)
    {
        if (_video is null || !SampleProtocol.IsValidRgba(payload))
        {
            return;
        }

        _video.Write(payload, SampleProtocol.TimestampUs());
    }

    /// <summary>Publishes one JPEG preview frame for receivers that cannot decode H.264.</summary>
    public void WriteJpeg(byte[] payload)
    {
        if (_preview is null || !SampleProtocol.IsValidJpeg(payload))
        {
            return;
        }

        _preview.WriteFrame(payload, SampleProtocol.TimestampUs());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _audio?.Finish();
        _video?.Finish();
        _preview?.Finish();
        _broadcast?.Finish();
        _audio?.Dispose();
        _video?.Dispose();
        _preview?.Dispose();
        _broadcast?.Dispose();
        if (_session is not null)
        {
            try
            {
                await _session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }

        _client?.Dispose();
        _audio = null;
        _video = null;
        _preview = null;
        _broadcast = null;
        _session = null;
        _client = null;
    }

    private bool IsValidForWrite() => _audio is not null;
}
