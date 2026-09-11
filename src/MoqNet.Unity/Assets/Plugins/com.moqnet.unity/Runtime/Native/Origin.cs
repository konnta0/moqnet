using System;
using MoqNet.Native.FFI;

namespace MoqNet.Native
{
    /// <summary>
    /// Produces broadcasts under a MoQ origin.
    /// </summary>
    public sealed class OriginProducer : NativeObject
    {
        /// <summary>
        /// Creates a new origin. Pass a non-negative cache capacity in bytes, or -1 for the default pool.
        /// </summary>
        public OriginProducer(long cacheCapacityBytes = -1)
            : base(Create(cacheCapacityBytes))
        {
        }

        internal OriginProducer(IntPtr handle)
            : base(handle)
        {
        }

        private static unsafe IntPtr Create(long cacheCapacityBytes)
        {
            var ptr = NativeMethods.moq_origin_producer_new(cacheCapacityBytes);
            if (ptr == null)
            {
                throw MoqException.FromLastError("moq_origin_producer_new");
            }

            return (IntPtr)ptr;
        }

        /// <summary>
        /// Returns a consumer handle for this origin.
        /// </summary>
        public unsafe OriginConsumer Consume()
        {
            void* consumer = null;
            Ffi.Check(NativeMethods.moq_origin_producer_consume((void*)Handle, &consumer), "Consume");
            return new OriginConsumer((IntPtr)consumer);
        }

        /// <summary>
        /// Creates and announces a broadcast at <paramref name="path"/>.
        /// </summary>
        public unsafe BroadcastProducer CreateBroadcast(string path)
        {
            Require(path, nameof(path));
            using var utf = Utf8String.Create(path);
            void* broadcast = null;
            Ffi.Check(NativeMethods.moq_origin_producer_create_broadcast((void*)Handle, utf.Ptr, &broadcast), "CreateBroadcast");
            return new BroadcastProducer((IntPtr)broadcast);
        }

        /// <summary>
        /// Accepts on-demand broadcast requests for this origin.
        /// </summary>
        public unsafe OriginDynamic Dynamic()
        {
            void* dynamic = null;
            Ffi.Check(NativeMethods.moq_origin_producer_dynamic((void*)Handle, &dynamic), "Dynamic");
            return new OriginDynamic((IntPtr)dynamic);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_origin_producer_free(handle);
        }
    }

    /// <summary>
    /// Consumes announced broadcasts from an origin.
    /// </summary>
    public sealed class OriginConsumer : NativeObject
    {
        internal OriginConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>
        /// Watches announced broadcasts under <paramref name="prefix"/>.
        /// </summary>
        public unsafe Announced Announced(string prefix)
        {
            Require(prefix, nameof(prefix));
            using var utf = Utf8String.Create(prefix);
            void* announced = null;
            Ffi.Check(NativeMethods.moq_origin_consumer_announced((void*)Handle, utf.Ptr, &announced), "Announced");
            return new Announced((IntPtr)announced);
        }

        /// <summary>
        /// Waits until the broadcast at <paramref name="path"/> is announced.
        /// </summary>
        public unsafe AnnouncedBroadcast AnnouncedBroadcast(string path)
        {
            Require(path, nameof(path));
            using var utf = Utf8String.Create(path);
            void* wait = null;
            Ffi.Check(NativeMethods.moq_origin_consumer_announced_broadcast((void*)Handle, utf.Ptr, &wait), "AnnouncedBroadcast");
            return new AnnouncedBroadcast((IntPtr)wait);
        }

        /// <summary>
        /// Requests a broadcast by path. Blocks until it is available.
        /// </summary>
        public unsafe BroadcastConsumer RequestBroadcast(string path)
        {
            Require(path, nameof(path));
            using var utf = Utf8String.Create(path);
            void* broadcast = null;
            Ffi.Check(NativeMethods.moq_origin_consumer_request_broadcast((void*)Handle, utf.Ptr, &broadcast), "RequestBroadcast");
            return new BroadcastConsumer((IntPtr)broadcast);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_origin_consumer_free(handle);
        }
    }

    /// <summary>
    /// Stream of announced broadcasts.
    /// </summary>
    public sealed class Announced : NativeObject
    {
        internal Announced(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>
        /// Cancels a blocking <see cref="Next"/> call.
        /// </summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_announced_cancel((void*)Handle), "Cancel");
        }

        /// <summary>
        /// Blocks until the next announced broadcast, or returns false when the stream ends.
        /// </summary>
        public unsafe bool Next(out string path, out BroadcastConsumer broadcast)
        {
            byte* pathPtr = null;
            void* broadcastPtr = null;
            var status = NativeMethods.moq_announced_next((void*)Handle, &pathPtr, &broadcastPtr);
            if (Ffi.IsNone(status, "Announced.Next"))
            {
                path = string.Empty;
                broadcast = null!;
                return false;
            }

            path = Ffi.ClaimString(pathPtr) ?? string.Empty;
            broadcast = new BroadcastConsumer((IntPtr)broadcastPtr);
            return true;
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_announced_free(handle);
        }
    }

    /// <summary>
    /// Waits for a single named broadcast to become available.
    /// </summary>
    public sealed class AnnouncedBroadcast : NativeObject
    {
        internal AnnouncedBroadcast(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="Available"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_announced_broadcast_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the broadcast is announced.</summary>
        public unsafe BroadcastConsumer Available()
        {
            void* broadcast = null;
            Ffi.Check(NativeMethods.moq_announced_broadcast_available((void*)Handle, &broadcast), "Available");
            return new BroadcastConsumer((IntPtr)broadcast);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_announced_broadcast_free(handle);
        }
    }

    /// <summary>
    /// Accepts on-demand broadcast production requests.
    /// </summary>
    public sealed class OriginDynamic : NativeObject
    {
        internal OriginDynamic(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="RequestedBroadcast"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_origin_dynamic_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until a subscriber requests a broadcast.</summary>
        public unsafe BroadcastRequest RequestedBroadcast()
        {
            void* request = null;
            Ffi.Check(NativeMethods.moq_origin_dynamic_requested_broadcast((void*)Handle, &request), "RequestedBroadcast");
            return new BroadcastRequest((IntPtr)request);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_origin_dynamic_free(handle);
        }
    }

    /// <summary>
    /// A subscriber request for a broadcast that this origin can accept or abort.
    /// </summary>
    public sealed class BroadcastRequest : NativeObject
    {
        internal BroadcastRequest(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Requested broadcast path.</summary>
        public unsafe string Path
        {
            get
            {
                byte* path = null;
                Ffi.Check(NativeMethods.moq_broadcast_request_path((void*)Handle, &path), "Path");
                return Ffi.ClaimString(path) ?? string.Empty;
            }
        }

        /// <summary>Serves this request with <paramref name="broadcast"/>.</summary>
        public unsafe void Accept(BroadcastProducer broadcast)
        {
            if (broadcast == null)
            {
                throw new ArgumentNullException(nameof(broadcast));
            }

            Ffi.Check(NativeMethods.moq_broadcast_request_accept((void*)Handle, (void*)broadcast.Handle), "Accept");
        }

        /// <summary>Rejects the request with an application error code.</summary>
        public unsafe void Abort(ushort errorCode)
        {
            Ffi.Check(NativeMethods.moq_broadcast_request_abort((void*)Handle, errorCode), "Abort");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_broadcast_request_free(handle);
        }
    }
}
