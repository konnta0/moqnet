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

        /// <summary>Requests a broadcast by path.</summary>
        public async UniTask<BroadcastConsumer> RequestBroadcastAsync(string path, CancellationToken cancellationToken = default)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            var consumer = await UniTask.RunOnThreadPool(() => _native.RequestBroadcast(path), cancellationToken: cancellationToken);
            return new BroadcastConsumer(consumer);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _native.Dispose();
        }
    }
}
