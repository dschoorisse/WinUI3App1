// WinUI3App1/CameraService.cs
using Canon.Sdk.Core;
using Canon.Sdk.Events;
using Canon.Sdk.Exceptions;
using EDSDKLib; // Nodig voor EDSDK consten zoals Event-types en Error-codes
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading; // Voor CancellationTokenSource
using System.Threading.Tasks;
using Windows.Storage; // Voor ApplicationData

namespace WinUI3App1
{
    public class CameraService : IDisposable
    {
        private CanonAPI _canonAPI;
        private Camera _activeCamera;
        private CameraList _cameraList;
        private readonly ILogger _logger;
        private bool _isSdkInitialized = false;
        private bool _isDisposed = false;

        public event EventHandler CameraReady;
        public event EventHandler CameraConnectionFailed;
        public event EventHandler CameraDisconnected;
        public event EventHandler<string> CameraErrorOccurred;
        public event EventHandler<string> PhotoSuccessfullyTakenAndDownloaded;

        private TaskCompletionSource<string> _captureTcs;
        private CancellationTokenSource _captureTimeoutCts; // Voor timeout van de capture operatie

        public bool IsCameraAvailable => _activeCamera != null && IsCameraSessionOpen;
        public bool IsCameraSessionOpen { get; private set; } = false;

        public CameraService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.Debug("CameraService: Constructor called.");
        }

        public async Task InitializeAsync()
        {
            if (_isDisposed)
            {
                _logger.Warning("CameraService: InitializeAsync called on a disposed object.");
                return;
            }
            if (_isSdkInitialized)
            {
                _logger.Information("CameraService: SDK already initialized.");
                // Zorg ervoor dat de camera status wordt gecontroleerd, zelfs als de SDK al geïnitialiseerd is
                await RefreshCameraListAndConnectAsync();
                return;
            }

            _logger.Information("CameraService: Initializing Canon SDK...");
            try
            {
                _canonAPI = new CanonAPI();
                _canonAPI.Initialize();
                _isSdkInitialized = true;
                _logger.Information("CameraService: Canon SDK C# Wrapper Initialized.");

                // Deze regel instrueert de Canon SDK (via je CanonApi.cs wrapper)
                // om de methode OnRawCameraDetectedCallback in CameraService.cs aan te roepen
                // telkens wanneer de SDK detecteert dat er een camera is aangesloten (of beschikbaar komt voor de SDK).
                _canonAPI.SetCameraAddedHandler(OnRawCameraDetectedCallback);
                _logger.Information("CameraService: CameraAddedHandler set.");

                await RefreshCameraListAndConnectAsync();
            }
            catch (CanonSdkException ex)
            {
                _logger.Error(ex, "CameraService: SDK Exception during CanonAPI initialization: {ErrorMessage} (Code: 0x{ErrorCode:X})", ex.Message, ex.ErrorCode);
                CameraErrorOccurred?.Invoke(this, $"SDK Init Failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "CameraService: Generic Exception during CanonAPI initialization.");
                CameraErrorOccurred?.Invoke(this, $"SDK Init Error: {ex.Message}");
            }
        }

        private void OnRawCameraDetectedCallback(Camera detectedCamera)
        {
            _logger.Information("CameraService: OnRawCameraDetectedCallback triggered by SDK for camera: {CameraName}", detectedCamera.DeviceInfo?.DeviceDescription ?? "Unknown Camera");
            Task.Run(async () =>
            {
                if (_activeCamera == null)
                {
                    _logger.Information("CameraService: No active camera, attempting to connect to newly detected camera.");
                    // Het is belangrijk om RefreshCameraListAndConnectAsync te gebruiken, omdat die de _cameraList update
                    await RefreshCameraListAndConnectAsync();
                }
                else
                {
                    _logger.Information("CameraService: Active camera already exists. Refreshing list to ensure consistency.");
                    await RefreshCameraListAsync();

                    // Als de actieve camera niet meer in de lijst is, zal RefreshCameraListAsync hem disablen.
                    // Als er geen camera's zijn, of een andere camera is nu de eerste, dan
                    // zou je kunnen overwegen om opnieuw te proberen te verbinden als de actieve camera weg is.
                    if (!IsCameraAvailable && _cameraList != null && _cameraList.Count > 0)
                    {
                        _logger.Information("CameraService: Active camera was lost or changed, attempting to connect to first available.");
                        await ConnectToCameraAsync(_cameraList[0]);
                    }
                }
            });
        }

        private async Task RefreshCameraListAsync()
        {
            if (!_isSdkInitialized || _isDisposed) return;
            _logger.Debug("CameraService: Refreshing camera list...");
            try
            {
                _cameraList = _canonAPI.GetCameraList();
                _logger.Information("CameraService: Found {Count} camera(s).", _cameraList?.Count ?? 0);

                if ((_cameraList == null || _cameraList.Count == 0) && _activeCamera != null)
                {
                    _logger.Warning("CameraService: Active camera is no longer in the device list (list is empty). Likely disconnected.");
                    await DisconnectActiveCameraAsync(notifyUI: true);
                }
                else if (_cameraList != null && _cameraList.Count > 0 && _activeCamera != null)
                {
                    // Check if the current _activeCamera is still in the _cameraList
                    bool stillPresent = false;
                    for (int i = 0; i < _cameraList.Count; i++)
                    {
                        if (_cameraList[i].NativeReference == _activeCamera.NativeReference)
                        {
                            stillPresent = true;
                            break;
                        }
                    }
                    if (!stillPresent)
                    {
                        _logger.Warning("CameraService: Active camera (ref: {ActiveCamRef}) no longer found in refreshed list. Disconnecting.", _activeCamera.NativeReference);
                        await DisconnectActiveCameraAsync(notifyUI: true);
                    }
                }
            }
            catch (CanonSdkException ex)
            {
                _logger.Error(ex, "CameraService: SDK error refreshing camera list: {ErrorMessage} (Code: 0x{ErrorCode:X})", ex.Message, ex.ErrorCode);
                CameraErrorOccurred?.Invoke(this, $"List Error: {ex.Message}");
                if (_activeCamera != null) await DisconnectActiveCameraAsync(notifyUI: true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "CameraService: Generic error refreshing camera list.");
                CameraErrorOccurred?.Invoke(this, $"List Error: {ex.Message}");
                if (_activeCamera != null) await DisconnectActiveCameraAsync(notifyUI: true);
            }
        }

        public async Task RefreshCameraListAndConnectAsync()
        {
            if (!_isSdkInitialized || _isDisposed) return;
            await RefreshCameraListAsync();

            if (_activeCamera == null && _cameraList != null && _cameraList.Count > 0)
            {
                _logger.Information("CameraService: No active camera. Attempting to connect to the first available camera.");
                await ConnectToCameraAsync(_cameraList[0]);
            }
            else if (_activeCamera != null && (_cameraList == null || _cameraList.Count == 0 || !_cameraList.GetCameras().Any(c => c.NativeReference == _activeCamera.NativeReference)))
            {
                _logger.Warning("CameraService: Previously active camera no longer found in list or list is empty. Disconnecting.");
                await DisconnectActiveCameraAsync(notifyUI: true);
            }
            else if (_activeCamera == null && (_cameraList == null || _cameraList.Count == 0))
            {
                _logger.Information("CameraService: No cameras found to connect to.");
                CameraDisconnected?.Invoke(this, EventArgs.Empty); // Expliciet aangeven dat er geen camera is.
            }
        }

        public async Task ConnectToCameraAsync(Camera cameraToConnect)
        {
            if (_isDisposed) return;
            if (cameraToConnect == null)
            {
                _logger.Warning("CameraService: ConnectToCameraAsync called with null camera.");
                return;
            }

            // Voorkom dubbel verbinden of verbinden met een ongeldige referentie
            if (_activeCamera != null && _activeCamera.NativeReference == cameraToConnect.NativeReference && IsCameraSessionOpen)
            {
                _logger.Information("CameraService: Already connected and session open with this camera: {CameraName}", _activeCamera.DeviceInfo?.DeviceDescription);
                CameraReady?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (cameraToConnect.NativeReference == IntPtr.Zero)
            {
                _logger.Error("CameraService: Attempted to connect to a camera with an invalid (zero) native reference.");
                return;
            }


            if (_activeCamera != null)
            {
                _logger.Information("CameraService: Disconnecting previous active camera before connecting to new one.");
                await DisconnectActiveCameraAsync(notifyUI: false);
            }

            _activeCamera = cameraToConnect;
            _logger.Information("CameraService: Attempting to connect to camera: {CameraName}", _activeCamera.DeviceInfo?.DeviceDescription ?? "Unknown Camera");

            try
            {
                _activeCamera.OpenSession();
                IsCameraSessionOpen = true;
                _logger.Information("CameraService: Session opened successfully with {CameraName}.", _activeCamera.DeviceInfo.DeviceDescription);

                _activeCamera.ObjectChanged += OnSdkObjectEventReceived;
                _activeCamera.StateChanged += OnSdkStateEventReceived;
                _activeCamera.PropertyChanged += OnSdkPropertyEventReceived;
                _logger.Debug("CameraService: Subscribed to camera events.");

                // Controleer en stel ImageSaveDestination in. De property in Camera.cs doet dit al.
                if (_activeCamera.ImageSaveDestination != Camera.SaveDestination.Host)
                {
                    _activeCamera.ImageSaveDestination = Camera.SaveDestination.Host;
                }
                _logger.Information("CameraService: ImageSaveDestination is set to {SaveMode}", _activeCamera.ImageSaveDestination);


                CameraReady?.Invoke(this, EventArgs.Empty);
            }
            catch (CanonSdkException ex)
            {
                _logger.Error(ex, "CameraService: SDK error connecting or setting up camera {CameraName}: {ErrorMessage} (Code: 0x{ErrorCode:X})", _activeCamera.DeviceInfo?.DeviceDescription, ex.Message, ex.ErrorCode);
                CameraConnectionFailed?.Invoke(this, EventArgs.Empty);
                CameraErrorOccurred?.Invoke(this, $"Connect Error: {ex.Message}");
                _activeCamera = null;
                IsCameraSessionOpen = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "CameraService: Generic error connecting or setting up camera {CameraName}.", _activeCamera.DeviceInfo?.DeviceDescription);
                CameraConnectionFailed?.Invoke(this, EventArgs.Empty);
                CameraErrorOccurred?.Invoke(this, $"Connect Error: {ex.Message}");
                _activeCamera = null;
                IsCameraSessionOpen = false;
            }
        }

        private async Task DisconnectActiveCameraAsync(bool notifyUI = true)
        {
            if (_isDisposed) return;
            if (_activeCamera != null)
            {
                string camName = _activeCamera.DeviceInfo?.DeviceDescription ?? "Unknown Camera";
                _logger.Information("CameraService: Disconnecting from active camera: {CameraName}", camName);
                IsCameraSessionOpen = false;
                try
                {
                    _activeCamera.ObjectChanged -= OnSdkObjectEventReceived;
                    _activeCamera.StateChanged -= OnSdkStateEventReceived;
                    _activeCamera.PropertyChanged -= OnSdkPropertyEventReceived;
                    _logger.Debug("CameraService: Unsubscribed from camera events for {CameraName}.", camName);

                    _activeCamera.CloseSession();
                    _logger.Information("CameraService: Session closed for {CameraName}.", camName);
                }
                catch (CanonSdkException ex)
                {
                    _logger.Error(ex, "CameraService: SDK error during camera disconnect: {ErrorMessage} (Code: 0x{ErrorCode:X})", ex.Message, ex.ErrorCode);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "CameraService: Generic error during camera disconnect for {CameraName}.", camName);
                }
                finally
                {
                    // De _activeCamera.Dispose() zou de native EdsRelease moeten aanroepen.
                    // Doe dit alleen als het object daadwerkelijk van deze service is en niet
                    // extern beheerd wordt. In dit geval is het van ons.
                    try
                    {
                        _activeCamera.Dispose();
                        _logger.Information("CameraService: Disposed camera object for {CameraName}", camName);
                    }
                    catch (Exception dex)
                    {
                        _logger.Error(dex, "CameraService: Error disposing camera object for {CameraName}", camName);
                    }

                    _activeCamera = null;
                    if (notifyUI)
                    {
                        CameraDisconnected?.Invoke(this, EventArgs.Empty);
                    }
                    _logger.Information("CameraService: Active camera reference nulled ({CameraName}).", camName);
                }
            }
        }

        private void OnSdkObjectEventReceived(object sender, ObjectEventArgs e)
        {
            _logger.Debug("CameraService: OnSdkObjectEventReceived: Type=0x{EventType:X}, Ref=0x{Ref:X}, Handled={IsHandled}", e.EventType, e.ObjectPointer.ToInt64(), e.Handled);
            if (e.EventType == EDSDK.ObjectEvent_DirItemRequestTransfer && !e.Handled)
            {
                _logger.Information("CameraService: DirItemRequestTransfer event received. Flagging to handle download.");
                e.Handled = true;
                Task.Run(async () => await HandleImageDownloadAsync(e.ObjectPointer));
            }
            else if (e.ObjectPointer != IntPtr.Zero && !e.Handled)
            {
                _logger.Verbose("CameraService: Releasing unhandled SDK object event ref: 0x{Ref:X} for event type 0x{EventType:X}", e.ObjectPointer.ToInt64(), e.EventType);
                // EDSDK.EdsRelease(e.ObjectPointer); // De Camera.cs wrapper doet dit nu.
            }
        }

        private async Task HandleImageDownloadAsync(IntPtr dirItemRef)
        {
            if (dirItemRef == IntPtr.Zero)
            {
                _logger.Error("CameraService: HandleImageDownloadAsync called with null dirItemRef.");
                _captureTcs?.TrySetException(new ArgumentNullException(nameof(dirItemRef)));
                return;
            }

            string filePath = null; // Houd het pad bij voor logging, zelfs bij falen

            try
            {
                EDSDK.EdsDirectoryItemInfo dirItemInfo;
                uint err = EDSDK.EdsGetDirectoryItemInfo(dirItemRef, out dirItemInfo);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    throw new CanonSdkException($"Failed to get directory item info (0x{err:X}).", err);
                }

                string fileNameToUse = string.IsNullOrWhiteSpace(dirItemInfo.szFileName) ? $"capture_{Guid.NewGuid()}.jpg" : dirItemInfo.szFileName;
                // Sanitize filename (hoewel GUID meestal veilig is)
                foreach (char invalidChar in Path.GetInvalidFileNameChars())
                {
                    fileNameToUse = fileNameToUse.Replace(invalidChar, '_');
                }

                StorageFolder localCacheFolder = ApplicationData.Current.LocalFolder;
                StorageFolder capturesFolder = await localCacheFolder.CreateFolderAsync("PhotoboothCaptures", CreationCollisionOption.OpenIfExists);
                filePath = Path.Combine(capturesFolder.Path, fileNameToUse);

                _logger.Debug("CameraService: Preparing to download image '{OriginalFileName}' as '{LocalFileName}' to {FilePath}. Size: {Size}", dirItemInfo.szFileName, fileNameToUse, filePath, dirItemInfo.Size);

                using (var fileStreamWrapper = Canon.Sdk.Core.Stream.CreateFileStream(filePath, EDSDK.EdsFileCreateDisposition.CreateAlways, EDSDK.EdsAccess.ReadWrite))
                {
                    err = EDSDK.EdsDownload(dirItemRef, dirItemInfo.Size, fileStreamWrapper.Handle);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        throw new CanonSdkException($"SDK EdsDownload failed (0x{err:X}). Check camera state and card.", err);
                    }
                    _logger.Debug("CameraService: EdsDownload call completed for {FileName}.", fileNameToUse);

                    err = EDSDK.EdsDownloadComplete(dirItemRef);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        _logger.Warning("CameraService: SDK EdsDownloadComplete failed (0x{err:X}) for {FileName}, but download might be partially/fully complete.", err, fileNameToUse);
                    }
                    else
                    {
                        _logger.Debug("CameraService: EdsDownloadComplete call succeeded for {FileName}.", fileNameToUse);
                    }
                } // fileStreamWrapper wordt hier gedisposed

                _logger.Information("CameraService: Image successfully downloaded: {FilePath}", filePath);
                PhotoSuccessfullyTakenAndDownloaded?.Invoke(this, filePath);
                _captureTcs?.TrySetResult(filePath);
            }
            catch (CanonSdkException ex)
            {
                _logger.Error(ex, "CameraService: SDK error during image download to {FilePath}: {ErrorMessage} (Code: 0x{ErrorCode:X})", filePath ?? "Unknown Path", ex.Message, ex.ErrorCode);
                CameraErrorOccurred?.Invoke(this, $"Download Error: {ex.Message}");
                _captureTcs?.TrySetException(ex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "CameraService: Generic error during image download to {FilePath}.", filePath ?? "Unknown Path");
                CameraErrorOccurred?.Invoke(this, $"Download Error: {ex.Message}");
                _captureTcs?.TrySetException(ex);
            }
            finally
            {
                if (dirItemRef != IntPtr.Zero)
                {
                    uint releaseErr = EDSDK.EdsRelease(dirItemRef);
                    _logger.Verbose("CameraService: Released dirItemRef (0x{Ref:X}) in HandleImageDownloadAsync. Release result: 0x{ReleaseResult:X}", dirItemRef.ToInt64(), releaseErr);
                }
            }
        }

        private async void OnSdkStateEventReceived(object sender, StateEventArgs e)
        {
            _logger.Information("CameraService: OnSdkStateEventReceived: Type=0x{EventType:X}, Param=0x{Param:X}", e.EventType, e.Parameter);
            if (e.EventType == EDSDK.StateEvent_Shutdown)
            {
                _logger.Warning("CameraService: Camera shutdown event (StateEvent_Shutdown) received!");
                await DisconnectActiveCameraAsync(notifyUI: true);
            }
            else if (e.EventType == EDSDK.StateEvent_CaptureError)
            {
                _logger.Error("CameraService: Capture Error event (StateEvent_CaptureError) received! SDK Param: 0x{Param:X}", e.Parameter);
                string errorMessage = $"Capture Error (SDK Code: 0x{e.Parameter:X}). Check camera display for details.";
                CameraErrorOccurred?.Invoke(this, errorMessage);
                _captureTcs?.TrySetException(new CanonSdkException(errorMessage, e.Parameter));
            }
            //else if (e.EventType == EDSDK.StateEvent_BulbExposureTime)
            //{
            //    // Voor bulb shooting, niet direct relevant nu
            //    _logger.Debug("CameraService: Bulb exposure time update: {Param}s", e.Parameter);
            //}
            else if (e.EventType == EDSDK.StateEvent_InternalError)
            {
                _logger.Error("CameraService: SDK Internal Error Event! Param: 0x{Param:X}. Consider re-initializing.", e.Parameter);
                CameraErrorOccurred?.Invoke(this, $"SDK Internal Error: 0x{e.Parameter:X}");
                await DisconnectActiveCameraAsync(notifyUI: true); // Disconnect, app might need restart or SDK re-init
            }
        }

        private void OnSdkPropertyEventReceived(object sender, PropertyEventArgs e)
        {
            _logger.Debug("CameraService: OnSdkPropertyEventReceived: Type=0x{EventType:X}, ID=0x{PropertyId:X}, Param=0x{Param:X}", e.EventType, e.PropertyId, e.Parameter);
            if (e.PropertyId == EDSDK.PropID_Unknown) // 0x0000ffff
            {
                _logger.Warning("CameraService: PropertyEvent with Unknown PropertyID. Consider re-querying relevant properties if UI depends on them.");
            }
            // Je kunt hier specifieke properties afhandelen als dat nodig is voor de UI.
        }

        public async Task<string> CapturePhotoAsync(TimeSpan? timeout = null)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(CameraService));
            if (!IsCameraAvailable)
            {
                _logger.Error("CameraService: CapturePhotoAsync called but camera is not available.");
                CameraErrorOccurred?.Invoke(this, "Camera not available for capture.");
                throw new InvalidOperationException("Camera not available for capture.");
            }

            _logger.Information("CameraService: Initiating photo capture sequence...");
            _captureTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Timeout voor de operatie
            timeout ??= TimeSpan.FromSeconds(30); // Default 30s timeout
            _captureTimeoutCts = new CancellationTokenSource();
            var timeoutTask = Task.Delay(timeout.Value, _captureTimeoutCts.Token);

            try
            {
                _logger.Debug("CameraService: Sending TakePicture command...");
                _activeCamera.TakePicture();
                _logger.Information("CameraService: TakePicture command sent. Waiting for download event (timeout: {TimeoutValue}s)...", timeout.Value.TotalSeconds);

                var completedTask = await Task.WhenAny(_captureTcs.Task, timeoutTask);

                if (completedTask == _captureTcs.Task)
                {
                    _captureTimeoutCts.Cancel(); // Annuleer de timeout task, want de capture is gelukt (of gefaald met exception)
                    string imagePath = await _captureTcs.Task; // Haal resultaat op of gooi exception als die gezet is
                    _logger.Information("CameraService: Photo capture and download successful. Image at: {ImagePath}", imagePath);
                    return imagePath;
                }
                else // Timeout
                {
                    _logger.Error("CameraService: Timeout ({TimeoutValue}s) waiting for photo capture/download to complete.", timeout.Value.TotalSeconds);
                    _captureTcs.TrySetCanceled(); // Markeer de TCS als geannuleerd.
                    CameraErrorOccurred?.Invoke(this, $"Timeout ({timeout.Value.TotalSeconds}s) waiting for photo.");
                    throw new TimeoutException($"Timeout waiting for photo capture/download ({timeout.Value.TotalSeconds}s).");
                }
            }
            catch (CanonSdkException ex)
            {
                _logger.Error(ex, "CameraService: SDK error during CapturePhotoAsync: {ErrorMessage} (Code: 0x{ErrorCode:X})", ex.Message, ex.ErrorCode);
                CameraErrorOccurred?.Invoke(this, $"SDK Capture Error: {ex.Message}");
                _captureTcs.TrySetException(ex); // Ensure TCS is completed
                _captureTimeoutCts.Cancel();
                throw;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && _captureTcs.Task.IsCanceled)) // Voorkom dubbel loggen bij timeout
            {
                _logger.Error(ex, "CameraService: Generic error during CapturePhotoAsync.");
                CameraErrorOccurred?.Invoke(this, $"Generic Capture Error: {ex.Message}");
                _captureTcs.TrySetException(ex); // Ensure TCS is completed
                _captureTimeoutCts.Cancel();
                throw;
            }
            finally
            {
                _captureTimeoutCts?.Dispose();
                _captureTimeoutCts = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _logger.Information("CameraService: Disposing...");

            _captureTimeoutCts?.Cancel();
            _captureTimeoutCts?.Dispose();

            if (_captureTcs != null && !_captureTcs.Task.IsCompleted)
            {
                _logger.Information("CameraService: Cancelling an ongoing capture TCS during dispose.");
                _captureTcs.TrySetCanceled();
            }

            // Gebruik GetAwaiter().GetResult() om de async methode synchroon aan te roepen vanuit Dispose.
            // Wees voorzichtig hiermee als DisconnectActiveCameraAsync lang kan duren.
            try
            {
                DisconnectActiveCameraAsync(notifyUI: false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "CameraService: Exception during synchronous DisconnectActiveCameraAsync in Dispose.");
            }


            if (_canonAPI != null && _isSdkInitialized)
            {
                try
                {
                    _logger.Debug("CameraService: Terminating Canon SDK via CanonAPI.Dispose().");
                    _canonAPI.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "CameraService: Exception during CanonAPI.Dispose().");
                }
                _canonAPI = null;
                _isSdkInitialized = false;
            }
            _logger.Information("CameraService: Disposed.");
        }
    }
}