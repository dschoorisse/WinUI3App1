using System;
using System.IO;
using Canon.Sdk.Exceptions;

namespace Canon.Sdk.Core
{
    /// <summary>
    /// Represents a file on a camera.
    /// </summary>
    public class CameraFile : IDisposable
    {
        private IntPtr _directoryItemRef;
        private bool _disposed = false;

        /// <summary>
        /// Gets the file name.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Gets the file size in bytes.
        /// </summary>
        public UInt64 Size { get; }

        /// <summary>
        /// Gets whether the item is a folder.
        /// </summary>
        public bool IsFolder { get; }

        /// <summary>
        /// Gets the group ID.
        /// </summary>
        public uint GroupId { get; }

        /// <summary>
        /// Gets the file format.
        /// </summary>
        public uint Format { get; }

        /// <summary>
        /// Gets the date and time the file was created.
        /// </summary>
        public DateTime DateTime { get; }

        /// <summary>
        /// Initializes a new instance of the CameraFile class.
        /// </summary>
        /// <param name="directoryItemRef">The directory item reference.</param>
        internal CameraFile(IntPtr directoryItemRef)
        {
            if (directoryItemRef == IntPtr.Zero)
                throw new ArgumentNullException(nameof(directoryItemRef));

            _directoryItemRef = directoryItemRef;

            // Get information about the file
            EDSDKLib.EDSDK.EdsDirectoryItemInfo dirItemInfo;
            uint err = EDSDKLib.EDSDK.EdsGetDirectoryItemInfo(directoryItemRef, out dirItemInfo);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                throw new CanonSdkException("Failed to get directory item info.", err);

            // Set properties from directory item info
            FileName = dirItemInfo.szFileName;
            Size = dirItemInfo.Size;
            IsFolder = dirItemInfo.isFolder != 0;
            GroupId = dirItemInfo.GroupID;
            Format = dirItemInfo.format;

            // Convert date and time
            DateTime = ConvertCanonDateTime(dirItemInfo.dateTime);

            // Retain the directory item
            EDSDKLib.EDSDK.EdsRetain(directoryItemRef);
        }

        /// <summary>
        /// Converts a Canon date/time value to a .NET DateTime.
        /// </summary>
        /// <param name="canonDateTime">The Canon date/time value.</param>
        /// <returns>The .NET DateTime.</returns>
        private DateTime ConvertCanonDateTime(uint canonDateTime)
        {
            try
            {
                // Canon date/time is stored as a 32-bit value
                // The high 16 bits represent the date (year, month, day)
                // The low 16 bits represent the time (hour, minute, second)

                uint date = canonDateTime >> 16;
                uint time = canonDateTime & 0xFFFF;

                int year = (int)(date >> 9) + 1900;
                int month = (int)((date >> 5) & 0xF);
                int day = (int)(date & 0x1F);

                int hour = (int)(time >> 11);
                int minute = (int)((time >> 5) & 0x3F);
                int second = (int)((time & 0x1F) * 2);

                return new DateTime(year, month, day, hour, minute, second);
            }
            catch
            {
                // If there's an error parsing the date/time, return the minimum date value
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Downloads the file to the specified path.
        /// </summary>
        /// <param name="saveToPath">The path to save the file to.</param>
        public void Download(string saveToPath)
        {
            Console.WriteLine($"Downloading {FileName} to {saveToPath}...");

            if (_disposed)
                throw new ObjectDisposedException(nameof(CameraFile));

            if (string.IsNullOrEmpty(saveToPath))
                throw new ArgumentNullException(nameof(saveToPath));

            // Create the directory if it doesn't exist
            string directory = Path.GetDirectoryName(saveToPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Create a file stream to save the image
            IntPtr stream;
            uint err = EDSDKLib.EDSDK.EdsCreateFileStream(saveToPath, EDSDKLib.EDSDK.EdsFileCreateDisposition.CreateAlways,
                EDSDKLib.EDSDK.EdsAccess.ReadWrite, out stream);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                throw new CanonSdkException("Failed to create file stream.", err);

            try
            {
                // Download the image
                err = EDSDKLib.EDSDK.EdsDownload(_directoryItemRef, Size, stream);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                    throw new CanonSdkException("Failed to download file.", err);

                // Complete the download
                err = EDSDKLib.EDSDK.EdsDownloadComplete(_directoryItemRef);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                    throw new CanonSdkException("Failed to complete download.", err);
            }
            catch(Exception ex)
            {
                // Handle exceptions during download
                Console.WriteLine($"Error downloading file: {ex.Message}");
                throw new CanonSdkException("An error occurred while downloading the file.", 0);
            }
            finally
            {
                // Release the stream
                if (stream != IntPtr.Zero)
                    EDSDKLib.EDSDK.EdsRelease(stream);
            }
        }

        /// <summary>
        /// Deletes the file from the camera.
        /// </summary>
        public void Delete()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CameraFile));

            uint err = EDSDKLib.EDSDK.EdsDeleteDirectoryItem(_directoryItemRef);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                throw new CanonSdkException("Failed to delete directory item.", err);
        }

        /// <summary>
        /// Releases the file resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the file resources.
        /// </summary>
        /// <param name="disposing">Whether the method is being called from Dispose() or the finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources
            }

            // Dispose unmanaged resources
            if (_directoryItemRef != IntPtr.Zero)
            {
                EDSDKLib.EDSDK.EdsRelease(_directoryItemRef);
                _directoryItemRef = IntPtr.Zero;
            }

            _disposed = true;
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~CameraFile()
        {
            Dispose(false);
        }
    }
}