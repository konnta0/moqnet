using System;
using MoqNet.Native.FFI;

namespace MoqNet.Native
{
    /// <summary>
    /// Native QUIC / WebTransport server.
    /// </summary>
    public sealed class Server : NativeObject
    {
        /// <summary>Creates a server with default configuration.</summary>
        public Server()
            : base(Create())
        {
        }

        private static unsafe IntPtr Create()
        {
            var ptr = NativeMethods.moq_server_new();
            if (ptr == null)
            {
                throw MoqException.FromLastError("moq_server_new");
            }

            return (IntPtr)ptr;
        }

        /// <summary>Sets the bind address, for example <c>[::]:443</c> or <c>127.0.0.1:0</c>.</summary>
        public unsafe void SetBind(string addr)
        {
            Require(addr, nameof(addr));
            using var utf = Utf8String.Create(addr);
            Ffi.Check(NativeMethods.moq_server_set_bind((void*)Handle, utf.Ptr), "SetBind");
        }

        /// <summary>Sets TLS certificate file paths, separated by <c>;</c>.</summary>
        public unsafe void SetTlsCert(string? paths)
        {
            using var utf = Utf8String.Create(paths);
            Ffi.Check(NativeMethods.moq_server_set_tls_cert((void*)Handle, utf.Ptr), "SetTlsCert");
        }

        /// <summary>Sets TLS private key file paths, separated by <c>;</c>.</summary>
        public unsafe void SetTlsKey(string? paths)
        {
            using var utf = Utf8String.Create(paths);
            Ffi.Check(NativeMethods.moq_server_set_tls_key((void*)Handle, utf.Ptr), "SetTlsKey");
        }

        /// <summary>Generates a self-signed certificate for the given hostnames, separated by <c>;</c>.</summary>
        public unsafe void SetTlsGenerate(string? hostnames)
        {
            using var utf = Utf8String.Create(hostnames);
            Ffi.Check(NativeMethods.moq_server_set_tls_generate((void*)Handle, utf.Ptr), "SetTlsGenerate");
        }

        /// <summary>Uses an existing origin as the session publisher for accepted connections.</summary>
        public unsafe void SetPublish(OriginProducer? origin)
        {
            var ptr = origin == null ? null : (void*)origin.Handle;
            Ffi.Check(NativeMethods.moq_server_set_publish((void*)Handle, ptr), "SetPublish");
        }

        /// <summary>Uses an existing origin as the session subscriber for accepted connections.</summary>
        public unsafe void SetConsume(OriginProducer? origin)
        {
            var ptr = origin == null ? null : (void*)origin.Handle;
            Ffi.Check(NativeMethods.moq_server_set_consume((void*)Handle, ptr), "SetConsume");
        }

        /// <summary>Binds the server and returns the local address. Blocks while initializing TLS.</summary>
        public unsafe string Listen()
        {
            byte* addr = null;
            Ffi.Check(NativeMethods.moq_server_listen((void*)Handle, &addr), "Listen");
            return Ffi.ClaimString(addr) ?? string.Empty;
        }

        /// <summary>Blocks until the next connection request, or returns null when the listener ends.</summary>
        public unsafe Request? Accept()
        {
            void* request = null;
            var status = NativeMethods.moq_server_accept((void*)Handle, &request);
            if (Ffi.IsNone(status, "Accept"))
            {
                return null;
            }

            return new Request((IntPtr)request);
        }

        /// <summary>Returns served certificate fingerprints as JSON.</summary>
        public unsafe string CertFingerprintsJson()
        {
            byte* json = null;
            Ffi.Check(NativeMethods.moq_server_cert_fingerprints((void*)Handle, &json), "CertFingerprintsJson");
            return Ffi.ClaimString(json) ?? "[]";
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_server_free(handle);
        }
    }

    /// <summary>
    /// An incoming connection that can be accepted or rejected.
    /// </summary>
    public sealed class Request : NativeObject
    {
        internal Request(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Request URL, if the transport provided one.</summary>
        public unsafe string? Url
        {
            get
            {
                byte* url = null;
                Ffi.Check(NativeMethods.moq_request_url((void*)Handle, &url), "Url");
                return Ffi.ClaimString(url);
            }
        }

        /// <summary>Request path.</summary>
        public unsafe string Path
        {
            get
            {
                byte* path = null;
                Ffi.Check(NativeMethods.moq_request_path((void*)Handle, &path), "Path");
                return Ffi.ClaimString(path) ?? string.Empty;
            }
        }

        /// <summary>Query string, if present.</summary>
        public unsafe string? Query
        {
            get
            {
                byte* query = null;
                Ffi.Check(NativeMethods.moq_request_query((void*)Handle, &query), "Query");
                return Ffi.ClaimString(query);
            }
        }

        /// <summary>Transport name.</summary>
        public unsafe string Transport
        {
            get
            {
                byte* transport = null;
                Ffi.Check(NativeMethods.moq_request_transport((void*)Handle, &transport), "Transport");
                return Ffi.ClaimString(transport) ?? string.Empty;
            }
        }

        /// <summary>Accepts the request and returns a session. Blocks on handshake.</summary>
        public unsafe Session Accept()
        {
            void* session = null;
            Ffi.Check(NativeMethods.moq_request_accept((void*)Handle, &session), "Accept");
            return new Session((IntPtr)session);
        }

        /// <summary>Rejects the request with an HTTP-style status code.</summary>
        public unsafe void Reject(ushort code)
        {
            Ffi.Check(NativeMethods.moq_request_reject((void*)Handle, code), "Reject");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_request_free(handle);
        }
    }
}
