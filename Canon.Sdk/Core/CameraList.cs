// Path: Canon.Sdk/Core/CameraList.cs

using System;
using System.Collections.Generic;
using Canon.Sdk.Exceptions;
using Canon.Sdk.Logging;

namespace Canon.Sdk.Core
{
    /// <summary>
    /// Represents a list of connected Canon cameras
    /// </summary>
    public class CameraList : IDisposable
    {
        private bool _disposed = false;
        private IntPtr _cameraListRef;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the CameraList class
        /// </summary>
        /// <param name="cameraListRef">The native camera list reference</param>
        internal CameraList(IntPtr cameraListRef, ILogger logger)
        {
            _logger = logger;
            _cameraListRef = cameraListRef;
        }

        /// <summary>
        /// Gets the number of cameras in the list
        /// </summary>
        public int Count
        {
            get
            {
                int count;
                uint err = EDSDKLib.EDSDK.EdsGetChildCount(_cameraListRef, out count);

                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    throw new CanonSdkException("Failed to get camera count", err);
                }

                return count;
            }
        }

        /// <summary>
        /// Gets a camera at the specified index
        /// </summary>
        /// <param name="index">The index of the camera</param>
        /// <returns>The camera</returns>
        public Camera GetCamera(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            IntPtr cameraRef;
            uint err = EDSDKLib.EDSDK.EdsGetChildAtIndex(_cameraListRef, index, out cameraRef);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to get camera at index", err);
            }

            return new Camera(cameraRef, _logger);
        }

        /// <summary>
        /// Gets or sets the camera at the specified index
        /// </summary>
        /// <param name="index">The index of the camera</param>
        /// <returns>The camera at the specified index</returns>
        public Camera this[int index]
        {
            get
            {
                return GetCamera(index);
            }
        }

        /// <summary>
        /// Gets all cameras in the list
        /// </summary>
        /// <returns>An array of cameras</returns>
        public Camera[] GetCameras()
        {
            int count = Count;
            Camera[] cameras = new Camera[count];

            for (int i = 0; i < count; i++)
            {
                cameras[i] = GetCamera(i);
            }

            return cameras;
        }

        #region IDisposable Implementation

        /// <summary>
        /// Releases resources used by the camera list
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources used by the camera list
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
                if (_cameraListRef != IntPtr.Zero)
                {
                    EDSDKLib.EDSDK.EdsRelease(_cameraListRef);
                    _cameraListRef = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~CameraList()
        {
            Dispose(false);
        }

        #endregion
    }
}