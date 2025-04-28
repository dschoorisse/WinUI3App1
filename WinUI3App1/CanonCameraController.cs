using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Serilog;

namespace WinUI3App1
{
    // A minimal wrapper for camera operations
    public class CanonCameraController : IDisposable
    {
        private readonly ILogger _logger;
        private IntPtr _camera = IntPtr.Zero;
        private bool _isConnected = false;
        private bool _isLiveViewActive = false;

        // Context for callbacks
        private readonly GCHandle _callbackHandle;

        public CanonCameraController(ILogger logger)
        {
            _logger = logger;

            // Create a GCHandle to prevent garbage collection of the callback
            _callbackHandle = GCHandle.Alloc(this);

            _logger.Information("CanonCameraController initialized");
        }

        // Connect to the first available camera
        public bool Connect()
        {
            try
            {
                _logger.Information("Connecting to camera...");

                uint err = EDSDK.EdsInitializeSDK();
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to initialize SDK: 0x{Error:X}", err);
                    return false;
                }

                // Get camera list
                IntPtr cameraList = IntPtr.Zero;
                err = EDSDK.EdsGetCameraList(out cameraList);
                if (err != EDSDK.EDS_ERR_OK || cameraList == IntPtr.Zero)
                {
                    _logger.Error("Failed to get camera list: 0x{Error:X}", err);
                    EDSDK.EdsTerminateSDK();
                    return false;
                }

                // Get camera count
                int count = 0;
                err = EDSDK.EdsGetChildCount(cameraList, out count);
                if (err != EDSDK.EDS_ERR_OK || count == 0)
                {
                    _logger.Warning("No cameras found: 0x{Error:X}", err);
                    EDSDK.EdsRelease(cameraList);
                    EDSDK.EdsTerminateSDK();
                    return false;
                }

                _logger.Information("Found {Count} camera(s)", count);

                // Get first camera
                err = EDSDK.EdsGetChildAtIndex(cameraList, 0, out _camera);
                if (err != EDSDK.EDS_ERR_OK || _camera == IntPtr.Zero)
                {
                    _logger.Error("Failed to get camera: 0x{Error:X}", err);
                    EDSDK.EdsRelease(cameraList);
                    EDSDK.EdsTerminateSDK();
                    return false;
                }

                // Release camera list (we have the camera handle now)
                EDSDK.EdsRelease(cameraList);

                // Open session with camera
                err = EDSDK.EdsOpenSession(_camera);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to open session: 0x{Error:X}", err);
                    EDSDK.EdsRelease(_camera);
                    EDSDK.EdsTerminateSDK();
                    return false;
                }

                // Set up event handler for downloaded images
                EDSDK.EdsObjectEventHandler eventHandler = new EDSDK.EdsObjectEventHandler(ObjectEventHandler);
                err = EDSDK.EdsSetObjectEventHandler(_camera, EDSDK.ObjectEvent_All, eventHandler, IntPtr.Zero);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Warning("Failed to set event handler: 0x{Error:X}", err);
                    // Continue anyway, this isn't fatal
                }

                _isConnected = true;
                _logger.Information("Successfully connected to camera");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in Connect method");
                Disconnect();
                return false;
            }
        }

        // Handle object events (like when an image is ready to download)
        private uint ObjectEventHandler(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            try
            {
                if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer)
                {
                    _logger.Information("Image ready for download");

                    // Create a memory stream to hold the image data
                    IntPtr stream = IntPtr.Zero;
                    uint err = EDSDK.EdsCreateMemoryStream(0, out stream);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        _logger.Error("Failed to create memory stream: 0x{Error:X}", err);
                        return err;
                    }

                    // Download the image
                    err = EDSDK.EdsDownload(inRef, 0, stream);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        _logger.Error("Failed to download image: 0x{Error:X}", err);
                        EDSDK.EdsRelease(stream);
                        return err;
                    }

                    // Signal that download is complete
                    err = EDSDK.EdsDownloadComplete(inRef);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        _logger.Error("Failed to complete download: 0x{Error:X}", err);
                        EDSDK.EdsRelease(stream);
                        return err;
                    }

                    // Get image data info
                    uint length = 0;
                    EDSDK.EdsGetLength(stream, out length);
                    _logger.Information("Downloaded image with size: {Size} bytes", length);

                    // TODO: Here you would save the image or process it
                    // For a simple photo booth app, you might want to save it to a file

                    // Release memory stream
                    EDSDK.EdsRelease(stream);
                }

                return EDSDK.EDS_ERR_OK;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in ObjectEventHandler");
                return EDSDK.EDS_ERR_OK; // Return OK to avoid SDK issues
            }
        }

        // Disconnect from camera
        public void Disconnect()
        {
            try
            {
                _logger.Information("Disconnecting from camera...");

                if (_isLiveViewActive)
                {
                    StopLiveView();
                }

                if (_isConnected && _camera != IntPtr.Zero)
                {
                    // Close session
                    uint err = EDSDK.EdsCloseSession(_camera);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        _logger.Warning("Failed to close session: 0x{Error:X}", err);
                    }

                    // Release camera reference
                    EDSDK.EdsRelease(_camera);
                    _camera = IntPtr.Zero;
                }

                // Terminate SDK
                EDSDK.EdsTerminateSDK();

                _isConnected = false;
                _logger.Information("Camera disconnected");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in Disconnect method");
            }
        }

        // Take a picture
        public bool TakePicture()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Taking picture...");

                // Send TakePicture command
                uint err = EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_TakePicture, 0);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    if (err == EDSDK.EDS_ERR_DEVICE_BUSY)
                    {
                        _logger.Warning("Camera is busy, try again later");
                    }
                    else
                    {
                        _logger.Error("Failed to take picture: 0x{Error:X}", err);
                    }
                    return false;
                }

                _logger.Information("Picture taken successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in TakePicture method");
                return false;
            }
        }

        // Start Live View
        public bool StartLiveView()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            if (_isLiveViewActive)
            {
                _logger.Information("Live View is already active");
                return true;
            }

            try
            {
                _logger.Information("Starting Live View...");

                // Set Live View mode to On
                uint evfMode = 1; // On
                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Evf_Mode, 0, sizeof(uint), evfMode);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set Live View mode: 0x{Error:X}", err);
                    return false;
                }

                // Set Live View output device to PC
                uint outDevice = EDSDK.EvfOutputDevice_PC;
                err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), outDevice);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set Live View output device: 0x{Error:X}", err);
                    return false;
                }

                _isLiveViewActive = true;
                _logger.Information("Live View started successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in StartLiveView method");
                return false;
            }
        }

        // Stop Live View
        public bool StopLiveView()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            if (!_isLiveViewActive)
            {
                _logger.Information("Live View is not active");
                return true;
            }

            try
            {
                _logger.Information("Stopping Live View...");

                // Set Live View output device to None
                uint outDevice = 0; // None
                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), outDevice);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set Live View output device: 0x{Error:X}", err);
                    return false;
                }

                _isLiveViewActive = false;
                _logger.Information("Live View stopped successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in StopLiveView method");
                return false;
            }
        }

        // Start bulb exposure (long exposure)
        public bool StartBulbExposure()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Starting bulb exposure...");

                // Send BulbStart command
                uint err = EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_BulbStart, 0);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to start bulb exposure: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Bulb exposure started successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in StartBulbExposure method");
                return false;
            }
        }

        // End bulb exposure
        public bool EndBulbExposure()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Ending bulb exposure...");

                // Send BulbEnd command
                uint err = EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_BulbEnd, 0);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to end bulb exposure: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Bulb exposure ended successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in EndBulbExposure method");
                return false;
            }
        }

        // Press shutter button halfway (for autofocus)
        public bool PressShutterButtonHalfway()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Pressing shutter button halfway...");

                // Send ShutterButton command with Halfway parameter
                uint err = EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Halfway);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to press shutter button halfway: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Shutter button pressed halfway successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in PressShutterButtonHalfway method");
                return false;
            }
        }

        // Press shutter button completely (for taking a picture with full control)
        public bool PressShutterButtonCompletely()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Pressing shutter button completely...");

                // Send ShutterButton command with Completely parameter
                uint err = EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to press shutter button completely: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Shutter button pressed completely successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in PressShutterButtonCompletely method");
                return false;
            }
        }

        // Release shutter button
        public bool ReleaseShutterButton()
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Releasing shutter button...");

                // Send ShutterButton command with OFF parameter
                uint err = EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to release shutter button: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Shutter button released successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in ReleaseShutterButton method");
                return false;
            }
        }

        public async Task<byte[]> DownloadLiveViewFrameAsync()
        {
            if (!_isConnected || !_isLiveViewActive)
                return null;

            try
            {
                // Create memory stream
                IntPtr memoryStream = IntPtr.Zero;
                uint err = EDSDK.EdsCreateMemoryStream(0, out memoryStream);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Warning("Failed to create memory stream: 0x{Error:X}", err);
                    return null;
                }

                // Create EVF image ref
                IntPtr evfImageRef = IntPtr.Zero;
                err = EDSDK.EdsCreateEvfImageRef(memoryStream, out evfImageRef);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Warning("Failed to create EVF image ref: 0x{Error:X}", err);
                    EDSDK.EdsRelease(memoryStream);
                    return null;
                }

                // Download EVF image into evfImageRef
                err = EDSDK.EdsDownloadEvfImage(_camera, evfImageRef);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Warning("Failed to download EVF image: 0x{Error:X}", err);
                    EDSDK.EdsRelease(evfImageRef);
                    EDSDK.EdsRelease(memoryStream);
                    return null;
                }

                // Get stream length
                uint length = 0;
                err = EDSDK.EdsGetLength(memoryStream, out length);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Warning("Failed to get stream length: 0x{Error:X}", err);
                    EDSDK.EdsRelease(evfImageRef);
                    EDSDK.EdsRelease(memoryStream);
                    return null;
                }

                byte[] buffer = new byte[length];
                IntPtr pointer;
                err = EDSDK.EdsGetPointer(memoryStream, out pointer);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Warning("Failed to get stream pointer: 0x{Error:X}", err);
                    EDSDK.EdsRelease(evfImageRef);
                    EDSDK.EdsRelease(memoryStream);
                    return null;
                }

                Marshal.Copy(pointer, buffer, 0, (int)length);

                // Clean up
                EDSDK.EdsRelease(evfImageRef);
                EDSDK.EdsRelease(memoryStream);

                return buffer;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error downloading LiveView frame");
                return null;
            }
        }


        // IDisposable implementation
        public void Dispose()
        {
            Disconnect();

            if (_callbackHandle.IsAllocated)
            {
                _callbackHandle.Free();
            }
        }
    }
}