using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MoqNet.Native;

namespace MoqNet.Unity
{
    /// <summary>
    /// Unity MoQ client. Long waits run on the UniTask thread pool.
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

        /// <summary>
        /// Disables TLS certificate verification. Development only; leave off in production.
        /// </summary>
        public void SetTlsDisableVerify(bool disable)
        {
            _native.SetTlsDisableVerify(disable);
        }

        /// <summary>Sets extra TLS root certificate file paths, separated by <c>;</c>.</summary>
        public void SetTlsRoots(string paths)
        {
            _native.SetTlsRoots(paths);
        }

        /// <summary>Pins server certificate SHA-256 fingerprints, separated by <c>;</c>.</summary>
        public void SetTlsFingerprints(string fingerprints)
        {
            _native.SetTlsFingerprints(fingerprints);
        }

        /// <summary>Sets the local bind address.</summary>
        public void SetBind(string addr)
        {
            _native.SetBind(addr);
        }

        /// <summary>Uses an existing origin as the session publisher.</summary>
        public void SetPublish(OriginProducer origin)
        {
            _native.SetPublish(origin != null ? origin.Native : null);
        }

        /// <summary>Uses an existing origin as the session subscriber.</summary>
        public void SetConsume(OriginProducer origin)
        {
            _native.SetConsume(origin != null ? origin.Native : null);
        }

        /// <summary>Connects to a MoQ relay on the thread pool.</summary>
        public async UniTask<Session> ConnectAsync(string url, CancellationToken cancellationToken = default)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            ThrowIfDisposed();
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
                var session = await NativeAsync.Call(() => _native.Connect(url), cancellationToken);
                return new Session(session);
            }
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Client));
            }
        }
    }

    /// <summary>An established MoQ session.</summary>
    public sealed class Session : IDisposable
    {
        private readonly Native.Session _native;
        private bool _disposed;

        internal Session(Native.Session native)
        {
            _native = native;
        }

        /// <summary>Origin used to publish broadcasts.</summary>
        public OriginProducer Publisher
        {
            get { return new OriginProducer(_native.Publisher); }
        }

        /// <summary>Origin used to consume broadcasts.</summary>
        public OriginConsumer Consumer
        {
            get { return new OriginConsumer(_native.Consumer); }
        }

        /// <summary>Gracefully shuts the session down.</summary>
        public UniTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return UniTask.RunOnThreadPool(() => _native.Shutdown(), cancellationToken: cancellationToken);
        }

        /// <summary>Aborts the session with an application error code.</summary>
        public UniTask CancelAsync(uint code = 0, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return UniTask.RunOnThreadPool(() => _native.Cancel(code), cancellationToken: cancellationToken);
        }

        /// <summary>Returns session statistics as JSON.</summary>
        public UniTask<string> StatsJsonAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return UniTask.RunOnThreadPool(() => _native.StatsJson(), cancellationToken: cancellationToken);
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Session));
            }
        }
    }

    /// <summary>
    /// Runs a blocking native call that returns an owned handle on the UniTask thread pool.
    /// The token is not passed to <c>RunOnThreadPool</c>: that would drop the returned handle
    /// if the token fires after the delegate completed.
    /// </summary>
    internal static class NativeAsync
    {
        /// <summary>
        /// Invokes <paramref name="call"/> on the thread pool and disposes the result if the token is cancelled.
        /// </summary>
        /// <typeparam name="T">Owned native handle type.</typeparam>
        /// <param name="call">Blocking native call that returns an owned handle.</param>
        /// <param name="cancellationToken">Checked before the call and after it returns.</param>
        /// <returns>The handle when the call completes without cancellation.</returns>
        internal static async UniTask<T> Call<T>(Func<T> call, CancellationToken cancellationToken)
            where T : IDisposable
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await UniTask.RunOnThreadPool(call);
            if (cancellationToken.IsCancellationRequested)
            {
                result.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            return result;
        }
    }
}
