using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MoqNet.Native;

namespace MoqNet.Unity
{
    /// <summary>Produces tracks on a broadcast.</summary>
    public sealed class BroadcastProducer : IDisposable
    {
        private readonly Native.BroadcastProducer _native;

        internal BroadcastProducer(Native.BroadcastProducer native)
        {
            _native = native;
        }

        /// <summary>Returns a consumer of this broadcast.</summary>
        public BroadcastConsumer Consume()
        {
            return new BroadcastConsumer(_native.Consume());
        }

        /// <summary>Publishes a raw byte track.</summary>
        public TrackProducer PublishTrack(string name, TrackOptions options = null)
        {
            return new TrackProducer(_native.PublishTrack(name, options));
        }

        /// <summary>Publishes a JSON snapshot track.</summary>
        public Native.JsonSnapshotProducer PublishJsonSnapshot(string name, uint deltaRatio = 0, bool compression = false)
        {
            return _native.PublishJsonSnapshot(name, deltaRatio, compression);
        }

        /// <summary>Publishes PCM audio that the native library encodes to Opus.</summary>
        public AudioProducer PublishAudio(string name, AudioFormat format, uint sampleRate, uint channels, long bitrate = -1, uint frameDurationMs = 20)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return new AudioProducer(_native.PublishAudio(name, format, sampleRate, channels, bitrate, frameDurationMs));
        }

        /// <summary>Publishes raw video that the native library encodes.</summary>
        public VideoProducer PublishVideo(
            VideoPixelFormat format,
            uint width,
            uint height,
            uint framerate,
            VideoCodec codec = VideoCodec.H264,
            string track = null,
            long bitrate = -1,
            int gop = -1,
            VideoEncoderKind kind = VideoEncoderKind.Auto,
            string kindName = null)
        {
            return new VideoProducer(_native.PublishVideo(format, width, height, framerate, codec, track, bitrate, gop, kind, kindName));
        }

        /// <summary>Finishes the broadcast.</summary>
        public void Finish()
        {
            _native.Finish();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
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
        public string Name
        {
            get { return _native.Name; }
        }

        /// <summary>Writes a timestamped frame.</summary>
        public void WriteFrame(byte[] payload, ulong timestampUs)
        {
            _native.WriteFrame(payload, timestampUs);
        }

        /// <summary>Waits until at least one subscriber is attached.</summary>
        public UniTask UsedAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(() => _native.Used(), cancellationToken: cancellationToken);
        }

        /// <summary>Finishes the track.</summary>
        public void Finish()
        {
            _native.Finish();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
    }

    /// <summary>Consumes tracks from a broadcast.</summary>
    public sealed class BroadcastConsumer : IDisposable
    {
        private readonly Native.BroadcastConsumer _native;

        internal BroadcastConsumer(Native.BroadcastConsumer native)
        {
            _native = native;
        }

        /// <summary>Subscribes to a raw track.</summary>
        public async UniTask<TrackConsumer> SubscribeTrackAsync(string name, SubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var track = await NativeAsync.Call(() => _native.SubscribeTrack(name, options), cancellationToken);
            return new TrackConsumer(track);
        }

        /// <summary>Subscribes to an audio track and decodes PCM.</summary>
        public async UniTask<AudioConsumer> SubscribeAudioAsync(
            string name,
            string codec,
            uint sampleRate,
            uint channelCount,
            AudioFormat format,
            long latencyMaxMs = -1,
            CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            var consumer = await NativeAsync.Call(
                () => _native.SubscribeAudio(name, codec, sampleRate, channelCount, format, latencyMaxMs),
                cancellationToken);
            return new AudioConsumer(consumer);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
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
        public async UniTask<MoqFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() =>
            {
                try
                {
                    _native.Cancel();
                }
                catch (MoqException)
                {
                }
            }))
            {
                return await UniTask.RunOnThreadPool(() => _native.ReadFrame(), cancellationToken: cancellationToken);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
    }

    /// <summary>Publishes PCM that the native library encodes.</summary>
    public sealed class AudioProducer : IDisposable
    {
        private readonly Native.AudioProducer _native;

        internal AudioProducer(Native.AudioProducer native)
        {
            _native = native;
        }

        /// <summary>Audio track name.</summary>
        public string Name
        {
            get { return _native.Name; }
        }

        /// <summary>Writes one PCM frame.</summary>
        public void Write(byte[] payload, ulong timestampUs)
        {
            _native.Write(payload, timestampUs);
        }

        /// <summary>Finishes the audio track.</summary>
        public void Finish()
        {
            _native.Finish();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
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
        public string Name
        {
            get { return _native.Name; }
        }

        /// <summary>Encodes and publishes one raw picture.</summary>
        public void Write(byte[] payload, ulong timestampUs)
        {
            _native.Write(payload, timestampUs);
        }

        /// <summary>Requests a keyframe on the next encoded picture.</summary>
        public void Cut()
        {
            _native.Cut();
        }

        /// <summary>Drains the encoder and finishes the track.</summary>
        public void Finish()
        {
            _native.Finish();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
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
        public async UniTask<MoqFrame?> NextAsync(CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() =>
            {
                try
                {
                    _native.Cancel();
                }
                catch (MoqException)
                {
                }
            }))
            {
                return await UniTask.RunOnThreadPool(() => _native.Next(), cancellationToken: cancellationToken);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
    }
}
