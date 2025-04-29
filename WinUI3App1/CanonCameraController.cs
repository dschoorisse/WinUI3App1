using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Serilog;

namespace WinUI3App1
{
    // A minimal wrapper for camera operations
    public class CanonCameraController : IDisposable
    {
        #region Fields & Properties

        // Fields
        private readonly ILogger _logger;
        private IntPtr _camera = IntPtr.Zero;
        private bool _isConnected = false;
        private bool _isLiveViewActive = false;
        private string _saveDirectory = null; // Directory to save images
        private readonly GCHandle _callbackHandle;

        // Events
        public event EventHandler<string> ImageSaved;
        public event EventHandler<string> ImageSaveError;

        // Properties for external access
        public bool IsConnected => _isConnected;
        public bool IsLiveViewActive => _isLiveViewActive;
        public string SaveDirectory => _saveDirectory;
        public IntPtr CameraRef => _camera;
        #endregion

        #region Constructor & Disposal
        public CanonCameraController(ILogger logger, string saveDirectory = null)
        {
            _logger = logger;

            // Set save directory, default to Pictures/PhotoBoothApp if not specified
            if (string.IsNullOrEmpty(saveDirectory))
            {
                _saveDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "PhotoBoothApp");
            }
            else
            {
                _saveDirectory = saveDirectory;
            }

            // Create the save directory if it doesn't exist
            if (!Directory.Exists(_saveDirectory))
            {
                Directory.CreateDirectory(_saveDirectory);
            }

            // Create a GCHandle to prevent garbage collection of the callback
            _callbackHandle = GCHandle.Alloc(this);

            _logger.Information("CanonCameraController initialized with save directory: {Directory}", _saveDirectory);
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
        #endregion

        #region Connection Methods
        /// <summary>
        /// Connect to the first available Canon camera
        /// </summary>
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

        /// <summary>
        /// Disconnect from the camera and clean up resources
        /// </summary>
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

        #endregion

        #region Camera Event Handlers
        /// <summary>
        /// Handle camera object events, particularly for image downloads
        /// </summary>
        private uint ObjectEventHandler(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            try
            {
                if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer)
                {
                    _logger.Information("Image ready for download");

                    try
                    {
                        // Get directory item info to get the filename
                        EDSDK.EdsDirectoryItemInfo dirItemInfo;
                        uint err = EDSDK.EdsGetDirectoryItemInfo(inRef, out dirItemInfo);
                        if (err != EDSDK.EDS_ERR_OK)
                        {
                            _logger.Error("Failed to get directory item info: 0x{Error:X}", err);
                            return err;
                        }

                        // Create file path with timestamp
                        string baseFileName = Path.GetFileNameWithoutExtension(dirItemInfo.szFileName);
                        string extension = Path.GetExtension(dirItemInfo.szFileName);
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string newFileName = $"{baseFileName}_{timestamp}{extension}";
                        string filePath = Path.Combine(_saveDirectory, newFileName);

                        // Create file stream for saving the image
                        IntPtr stream = IntPtr.Zero;
                        err = EDSDK.EdsCreateFileStream(filePath, EDSDK.EdsFileCreateDisposition.CreateAlways, EDSDK.EdsAccess.ReadWrite, out stream);
                        if (err != EDSDK.EDS_ERR_OK)
                        {
                            _logger.Error("Failed to create file stream: 0x{Error:X}", err);
                            ImageSaveError?.Invoke(this, $"Failed to create file: {err}");
                            return err;
                        }

                        // Download the image
                        err = EDSDK.EdsDownload(inRef, dirItemInfo.Size, stream);
                        if (err != EDSDK.EDS_ERR_OK)
                        {
                            _logger.Error("Failed to download image: 0x{Error:X}", err);
                            EDSDK.EdsRelease(stream);
                            ImageSaveError?.Invoke(this, $"Failed to download image: {err}");
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

                        // Release the stream
                        EDSDK.EdsRelease(stream);

                        _logger.Information("Image saved successfully to: {FilePath}", filePath);

                        // Trigger event - UI can listen to this to update preview
                        ImageSaved?.Invoke(this, filePath);

                        return EDSDK.EDS_ERR_OK;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Exception in ObjectEventHandler while saving image");
                        ImageSaveError?.Invoke(this, ex.Message);
                    }
                }

                return EDSDK.EDS_ERR_OK;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in ObjectEventHandler");
                return EDSDK.EDS_ERR_OK; // Return OK to avoid SDK issues
            }
        }

        #endregion

        #region Basic Camera Operations

        /// <summary>
        /// Take a picture with the camera
        /// </summary>
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

        /// <summary>
        /// Start Live View mode
        /// </summary>
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

        /// <summary>
        /// Stop Live View mode
        /// </summary>
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

        /// <summary>
        /// Start bulb exposure (long exposure)
        /// </summary>
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

        /// <summary>
        /// End bulb exposure
        /// </summary>
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

        /// <summary>
        /// Press shutter button halfway (for autofocus)
        /// </summary>
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

        /// <summary>
        /// Press shutter button completely (for taking a picture with full control)
        /// </summary>
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

        /// <summary>
        /// Release shutter button
        /// </summary>
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

        #endregion

        #region Camera Settings

        /// <summary>
        /// Set ISO speed
        /// </summary>
        public bool SetIsoSpeed(uint isoValue)
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Setting ISO speed to {ISO}...", GetIsoSpeedString(isoValue));

                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_ISOSpeed, 0, sizeof(uint), isoValue);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set ISO speed: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("ISO speed set successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in SetIsoSpeed");
                return false;
            }
        }

        /// <summary>
        /// Set aperture value
        /// </summary>
        public bool SetAperture(uint apertureValue)
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Setting aperture to {Aperture}...", GetApertureString(apertureValue));

                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Av, 0, sizeof(uint), apertureValue);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set aperture: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Aperture set successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in SetAperture");
                return false;
            }
        }

        /// <summary>
        /// Set shutter speed
        /// </summary>
        public bool SetShutterSpeed(uint shutterSpeedValue)
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Setting shutter speed to {ShutterSpeed}...",
                    GetShutterSpeedString(shutterSpeedValue));

                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Tv, 0, sizeof(uint), shutterSpeedValue);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set shutter speed: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Shutter speed set successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in SetShutterSpeed");
                return false;
            }
        }

        /// <summary>
        /// Get current camera settings (ISO, aperture, shutter speed)
        /// </summary>
        public bool GetCurrentSettings(out uint isoSpeed, out uint aperture, out uint shutterSpeed)
        {
            isoSpeed = 0;
            aperture = 0;
            shutterSpeed = 0;

            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Getting current camera settings...");

                // Get ISO
                uint err = EDSDK.EdsGetPropertyData(_camera, EDSDK.PropID_ISOSpeed, 0, out isoSpeed);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to get ISO speed: 0x{Error:X}", err);
                    return false;
                }

                // Get aperture
                err = EDSDK.EdsGetPropertyData(_camera, EDSDK.PropID_Av, 0, out aperture);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to get aperture: 0x{Error:X}", err);
                    return false;
                }

                // Get shutter speed
                err = EDSDK.EdsGetPropertyData(_camera, EDSDK.PropID_Tv, 0, out shutterSpeed);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to get shutter speed: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Current settings - ISO: {ISO}, Aperture: {Aperture}, Shutter Speed: {ShutterSpeed}",
                    GetIsoSpeedString(isoSpeed),
                    GetApertureString(aperture),
                    GetShutterSpeedString(shutterSpeed));

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in GetCurrentSettings");
                return false;
            }
        }

        /// <summary>
        /// Set exposure compensation
        /// </summary>
        public bool SetExposureCompensation(uint exposureCompValue)
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Setting exposure compensation...");

                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_ExposureCompensation, 0, sizeof(uint), exposureCompValue);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set exposure compensation: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Exposure compensation set successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in SetExposureCompensation");
                return false;
            }
        }

        /// <summary>
        /// Set AE mode (Program, Tv, Av, Manual, etc.)
        /// </summary>
        public bool SetAEMode(uint aeModeValue)
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Setting AE mode...");

                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_AEMode, 0, sizeof(uint), aeModeValue);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set AE mode: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("AE mode set successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in SetAEMode");
                return false;
            }
        }

        /// <summary>
        /// Set image quality
        /// </summary>
        public bool SetImageQuality(uint imageQualityValue)
        {
            if (!_isConnected || _camera == IntPtr.Zero)
            {
                _logger.Error("Camera not connected");
                return false;
            }

            try
            {
                _logger.Information("Setting image quality...");

                uint err = EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_ImageQuality, 0, sizeof(uint), imageQualityValue);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    _logger.Error("Failed to set image quality: 0x{Error:X}", err);
                    return false;
                }

                _logger.Information("Image quality set successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in SetImageQuality");
                return false;
            }
        }

        #endregion

        #region Property Conversion Helpers/// <summary>
        /// Convert aperture value (Av) to F-number string
        /// </summary>
        public static string GetApertureString(uint avValue)
        {
            // These are common aperture values and their corresponding F-numbers
            switch (avValue)
            {
                case 8: return "f/1.0";
                case 11: return "f/1.2";
                case 13: return "f/1.4";
                case 16: return "f/1.8";
                case 19: return "f/2.0";
                case 20: return "f/2.2";
                case 21: return "f/2.5";
                case 24: return "f/2.8";
                case 27: return "f/3.2";
                case 29: return "f/3.5";
                case 32: return "f/4.0";
                case 35: return "f/4.5";
                case 37: return "f/5.0";
                case 40: return "f/5.6";
                case 43: return "f/6.3";
                case 45: return "f/7.1";
                case 48: return "f/8.0";
                case 51: return "f/9.0";
                case 53: return "f/10";
                case 56: return "f/11";
                case 59: return "f/13";
                case 61: return "f/14";
                case 64: return "f/16";
                case 67: return "f/18";
                case 69: return "f/20";
                case 72: return "f/22";
                case 75: return "f/25";
                case 77: return "f/29";
                case 80: return "f/32";
                default: return $"Unknown ({avValue})";
            }
        }

        /// <summary>
        /// Convert shutter speed value (Tv) to exposure time string
        /// </summary>
        public static string GetShutterSpeedString(uint tvValue)
        {
            // These are common shutter speed values and their corresponding exposure times
            switch (tvValue)
            {
                case 12: return "30\"";
                case 15: return "25\"";
                case 16: return "20\"";
                case 19: return "15\"";
                case 20: return "13\"";
                case 21: return "10\"";
                case 24: return "8\"";
                case 27: return "6\"";
                case 28: return "5\"";
                case 29: return "4\"";
                case 32: return "3\"";
                case 35: return "2.5\"";
                case 36: return "2\"";
                case 37: return "1.6\"";
                case 40: return "1.3\"";
                case 43: return "1\"";
                case 45: return "0.8\"";
                case 48: return "0.6\"";
                case 51: return "0.5\"";
                case 53: return "0.4\"";
                case 56: return "0.3\"";
                case 59: return "1/4";
                case 60: return "1/5";
                case 61: return "1/6";
                case 64: return "1/8";
                case 67: return "1/10";
                case 69: return "1/13";
                case 72: return "1/15";
                case 75: return "1/20";
                case 77: return "1/25";
                case 80: return "1/30";
                case 83: return "1/40";
                case 84: return "1/45";
                case 85: return "1/50";
                case 88: return "1/60";
                case 91: return "1/80";
                case 93: return "1/100";
                case 96: return "1/125";
                case 99: return "1/160";
                case 101: return "1/200";
                case 104: return "1/250";
                case 107: return "1/320";
                case 109: return "1/400";
                case 112: return "1/500";
                case 115: return "1/640";
                case 117: return "1/800";
                case 120: return "1/1000";
                case 123: return "1/1250";
                case 125: return "1/1600";
                case 128: return "1/2000";
                case 131: return "1/2500";
                case 133: return "1/3200";
                case 136: return "1/4000";
                case 139: return "1/5000";
                case 141: return "1/6400";
                case 144: return "1/8000";
                default: return $"Unknown ({tvValue})";
            }
        }

        /// <summary>
        /// Convert ISO speed value to ISO string
        /// </summary>
        public static string GetIsoSpeedString(uint isoValue)
        {
            switch (isoValue)
            {
                case 0x00000000: return "Auto";
                case 0x00000028: return "ISO 50";
                case 0x00000030: return "ISO 100";
                case 0x00000038: return "ISO 125";
                case 0x00000040: return "ISO 160";
                case 0x00000048: return "ISO 200";
                case 0x00000050: return "ISO 250";
                case 0x00000058: return "ISO 320";
                case 0x00000060: return "ISO 400";
                case 0x00000068: return "ISO 500";
                case 0x00000070: return "ISO 640";
                case 0x00000078: return "ISO 800";
                case 0x00000080: return "ISO 1000";
                case 0x00000088: return "ISO 1250";
                case 0x00000090: return "ISO 1600";
                case 0x00000098: return "ISO 2000";
                case 0x000000A0: return "ISO 2500";
                case 0x000000A8: return "ISO 3200";
                case 0x000000B0: return "ISO 4000";
                case 0x000000B8: return "ISO 5000";
                case 0x000000C0: return "ISO 6400";
                case 0x000000C8: return "ISO 8000";
                case 0x000000D0: return "ISO 10000";
                case 0x000000D8: return "ISO 12800";
                case 0x000000E0: return "ISO 16000";
                case 0x000000E8: return "ISO 20000";
                case 0x000000F0: return "ISO 25600";
                case 0x000000F8: return "ISO 32000";
                case 0x00000100: return "ISO 40000";
                case 0x00000108: return "ISO 51200";
                case 0x00000110: return "ISO 64000";
                case 0x00000118: return "ISO 80000";
                case 0x00000120: return "ISO 102400";
                default: return $"Unknown ({isoValue})";
            }
        }
        #endregion



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

    }
}