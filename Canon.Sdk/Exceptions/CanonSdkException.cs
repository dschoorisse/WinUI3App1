// Path: Canon.Sdk/Exceptions/CanonSdkException.cs

using System;

namespace Canon.Sdk.Exceptions
{
    /// <summary>
    /// Exception thrown when a Canon SDK operation fails
    /// </summary>
    public class CanonSdkException : Exception
    {
        /// <summary>
        /// Gets the error code returned by the Canon SDK
        /// </summary>
        public uint ErrorCode { get; }

        /// <summary>
        /// Initializes a new instance of the CanonSdkException class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code returned by the Canon SDK</param>
        public CanonSdkException(string message, uint errorCode)
            : base($"{message} (Error code: 0x{errorCode:X})")
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the CanonSdkException class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code returned by the Canon SDK</param>
        /// <param name="innerException">The exception that is the cause of the current exception</param>
        public CanonSdkException(string message, uint errorCode, Exception innerException)
            : base($"{message} (Error code: 0x{errorCode:X})", innerException)
        {
            ErrorCode = errorCode;
        }
    }
}