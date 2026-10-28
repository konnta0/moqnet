using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MoqNet.Native;

namespace MoqNet.Unity
{
    /// <summary>Produces broadcasts under a MoQ origin.</summary>
    public sealed class OriginProducer : IDisposable
    {
        private readonly Native.OriginProducer _native;

        /// <summary>Creates a new origin.</summary>
        public OriginProducer(long cacheCapacityBytes = -1)
        {
            _native = new Native.OriginProducer(cacheCapacityBytes);
        }

        internal OriginProducer(Native.OriginProducer native)
        {
            _native = native;
        }

        internal Native.OriginProducer Native
        {
            get { return _native; }
        }

        /// <summary>Returns a consumer handle for this origin.</summary>
        public OriginConsumer Consume()
        {
            return new OriginConsumer(_native.Consume());
        }

        /// <summary>Creates and announces a broadcast at <paramref name="path"/>.</summary>
        public BroadcastProducer CreateBroadcast(string path)
        {
            return new BroadcastProducer(_native.CreateBroadcast(path));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
    }

    /// <summary>Consumes announced broadcasts from an origin.</summary>
    public sealed class OriginConsumer : IDisposable
    {
        private readonly Native.OriginConsumer _native;

        internal OriginConsumer(Native.OriginConsumer native)
        {
            _native = native;
        }

        /// <summary>Waits until the broadcast at <paramref name="path"/> is announced.</summary>
        public AnnouncedBroadcast AnnouncedBroadcast(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return new AnnouncedBroadcast(_native.AnnouncedBroadcast(path));
        }

        /// <summary>Requests a broadcast by path. Fails immediately if it is not announced yet.</summary>
        public async UniTask<BroadcastConsumer> RequestBroadcastAsync(string path, CancellationToken cancellationToken = default)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            var consumer = await NativeAsync.Call(() => _native.RequestBroadcast(path), cancellationToken);
            return new BroadcastConsumer(consumer);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
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
        public async UniTask<BroadcastConsumer> AvailableAsync(CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(static state =>
            {
                try
                {
                    ((Native.AnnouncedBroadcast)state).Cancel();
                }
                catch (MoqException)
                {
                }
            }, _native))
            {
                var consumer = await NativeAsync.Call(() => _native.Available(), cancellationToken);
                return new BroadcastConsumer(consumer);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
    }
}
