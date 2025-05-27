// Path: Canon.Sdk/Core/LiveViewManager.cs

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Canon.Sdk.Exceptions;

namespace Canon.Sdk.Core
{
    /// <summary>
    /// Manages live view operations for a Canon camera
    /// </summary>
    public class LiveViewManager : IDisposable
    {
        private readonly Camera _camera;
        private bool _isLiveViewActive = false;
        private bool _disposed = false;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _liveViewTask;

        /// <summary>
        /// Initializes a new instance of the LiveViewManager class
        /// </summary>
        /// <param name="camera">The camera to manage live view for</param>
        public LiveViewManager(Camera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        /// <summary>
        /// Event raised when a live view frame is captured
        /// </summary>
        public event EventHandler<LiveViewFrameEventArgs> FrameCaptured;

        /// <summary>
        /// Starts the live view
        /// </summary>
        public void StartLiveView()
        {
            if (_isLiveViewActive)
                return;

            // Set the camera to live view mode
            _camera.SetPropertyData<uint>(EDSDKLib.EDSDK.PropID_Evf_Mode, 1);

            // Set the PC as the output device
            uint device = _camera.GetProperty<uint>(EDSDKLib.EDSDK.PropID_Evf_OutputDevice);
            device |= EDSDKLib.EDSDK.EvfOutputDevice_PC;
            _camera.SetPropertyData<uint>(EDSDKLib.EDSDK.PropID_Evf_OutputDevice, device);

            _isLiveViewActive = true;

            // Start the live view task
            _cancellationTokenSource = new CancellationTokenSource();
            _liveViewTask = Task.Run(() => LiveViewLoop(_cancellationTokenSource.Token));
        }

        /// <summary>
        /// Stops the live view
        /// </summary>
        public void StopLiveView()
        {
            if (!_isLiveViewActive)
                return;

            // Cancel the live view task
            _cancellationTokenSource?.Cancel();
            _liveViewTask?.Wait();

            // Turn off live view on the camera
            uint device = _camera.GetProperty<uint>(EDSDKLib.EDSDK.PropID_Evf_OutputDevice);
            device &= ~EDSDKLib.EDSDK.EvfOutputDevice_PC;
            _camera.SetPropertyData<uint>(EDSDKLib.EDSDK.PropID_Evf_OutputDevice, device);

            _isLiveViewActive = false;
        }

        /// <summary>
        /// Live view frame capture loop
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        private void LiveViewLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Download and process a live view frame
                    DownloadEvfFrame();

                    // Sleep to avoid overloading the camera
                    Thread.Sleep(100);
                }
                catch (OperationCanceledException)
                {
                    // Task was canceled
                    break;
                }
                catch (Exception ex)
                {
                    // Log the exception or handle it as needed
                    System.Diagnostics.Debug.WriteLine($"Error in live view loop: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Downloads and processes a live view frame
        /// </summary>
        private void DownloadEvfFrame()
        {
            IntPtr evfImageRef = IntPtr.Zero;
            IntPtr stream = IntPtr.Zero;

            try
            {
                // Create a memory stream to store the image
                uint err = EDSDKLib.EDSDK.EdsCreateMemoryStream(0, out stream);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                    throw new CanonSdkException("Failed to create memory stream", err);

                // Create an EVF image reference
                err = EDSDKLib.EDSDK.EdsCreateEvfImageRef(stream, out evfImageRef);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                    throw new CanonSdkException("Failed to create EVF image reference", err);

                // Download the live view image
                err = EDSDKLib.EDSDK.EdsDownloadEvfImage(_camera.NativeReference, evfImageRef);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    // Special handling for some errors
                    if (err == EDSDKLib.EDSDK.EDS_ERR_OBJECT_NOTREADY)
                    {
                        // The camera is not ready - this is normal, just wait
                        return;
                    }

                    throw new CanonSdkException("Failed to download EVF image", err);
                }

                // Get information about the frame
                uint zoom = 0;
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImageRef, EDSDKLib.EDSDK.PropID_Evf_Zoom, 0, out zoom);

                EDSDKLib.EDSDK.EdsPoint position;
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImageRef, EDSDKLib.EDSDK.PropID_Evf_ImagePosition, 0, out position);

                EDSDKLib.EDSDK.EdsRect zoomRect;
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImageRef, EDSDKLib.EDSDK.PropID_Evf_ZoomRect, 0, out zoomRect);

                EDSDKLib.EDSDK.EdsRect visibleRect;
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImageRef, EDSDKLib.EDSDK.PropID_Evf_VisibleRect, 0, out visibleRect);

                EDSDKLib.EDSDK.EdsSize sizeJpegLarge;
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImageRef, EDSDKLib.EDSDK.PropID_Evf_CoordinateSystem, 0, out sizeJpegLarge);

                // Extract the image data
                IntPtr imageDataPointer;
                EDSDKLib.EDSDK.EdsGetPointer(stream, out imageDataPointer);

                ulong imageDataLength;
                EDSDKLib.EDSDK.EdsGetLength(stream, out imageDataLength);

                // Create a byte array to hold the image data
                byte[] imageData = new byte[imageDataLength];
                Marshal.Copy(imageDataPointer, imageData, 0, (int)imageDataLength);

                // Create and raise the event
                LiveViewFrameEventArgs args = new LiveViewFrameEventArgs(
                    imageData,
                    zoom,
                    position,
                    zoomRect,
                    visibleRect,
                    sizeJpegLarge);

                FrameCaptured?.Invoke(this, args);
            }
            finally
            {
                // Release resources
                if (evfImageRef != IntPtr.Zero)
                    EDSDKLib.EDSDK.EdsRelease(evfImageRef);

                if (stream != IntPtr.Zero)
                    EDSDKLib.EDSDK.EdsRelease(stream);
            }
        }

        #region IDisposable Implementation

        /// <summary>
        /// Releases resources used by the live view manager
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources used by the live view manager
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Stop live view if it's active
                    if (_isLiveViewActive)
                    {
                        StopLiveView();
                    }

                    // Dispose managed resources
                    _cancellationTokenSource?.Dispose();
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~LiveViewManager()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// Event arguments for a live view frame
    /// </summary>
    public class LiveViewFrameEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the image data
        /// </summary>
        public byte[] ImageData { get; }

        /// <summary>
        /// Gets the zoom factor
        /// </summary>
        public uint Zoom { get; }

        /// <summary>
        /// Gets the image position
        /// </summary>
        public EDSDKLib.EDSDK.EdsPoint Position { get; }

        /// <summary>
        /// Gets the zoom rectangle
        /// </summary>
        public EDSDKLib.EDSDK.EdsRect ZoomRect { get; }

        /// <summary>
        /// Gets the visible rectangle
        /// </summary>
        public EDSDKLib.EDSDK.EdsRect VisibleRect { get; }

        /// <summary>
        /// Gets the size of a JPEG large image
        /// </summary>
        public EDSDKLib.EDSDK.EdsSize SizeJpegLarge { get; }

        /// <summary>
        /// Initializes a new instance of the LiveViewFrameEventArgs class
        /// </summary>
        /// <param name="imageData">The image data</param>
        /// <param name="zoom">The zoom factor</param>
        /// <param name="position">The image position</param>
        /// <param name="zoomRect">The zoom rectangle</param>
        /// <param name="visibleRect">The visible rectangle</param>
        /// <param name="sizeJpegLarge">The size of a JPEG large image</param>
        public LiveViewFrameEventArgs(
            byte[] imageData,
            uint zoom,
            EDSDKLib.EDSDK.EdsPoint position,
            EDSDKLib.EDSDK.EdsRect zoomRect,
            EDSDKLib.EDSDK.EdsRect visibleRect,
            EDSDKLib.EDSDK.EdsSize sizeJpegLarge)
        {
            ImageData = imageData;
            Zoom = zoom;
            Position = position;
            ZoomRect = zoomRect;
            VisibleRect = visibleRect;
            SizeJpegLarge = sizeJpegLarge;
        }
    }
}