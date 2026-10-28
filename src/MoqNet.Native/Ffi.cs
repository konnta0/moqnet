using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MoqNet.Native.FFI;

namespace MoqNet.Native
{
    internal static class Ffi
    {
        public const int Ok = 0;
        public const int None = 1;
        public const int Err = -1;

        public static void Check(int status, string operation)
        {
            if (status == Err)
            {
                throw MoqException.FromLastError(operation);
            }

            if (status != Ok && status != None)
            {
                throw MoqException.FromLastError(operation + " (status " + status + ")");
            }
        }

        public static bool IsNone(int status, string operation)
        {
            if (status == Err)
            {
                throw MoqException.FromLastError(operation);
            }

            if (status == None)
            {
                return true;
            }

            if (status == Ok)
            {
                return false;
            }

            throw MoqException.FromLastError(operation + " (status " + status + ")");
        }

        public static unsafe string? ClaimString(byte* ptr)
        {
            if (ptr == null)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUTF8((IntPtr)ptr);
            }
            finally
            {
                NativeMethods.moq_string_free(ptr);
            }
        }

        public static unsafe byte[] ClaimBuffer(byte* ptr, UIntPtr len)
        {
            var length = checked((int)(ulong)len);
            if (ptr == null || length <= 0)
            {
                if (ptr != null)
                {
                    NativeMethods.moq_buffer_free(ptr, len);
                }

                return Array.Empty<byte>();
            }

            var result = new byte[length];
            Marshal.Copy((IntPtr)ptr, result, 0, length);
            NativeMethods.moq_buffer_free(ptr, len);
            return result;
        }
    }

    internal unsafe struct Utf8String : IDisposable
    {
        public byte* Ptr;

        public static Utf8String Create(string? value)
        {
            if (value == null)
            {
                return default;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            var buf = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, buf, bytes.Length);
            Marshal.WriteByte(buf, bytes.Length, 0);
            return new Utf8String { Ptr = (byte*)buf };
        }

        public void Dispose()
        {
            if (Ptr != null)
            {
                Marshal.FreeHGlobal((IntPtr)Ptr);
                Ptr = null;
            }
        }
    }

    /// <summary>
    /// Owned native handle that releases the corresponding Rust object on dispose.
    /// </summary>
    public abstract class NativeObject : IDisposable
    {
        // Number of native calls in progress (low bits) | Disposed | Released.
        // Updated only with Interlocked so the finalizer never blocks.
        private const int Disposed = 1 << 30;
        private const int Released = 1 << 29;
        private const int CallMask = Released - 1;

        private IntPtr _handle;
        private bool _disposed;
        private int _state;

        private protected NativeObject(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new MoqException("native handle was null");
            }

            _handle = handle;
        }

        internal IntPtr Handle
        {
            get
            {
                ThrowIfDisposed();
                return _handle;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this object has been disposed.
        /// </summary>
        public bool IsDisposed => (Volatile.Read(ref _state) & Disposed) != 0;

        private protected void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        /// <summary>
        /// Marks a native call on this handle as in progress until the returned scope is disposed.
        /// Blocking calls keep running on the Rust side after <see cref="Dispose()"/> from another
        /// thread or the finalizer. The last call to return frees the handle instead of freeing
        /// under a live call (use-after-free). Do not wrap calls that copy what they need and
        /// then leave the handle untouched.
        /// </summary>
        /// <returns>A scope that decrements the in-flight count when disposed.</returns>
        private protected CallScope EnterCall()
        {
            while (true)
            {
                var state = Volatile.Read(ref _state);
                if ((state & Disposed) != 0)
                {
                    throw new ObjectDisposedException(GetType().Name);
                }

                if (Interlocked.CompareExchange(ref _state, state + 1, state) == state)
                {
                    return new CallScope(this);
                }
            }
        }

        private void ExitCall()
        {
            if (Interlocked.Decrement(ref _state) == Disposed)
            {
                ReleaseOnce();
            }
        }

        private unsafe void ReleaseOnce()
        {
            if (Interlocked.CompareExchange(ref _state, Disposed | Released, Disposed) != Disposed)
            {
                return;
            }

            var handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                Release((void*)handle);
            }
        }

        private protected static void Require(string? value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the native handle.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        /// <remarks>
        /// When a native call is still in progress, the handle is not freed here; the last call
        /// to return frees it. From the finalizer this means the object is not freed if that
        /// call never returns, which is preferred over freeing memory the call is still using.
        /// </remarks>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            while (true)
            {
                var state = Volatile.Read(ref _state);
                if ((state & Disposed) != 0)
                {
                    return;
                }

                _disposed = true;
                // Keep the handle valid while CancelPendingCalls runs, even if a pending call returns.
                if (Interlocked.CompareExchange(ref _state, (state + 1) | Disposed, state) == state)
                {
                    if ((state & CallMask) != 0)
                    {
                        unsafe
                        {
                            CancelPendingCalls((void*)_handle);
                        }
                    }

                    ExitCall();
                    return;
                }
            }
        }

        /// <summary>
        /// Wakes native calls that are blocked on this handle so they return and the last one frees.
        /// Must not block. Called from <see cref="Dispose()"/> (also the finalizer) while such a call is in progress.
        /// </summary>
        /// <param name="handle">The native handle, valid for the duration of this call.</param>
        private protected virtual unsafe void CancelPendingCalls(void* handle)
        {
        }

        /// <summary>
        /// Frees the native object.
        /// </summary>
        /// <param name="handle">The native handle.</param>
        protected abstract unsafe void Release(void* handle);

        /// <summary>
        /// Finalizer. Native objects should be disposed explicitly; this is a safety net.
        /// </summary>
        ~NativeObject()
        {
            Dispose(false);
        }

        /// <summary>
        /// Ends a native call started with <see cref="EnterCall"/>.
        /// </summary>
        private protected readonly struct CallScope : IDisposable
        {
            private readonly NativeObject _owner;

            internal CallScope(NativeObject owner)
            {
                _owner = owner;
            }

            /// <inheritdoc />
            public void Dispose() => _owner.ExitCall();
        }
    }
}
