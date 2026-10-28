using MoqClient = MoqNet.Client.Client;
using MoqSession = MoqNet.Client.Session;
using MoqBroadcastConsumer = MoqNet.Client.BroadcastConsumer;
using MoqAudioConsumer = MoqNet.Client.AudioConsumer;
using MoqTrackConsumer = MoqNet.Client.TrackConsumer;
using MoqException = MoqNet.Native.MoqException;
using MoqFrame = MoqNet.Native.MoqFrame;
using AudioFormat = MoqNet.Native.AudioFormat;

namespace MoqNet.Samples;

/// <summary>
/// Subscribes to the live sample broadcast and raises PCM / JPEG callbacks.
/// </summary>
public sealed class LiveSubscriberSession : IAsyncDisposable
{
    private MoqClient? _client;
    private MoqSession? _session;
    private MoqBroadcastConsumer? _broadcast;
    private CancellationTokenSource? _loopCts;
    private Task? _audioLoop;
    private Task? _previewLoop;

    /// <summary>Raised on a thread-pool thread when a PCM frame arrives.</summary>
    public event Action<byte[]>? PcmReceived;

    /// <summary>Raised on a thread-pool thread when a JPEG preview arrives.</summary>
    public event Action<byte[]>? JpegReceived;

    /// <summary>True after <see cref="StartAsync"/> succeeds.</summary>
    public bool IsConnected => _session is not null;

    /// <summary>Connects and starts read loops.</summary>
    public async Task StartAsync(string url, bool disableTlsVerify, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (_session is not null)
        {
            throw new InvalidOperationException("Subscriber is already connected.");
        }

        var client = new MoqClient();
        MoqSession? session = null;
        MoqBroadcastConsumer? broadcast = null;
        MoqAudioConsumer? audio = null;
        MoqTrackConsumer? preview = null;
        try
        {
            if (disableTlsVerify)
            {
                client.SetTlsDisableVerify(true);
            }

            session = await client.ConnectAsync(url, cancellationToken).ConfigureAwait(false);
            using (var announced = session.Consumer.AnnouncedBroadcast(SampleProtocol.BroadcastPath))
            {
                broadcast = await announced.AvailableAsync(cancellationToken).ConfigureAwait(false);
            }
            audio = await broadcast.SubscribeAudioAsync(
                SampleProtocol.AudioTrack,
                SampleProtocol.AudioCodec,
                SampleProtocol.AudioSampleRate,
                SampleProtocol.AudioChannels,
                AudioFormat.F32,
                latencyMaxMs: 200,
                cancellationToken).ConfigureAwait(false);
            preview = await broadcast.SubscribeTrackAsync(SampleProtocol.PreviewTrack, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            audio?.Dispose();
            preview?.Dispose();
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
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _loopCts.Token;
        _audioLoop = ReadAudioAsync(audio, ct);
        _previewLoop = ReadPreviewAsync(preview, ct);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync().ConfigureAwait(false);
        }

        await AwaitLoopAsync(_audioLoop).ConfigureAwait(false);
        await AwaitLoopAsync(_previewLoop).ConfigureAwait(false);
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
        _loopCts?.Dispose();
        _broadcast = null;
        _session = null;
        _client = null;
    }

    private async Task ReadAudioAsync(MoqAudioConsumer audio, CancellationToken cancellationToken)
    {
        using (audio)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                MoqFrame? frame;
                try
                {
                    frame = await audio.NextAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (MoqException)
                {
                    return;
                }

                if (frame is null)
                {
                    return;
                }

                if (SampleProtocol.IsValidPcm(frame.Value.Payload))
                {
                    PcmReceived?.Invoke(frame.Value.Payload);
                }
            }
        }
    }

    private async Task ReadPreviewAsync(MoqTrackConsumer preview, CancellationToken cancellationToken)
    {
        using (preview)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                MoqFrame? frame;
                try
                {
                    frame = await preview.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (MoqException)
                {
                    return;
                }

                if (frame is null)
                {
                    return;
                }

                if (SampleProtocol.IsValidJpeg(frame.Value.Payload))
                {
                    JpegReceived?.Invoke(frame.Value.Payload);
                }
            }
        }
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
}
