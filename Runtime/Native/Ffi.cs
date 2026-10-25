using System;
using System.Runtime.InteropServices;
using System.Text;
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
        private IntPtr _handle;
        private bool _disposed;

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
        public bool IsDisposed => _disposed;

        private protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
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
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                unsafe
                {
                    Release((void*)handle);
                }
            }
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
    }
}
