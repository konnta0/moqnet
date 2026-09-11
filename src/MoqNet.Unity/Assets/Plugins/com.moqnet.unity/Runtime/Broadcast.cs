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

            var track = await UniTask.RunOnThreadPool(() => _native.SubscribeTrack(name, options), cancellationToken: cancellationToken);
            return new TrackConsumer(track);
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
}
