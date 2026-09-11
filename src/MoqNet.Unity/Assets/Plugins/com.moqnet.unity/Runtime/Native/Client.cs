using System;
using MoqNet.Native.FFI;

namespace MoqNet.Native
{
    /// <summary>
    /// Native QUIC / WebTransport client. Transport stays inside the Rust runtime.
    /// </summary>
    public sealed class Client : NativeObject
    {
        /// <summary>
        /// Creates a client with default TLS verification enabled.
        /// </summary>
        public Client()
            : base(Create())
        {
        }

        private static unsafe IntPtr Create()
        {
            var ptr = NativeMethods.moq_client_new();
            if (ptr == null)
            {
                throw MoqException.FromLastError("moq_client_new");
            }

            return (IntPtr)ptr;
        }

        /// <summary>
        /// Disables TLS certificate verification. Development only; leave off in production.
        /// </summary>
        /// <param name="disable">True to skip verification.</param>
        public unsafe void SetTlsDisableVerify(bool disable)
        {
            Ffi.Check(NativeMethods.moq_client_set_tls_disable_verify((void*)Handle, disable ? (byte)1 : (byte)0), "SetTlsDisableVerify");
        }

        /// <summary>
        /// Sets extra TLS root certificate file paths, separated by <c>;</c>.
        /// </summary>
        public unsafe void SetTlsRoots(string? paths)
        {
            using var utf = Utf8String.Create(paths);
            Ffi.Check(NativeMethods.moq_client_set_tls_roots((void*)Handle, utf.Ptr), "SetTlsRoots");
        }

        /// <summary>
        /// Enables or disables the platform TLS root store.
        /// </summary>
        public unsafe void SetTlsSystemRoots(bool systemRoots)
        {
            Ffi.Check(NativeMethods.moq_client_set_tls_system_roots((void*)Handle, systemRoots ? (byte)1 : (byte)0), "SetTlsSystemRoots");
        }

        /// <summary>
        /// Pins server certificate SHA-256 fingerprints, separated by <c>;</c>.
        /// </summary>
        public unsafe void SetTlsFingerprints(string? fingerprints)
        {
            using var utf = Utf8String.Create(fingerprints);
            Ffi.Check(NativeMethods.moq_client_set_tls_fingerprints((void*)Handle, utf.Ptr), "SetTlsFingerprints");
        }

        /// <summary>
        /// Sets the client TLS certificate file path.
        /// </summary>
        public unsafe void SetTlsCert(string? path)
        {
            using var utf = Utf8String.Create(path);
            Ffi.Check(NativeMethods.moq_client_set_tls_cert((void*)Handle, utf.Ptr), "SetTlsCert");
        }

        /// <summary>
        /// Sets the client TLS private key file path.
        /// </summary>
        public unsafe void SetTlsKey(string? path)
        {
            using var utf = Utf8String.Create(path);
            Ffi.Check(NativeMethods.moq_client_set_tls_key((void*)Handle, utf.Ptr), "SetTlsKey");
        }

        /// <summary>
        /// Sets the local bind address (for example <c>0.0.0.0:0</c>).
        /// </summary>
        public unsafe void SetBind(string addr)
        {
            Require(addr, nameof(addr));
            using var utf = Utf8String.Create(addr);
            Ffi.Check(NativeMethods.moq_client_set_bind((void*)Handle, utf.Ptr), "SetBind");
        }

        /// <summary>
        /// Uses an existing origin as the session publisher.
        /// </summary>
        public unsafe void SetPublish(OriginProducer? origin)
        {
            var ptr = origin == null ? null : (void*)origin.Handle;
            Ffi.Check(NativeMethods.moq_client_set_publish((void*)Handle, ptr), "SetPublish");
        }

        /// <summary>
        /// Uses an existing origin as the session subscriber.
        /// </summary>
        public unsafe void SetConsume(OriginProducer? origin)
        {
            var ptr = origin == null ? null : (void*)origin.Handle;
            Ffi.Check(NativeMethods.moq_client_set_consume((void*)Handle, ptr), "SetConsume");
        }

        /// <summary>
        /// Connects to a MoQ relay. This call blocks on a dedicated native runtime.
        /// </summary>
        /// <param name="url">HTTPS / WebTransport URL of the relay.</param>
        public unsafe Session Connect(string url)
        {
            Require(url, nameof(url));
            using var utf = Utf8String.Create(url);
            void* session = null;
            Ffi.Check(NativeMethods.moq_client_connect((void*)Handle, utf.Ptr, &session), "Connect");
            if (session == null)
            {
                throw MoqException.FromLastError("Connect");
            }

            return new Session((IntPtr)session);
        }

        /// <summary>
        /// Requests that an in-flight <see cref="Connect"/> fail with a cancelled error.
        /// </summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_client_cancel((void*)Handle), "Cancel");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_client_free(handle);
        }
    }

    /// <summary>
    /// An established MoQ session.
    /// </summary>
    public sealed class Session : NativeObject
    {
        internal Session(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>
        /// Origin used to publish broadcasts on this session.
        /// </summary>
        public unsafe OriginProducer Publisher
        {
            get
            {
                void* origin = null;
                Ffi.Check(NativeMethods.moq_session_publisher((void*)Handle, &origin), "Publisher");
                return new OriginProducer((IntPtr)origin);
            }
        }

        /// <summary>
        /// Origin used to consume broadcasts on this session.
        /// </summary>
        public unsafe OriginConsumer Consumer
        {
            get
            {
                void* origin = null;
                Ffi.Check(NativeMethods.moq_session_consumer((void*)Handle, &origin), "Consumer");
                return new OriginConsumer((IntPtr)origin);
            }
        }

        /// <summary>
        /// Gracefully shuts the session down.
        /// </summary>
        public unsafe void Shutdown()
        {
            Ffi.Check(NativeMethods.moq_session_shutdown((void*)Handle), "Shutdown");
        }

        /// <summary>
        /// Aborts the session with an application error code.
        /// </summary>
        public unsafe void Cancel(uint code = 0)
        {
            Ffi.Check(NativeMethods.moq_session_cancel((void*)Handle, code), "Cancel");
        }

        /// <summary>
        /// Returns session statistics as JSON.
        /// </summary>
        public unsafe string StatsJson()
        {
            byte* json = null;
            Ffi.Check(NativeMethods.moq_session_stats_json((void*)Handle, &json), "StatsJson");
            return Ffi.ClaimString(json) ?? "{}";
        }

        /// <summary>
        /// Blocks until the session closes, then throws with the close error.
        /// </summary>
        public unsafe void Closed()
        {
            Ffi.Check(NativeMethods.moq_session_closed((void*)Handle), "Closed");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_session_free(handle);
        }
    }
}
