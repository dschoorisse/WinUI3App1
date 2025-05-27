// Path: Canon.Sdk/Core/Stream.cs

using System;
using System.IO;
using static EDSDKLib.EDSDK;

namespace Canon.Sdk.Core
{
    /// <summary>
    /// Represents a stream for file operations with Canon SDK
    /// </summary>
    public class Stream : IDisposable
    {
        private IntPtr _nativePtr;
        private bool _disposed = false;

        /// <summary>
        /// Gets the native handle to the stream
        /// </summary>
        public IntPtr Handle
        {
            get
            {
                EnsureNotDisposed();
                return _nativePtr;
            }
        }

        /// <summary>
        /// Creates a file stream
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <param name="createDisposition">File creation disposition</param>
        /// <param name="access">Access mode</param>
        /// <returns>A Stream object</returns>
        public static Stream CreateFileStream(string filePath, EdsFileCreateDisposition createDisposition, EdsAccess access)
        {
            IntPtr streamPtr;
            uint err = EDSDKLib.EDSDK.EdsCreateFileStream(filePath, createDisposition, access, out streamPtr);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to create file stream", err);
            }

            return new Stream(streamPtr);
        }

        /// <summary>
        /// Creates a memory stream
        /// </summary>
        /// <param name="bufferSize">Size of the buffer in bytes</param>
        /// <returns>A Stream object</returns>
        public static Stream CreateMemoryStream(ulong bufferSize)
        {
            IntPtr streamPtr;
            uint err = EDSDKLib.EDSDK.EdsCreateMemoryStream(bufferSize, out streamPtr);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to create memory stream", err);
            }

            return new Stream(streamPtr);
        }

        /// <summary>
        /// Creates a memory stream from an existing buffer
        /// </summary>
        /// <param name="buffer">The buffer to use</param>
        /// <param name="bufferSize">Size of the buffer in bytes</param>
        /// <returns>A Stream object</returns>
        public static Stream CreateMemoryStreamFromPointer(IntPtr buffer, ulong bufferSize)
        {
            IntPtr streamPtr;
            uint err = EDSDKLib.EDSDK.EdsCreateMemoryStreamFromPointer(buffer, bufferSize, out streamPtr);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to create memory stream from pointer", err);
            }

            return new Stream(streamPtr);
        }

        /// <summary>
        /// Gets the pointer to the data in a memory stream
        /// </summary>
        /// <returns>Pointer to the data</returns>
        public IntPtr GetPointer()
        {
            EnsureNotDisposed();

            IntPtr pointer;
            uint err = EDSDKLib.EDSDK.EdsGetPointer(_nativePtr, out pointer);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to get pointer to stream data", err);
            }

            return pointer;
        }

        /// <summary>
        /// Reads data from the stream
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="bufferSize">Size of the buffer</param>
        /// <returns>The number of bytes read</returns>
        public ulong Read(IntPtr buffer, ulong bufferSize)
        {
            EnsureNotDisposed();

            ulong bytesRead;
            uint err = EDSDKLib.EDSDK.EdsRead(_nativePtr, bufferSize, buffer, out bytesRead);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to read from stream", err);
            }

            return bytesRead;
        }

        /// <summary>
        /// Writes data to the stream
        /// </summary>
        /// <param name="buffer">Buffer to write from</param>
        /// <param name="bufferSize">Number of bytes to write</param>
        /// <returns>The number of bytes written</returns>
        public uint Write(IntPtr buffer, ulong bufferSize)
        {
            EnsureNotDisposed();

            uint bytesWritten;
            uint err = EDSDKLib.EDSDK.EdsWrite(_nativePtr, bufferSize, buffer, out bytesWritten);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to write to stream", err);
            }

            return bytesWritten;
        }

        /// <summary>
        /// Moves the position in the stream
        /// </summary>
        /// <param name="offset">The offset to move by</param>
        /// <param name="origin">The origin to move from</param>
        public void Seek(long offset, EdsSeekOrigin origin)
        {
            EnsureNotDisposed();

            uint err = EDSDKLib.EDSDK.EdsSeek(_nativePtr, offset, origin);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to seek in stream", err);
            }
        }

        /// <summary>
        /// Gets the current position in the stream
        /// </summary>
        /// <returns>The current position</returns>
        public ulong GetPosition()
        {
            EnsureNotDisposed();

            ulong position;
            uint err = EDSDKLib.EDSDK.EdsGetPosition(_nativePtr, out position);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to get stream position", err);
            }

            return position;
        }

        /// <summary>
        /// Gets the length of the stream
        /// </summary>
        /// <returns>The length in bytes</returns>
        public ulong GetLength()
        {
            EnsureNotDisposed();

            ulong length;
            uint err = EDSDKLib.EDSDK.EdsGetLength(_nativePtr, out length);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to get stream length", err);
            }

            return length;
        }

        /// <summary>
        /// Copies data from this stream to another stream
        /// </summary>
        /// <param name="destination">The destination stream</param>
        /// <param name="writeSize">Number of bytes to copy</param>
        public void CopyData(Stream destination, ulong writeSize)
        {
            EnsureNotDisposed();

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.EnsureNotDisposed();

            uint err = EDSDKLib.EDSDK.EdsCopyData(_nativePtr, writeSize, destination._nativePtr);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to copy data between streams", err);
            }
        }

        /// <summary>
        /// Sets a progress callback for stream operations
        /// </summary>
        /// <param name="callback">The callback function</param>
        /// <param name="option">Progress option</param>
        /// <param name="context">User-defined context</param>
        public void SetProgressCallback(EDSDKLib.EDSDK.EdsProgressCallback callback, EdsProgressOption option, IntPtr context)
        {
            EnsureNotDisposed();

            uint err = EDSDKLib.EDSDK.EdsSetProgressCallback(_nativePtr, callback, option, context);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new Exceptions.SdkException("Failed to set progress callback", err);
            }
        }

        /// <summary>
        /// Creates a new instance of Stream
        /// </summary>
        /// <param name="nativePtr">Native pointer to the stream</param>
        internal Stream(IntPtr nativePtr)
        {
            _nativePtr = nativePtr;
        }

        /// <summary>
        /// Gets the native pointer to the stream
        /// </summary>
        public IntPtr NativePtr
        {
            get
            {
                EnsureNotDisposed();
                return _nativePtr;
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        #region IDisposable Implementation

        /// <summary>
        /// Releases resources used by the stream
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources used by the stream
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Release managed resources
                }

                // Release unmanaged resources
                if (_nativePtr != IntPtr.Zero)
                {
                    EDSDKLib.EDSDK.EdsRelease(_nativePtr);
                    _nativePtr = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~Stream()
        {
            Dispose(false);
        }

        #endregion
    }
}