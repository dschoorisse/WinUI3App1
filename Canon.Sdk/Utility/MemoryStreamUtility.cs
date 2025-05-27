using System;
using System.IO;
using System.Runtime.InteropServices;
using Canon.Sdk.Exceptions;

namespace Canon.Sdk.Utility
{
    /// <summary>
    /// Provides utility methods for working with memory streams.
    /// </summary>
    public static class MemoryStreamUtility
    {
        /// <summary>
        /// Creates a managed memory stream from a native EDS memory stream.
        /// </summary>
        /// <param name="streamRef">The EDS memory stream.</param>
        /// <returns>A managed memory stream.</returns>
        public static MemoryStream FromEdsStreamRef(IntPtr streamRef)
        {
            if (streamRef == IntPtr.Zero)
                throw new ArgumentNullException(nameof(streamRef));

            // Get the stream pointer and length
            IntPtr streamPtr;
            uint err = EDSDKLib.EDSDK.EdsGetPointer(streamRef, out streamPtr);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                throw new CanonSdkException("Failed to get pointer to stream data.", err);

            UInt64 length;
            err = EDSDKLib.EDSDK.EdsGetLength(streamRef, out length);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                throw new CanonSdkException("Failed to get stream length.", err);

            // Copy the data to a byte array
            byte[] data = new byte[(int)length];
            Marshal.Copy(streamPtr, data, 0, (int)length);

            // Create a managed memory stream
            return new MemoryStream(data);
        }

        /// <summary>
        /// Creates a native EDS memory stream from a managed memory stream.
        /// </summary>
        /// <param name="stream">The managed memory stream.</param>
        /// <returns>A native EDS memory stream.</returns>
        public static IntPtr ToEdsStreamRef(MemoryStream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Create a native EDS memory stream
            IntPtr streamRef;
            uint err = EDSDKLib.EDSDK.EdsCreateMemoryStream((UInt64)stream.Length, out streamRef);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                throw new CanonSdkException("Failed to create memory stream.", err);

            try
            {
                // Get the stream pointer
                IntPtr streamPtr;
                err = EDSDKLib.EDSDK.EdsGetPointer(streamRef, out streamPtr);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                    throw new CanonSdkException("Failed to get pointer to stream data.", err);

                // Copy the data to the native stream
                byte[] data = stream.ToArray();
                Marshal.Copy(data, 0, streamPtr, data.Length);

                return streamRef;
            }
            catch
            {
                // Release the stream if an error occurs
                if (streamRef != IntPtr.Zero)
                    EDSDKLib.EDSDK.EdsRelease(streamRef);

                throw;
            }
        }
    }
}