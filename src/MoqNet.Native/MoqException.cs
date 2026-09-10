using System;
using System.Runtime.InteropServices;
using MoqNet.Native.FFI;

namespace MoqNet.Native
{
    /// <summary>
    /// Exception thrown when a MoQ native operation fails.
    /// </summary>
    public class MoqException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MoqException"/> class.
        /// </summary>
        public MoqException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MoqException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public MoqException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MoqException"/> class with a specified error message
        /// and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public MoqException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Gets the last error message from the native library for this thread.
        /// </summary>
        /// <returns>The error message, or <c>null</c> if no error is stored.</returns>
        public static unsafe string? GetLastNativeError()
        {
            var errorPtr = NativeMethods.moq_last_error();
            if (errorPtr == null)
            {
                return null;
            }

            return Marshal.PtrToStringUTF8((IntPtr)errorPtr);
        }

        /// <summary>
        /// Creates a <see cref="MoqException"/> with the last native error message appended.
        /// </summary>
        /// <param name="message">The base error message.</param>
        /// <returns>A new exception that includes native error detail when available.</returns>
        public static MoqException FromLastError(string message)
        {
            var nativeError = GetLastNativeError();
            if (string.IsNullOrEmpty(nativeError))
            {
                return new MoqException(message);
            }

            return new MoqException(message + ": " + nativeError);
        }
    }
}
