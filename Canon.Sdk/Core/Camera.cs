﻿// Path: Canon.Sdk/Core/Camera.cs

using System;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading; // Toegevoegd voor Thread.Sleep
using Canon.Sdk.Events;
using Canon.Sdk.Exceptions;
using Canon.Sdk.Logging;
using EDSDKLib;

namespace Canon.Sdk.Core
{
    /// <summary>
    /// Represents a Canon camera device
    /// </summary>
    public class Camera : IDisposable
    {
        private bool _disposed = false;
        private IntPtr _cameraRef;
        private CameraEventHandler _eventHandler; // Wordt geïnitialiseerd in de constructor
        private DeviceInfo _deviceInfo;
        private GCHandle _gcHandle;
        private readonly ILogger _logger;

        // Event delegates
        private EDSDKLib.EDSDK.EdsPropertyEventHandler _propertyEventHandler;
        private EDSDKLib.EDSDK.EdsObjectEventHandler _objectEventHandler;
        private EDSDKLib.EDSDK.EdsStateEventHandler _stateEventHandler;

        // Public events
        public event EventHandler<PropertyEventArgs> PropertyChanged;
        public event EventHandler<ObjectEventArgs> ObjectChanged;
        public event EventHandler<StateEventArgs> StateChanged;

        /// <summary>
        /// Gets the native camera reference
        /// </summary>
        public IntPtr NativeReference => _cameraRef;

        /// <summary>
        /// Gets device information about this camera
        /// </summary>
        public DeviceInfo DeviceInfo
        {
            get
            {
                _logger.Verbose("CanonSdk.Core.Camera: Accessing DeviceInfo property...");

                if (_disposed)
                    throw new ObjectDisposedException(nameof(Camera));
                if (_cameraRef == IntPtr.Zero)
                    throw new InvalidOperationException("Camera reference is not valid.");

                if (_deviceInfo == null)
                {

                    _logger.Verbose("CanonSdk.Core.Camera: DeviceInfo property is null, retrieving from camera now...");

                    EDSDKLib.EDSDK.EdsDeviceInfo nativeDeviceInfo;
                    uint err = EDSDKLib.EDSDK.EdsGetDeviceInfo(_cameraRef, out nativeDeviceInfo);

                    if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                    {
                        throw new CanonSdkException("Failed to get device information", err);
                    }
                    _deviceInfo = new DeviceInfo(nativeDeviceInfo);
                }
                return _deviceInfo;
            }
        }

        internal Camera(IntPtr cameraRef, ILogger logger)
        {
            _logger = logger;

            if (cameraRef == IntPtr.Zero)
                throw new ArgumentNullException(nameof(cameraRef), "Camera reference cannot be zero.");

            _cameraRef = cameraRef;
            _eventHandler = new CameraEventHandler(this); // Moet _cameraRef hebben voor registratie

            _gcHandle = GCHandle.Alloc(this);


            _logger.Verbose("CanonSdk.Core.Camera: Registering event handlers...");
            _propertyEventHandler = new EDSDKLib.EDSDK.EdsPropertyEventHandler(HandlePropertyEvent);
            _objectEventHandler = new EDSDKLib.EDSDK.EdsObjectEventHandler(HandleObjectEvent);
            _stateEventHandler = new EDSDKLib.EDSDK.EdsStateEventHandler(HandleStateEvent);

            SetupEventHandlers(); // Registreert de handlers bij de SDK
        }

        /// <summary>
        /// Sends a generic command to the camera.
        /// This is a low-level abstraction and should be used by more specific methods if possible.
        /// </summary>
        /// <param name="command">The command ID to send (e.g., EDSDK.CameraCommand_TakePicture).</param>
        /// <param name="parameter">The parameter for the command (default is 0).</param>
        /// <exception cref="ObjectDisposedException">Thrown if the camera object is disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the camera reference is not valid.</exception>
        /// <exception cref="CanonSdkException">Thrown if the SDK command fails.</exception>
        public void SendCommand(uint command, int parameter = 0)
        {
            _logger.Verbose($"CanonSdk.Core.Camera: SendCommand called, sending command {command} with parameter {parameter}...");

            if (_disposed)
                throw new ObjectDisposedException(nameof(Camera));
            if (_cameraRef == IntPtr.Zero)
                throw new InvalidOperationException("Camera reference is not valid (likely not connected or session not open).");

            uint err = EDSDKLib.EDSDK.EdsSendCommand(_cameraRef, command, parameter);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                _logger.Warning($"CanonSdk.Core.Camera: SendCommand failed, sending command {command} with parameter {parameter} but got error {err}...");
                throw new CanonSdkException($"Failed to send command 0x{command:X} with parameter {parameter}", err);
            }
        }

        public string GetModelName()
        {
            _logger.Verbose($"CanonSdk.Core.Camera: GetModelName called...");

            // Gebruikt GetProperty die intern EdsGetPropertyData aanroept
            return GetProperty<string>(EDSDKLib.EDSDK.PropID_ProductName);
        }

        public void OpenSession()
        {
            _logger.Verbose($"CanonSdk.Core.Camera: Opening camera session...");

            if (_disposed) throw new ObjectDisposedException(nameof(Camera));
            if (_cameraRef == IntPtr.Zero) throw new InvalidOperationException("Camera reference is not valid.");

            uint err = EDSDKLib.EDSDK.EdsOpenSession(_cameraRef);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to open session with camera", err);
            }

            SetSaveToHost(); // Essentieel na openen sessie
            _logger.Verbose("CanonSdk.Core.Camera: Session opened and configured successfully.");
        }

        private void SetSaveToHost()
        {

            _logger.Verbose($"CanonSdk.Core.Camera: SetSaveToHost called...");

            // Deze methode gebruikt direct EdsSetPropertyData en EdsSetCapacity,
            // wat prima is omdat het specifieke property-instellingen zijn, geen generieke commando's.
            // Je zou SetPropertyData<uint>(...) kunnen gebruiken als je die methode ook aanpast
            // om de sizeof(uint) correct te gebruiken ipv Marshal.SizeOf(data).
            // Voor nu laten we het zoals het was, omdat het een property-set is, geen command.
            try
            {
                uint saveToValue = (uint)EDSDKLib.EDSDK.EdsSaveTo.Host;
                uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_cameraRef, EDSDKLib.EDSDK.PropID_SaveTo, 0, sizeof(uint), saveToValue);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    _logger.Error($"CRITICAL ERROR: Failed to set PropID_SaveTo to Host. SDK Error: 0x{err:X}");
                    throw new CanonSdkException($"Failed to set save location (PropID_SaveTo) to host. SDK Error: 0x{err:X}", err);
                }
                _logger.Verbose("CanonSdk.Core.Camera: Successfully set PropID_SaveTo to Host.");

                EDSDKLib.EDSDK.EdsCapacity capacity = new EDSDKLib.EDSDK.EdsCapacity
                {
                    NumberOfFreeClusters = 0x7FFFFFFF,
                    BytesPerSector = 0x1000,
                    Reset = 1
                };

                err = EDSDKLib.EDSDK.EdsSetCapacity(_cameraRef, capacity);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    _logger.Error($"CRITICAL ERROR: Failed to set capacity for host saving. SDK Error: 0x{err:X}");
                    throw new CanonSdkException($"Failed to set capacity for host saving. SDK Error: 0x{err:X}", err);
                }
                _logger.Verbose("CanonSdk.Core.Camera: Successfully set capacity for host saving.");
            }
            catch (CanonSdkException ex)
            {
                _logger.Warning($"CanonSdk.Core.Camera: CanonSdkException in SetSaveToHost: {ex.Message} (SDK Error: 0x{ex.ErrorCode:X})");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning($"CanonSdk.Core.Camera: Generic error in SetSaveToHost: {ex.Message}");
                throw new Exception("Failed to configure camera for host saving.", ex);
            }
        }

        public void CloseSession()
        {
            _logger.Verbose($"CanonSdk.Core.Camera: Closing camera session...");

            if (_disposed) throw new ObjectDisposedException(nameof(Camera));
            if (_cameraRef == IntPtr.Zero) return; // Sessie kan niet gesloten worden als er geen ref is

            uint err = EDSDKLib.EDSDK.EdsCloseSession(_cameraRef);
            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                _logger.Verbose("CanonSdk.Core.Camera: Session closed successfully.");
            }
            else
            {
                // Loggen is hier beter dan een harde throw, omdat dit vaak in Dispose wordt aangeroepen
                _logger.Warning($"CanonSdk.Core.Camera: Failed to close session with camera. SDK Error: 0x{err:X}");
                // throw new CanonSdkException("Failed to close session with camera", err);
            }
        }

        public void TakePicture()
        {
            _logger.Verbose("CanonSdk.Core.Camera: TakePicture called, preparing to send shutter commands...");
            try
            {
                _logger.Verbose("CanonSdk.Core.Camera: Sending ShutterButton_Completely via SendCommand...");
                SendCommand(EDSDKLib.EDSDK.CameraCommand_PressShutterButton, (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely);
                _logger.Verbose("CanonSdk.Core.Camera: ShutterButton_Completely command successful.");

                Thread.Sleep(100); // Korte pauze, essentieel voor sommige camera's om het commando te verwerken
                                   // voordat het release commando komt. 500ms was in je oude code, 100ms kan ook werken. Test dit.
            }
            finally
            {
                _logger.Verbose("CanonSdk.Core.Camera: Sending ShutterButton_OFF command to release the shutter button...");
                try
                {
                    SendCommand(EDSDKLib.EDSDK.CameraCommand_PressShutterButton, (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF);
                    _logger.Verbose("CanonSdk.Core.Camera: ShutterButton_OFF command sent successfully.");
                }
                catch (CanonSdkException ex)
                {
                    // Dit is problematisch als de knop "ingedrukt" blijft.
                    _logger.Error($"CanonSdk.Core.Camera: Failed to send ShutterButton_OFF after TakePicture. SDK Error: 0x{ex.ErrorCode:X} - {ex.Message}", ex);
                    // Overweeg hier verdere actie, hoewel de initiële foto mogelijk al genomen is.
                }
            }
            _logger.Verbose("CanonSdk.Core.Camera: Exiting TakePicture method.");
        }

        public void ExtendShutdownTimer()
        {
            _logger.Verbose("CanonSdk.Core.Camera: ExtendShutdownTimer called, sending ExtendShutDownTimer command...");
            SendCommand(EDSDKLib.EDSDK.CameraCommand_ExtendShutDownTimer, 0);
            _logger.Verbose("CanonSdk.Core.Camera: ExtendShutdownTimer command sent successfully.");
        }

        public void SyncClock()
        {
            _logger.Verbose("CanonSdk.Core.Camera: SyncClock called, synchronizing camera clock with system time...");
            if (_disposed) throw new ObjectDisposedException(nameof(Camera));

            DateTime now = DateTime.Now;
            EDSDK.EdsTime edsTime = new EDSDK.EdsTime
            {
                Year = now.Year,
                Month = now.Month,
                Day = now.Day,
                Hour = now.Hour,
                Minute = now.Minute,
                Second = now.Second,
                Milliseconds = 0 // Milliseconden worden meestal niet ondersteund
            };
            _logger.Verbose($"CanonSdk.Core.Camera: Setting camera time to {edsTime.Year}-{edsTime.Month}-{edsTime.Day} {edsTime.Hour}:{edsTime.Minute}:{edsTime.Second}...");

            // Gebruik SetPropertyData met de EdsTime struct
            SetPropertyData(EDSDK.PropID_DateTime, edsTime);
        }

        #region Abstracted Properties
        // ... (ProductName, FirmwareVersion, BatteryLevel, IsEvfOutputToPcEnabled, IsFlashEnabled, ImageSaveDestination, AeMode, IsoSpeed, etc. blijven hetzelfde) ...
        // Deze gebruiken GetProperty<T> en SetPropertyData<T> wat correct is voor properties.

        public string ProductName => GetProperty<string>(EDSDK.PropID_ProductName);
        public string FirmwareVersion => GetProperty<string>(EDSDK.PropID_FirmwareVersion);
        public int BatteryLevel
        {
            get
            {
                try
                {
                    uint batteryLevel = GetProperty<uint>(EDSDK.PropID_BatteryLevel);
                    if (batteryLevel == EDSDK.BatteryLevel_AC) return -1;
                    return Math.Clamp((int)batteryLevel, 0, 100);
                }
                catch (CanonSdkException ex) when (ex.ErrorCode == EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE)
                {
                    _logger.Warning($"CanonSdk.Core.Camera: Battery level property unavailable. SDK Error: 0x{ex.ErrorCode:X}");
                    return -1;
                }
                catch (Exception ex)
                {
                    _logger.Error($"CanonSdk.Core.Camera: Error getting BatteryLevel: {ex.Message}", ex);
                    return -1;
                }
            }
        }
        public bool IsEvfOutputToPcEnabled
        {
            get
            {
                uint outputDevice = GetProperty<uint>(EDSDK.PropID_Evf_OutputDevice);
                return (outputDevice & EDSDK.EvfOutputDevice_PC) != 0;
            }
            set
            {
                uint currentOutputDevice = GetProperty<uint>(EDSDK.PropID_Evf_OutputDevice);
                uint newOutputDevice = value ? (currentOutputDevice | EDSDK.EvfOutputDevice_PC) : (currentOutputDevice & ~EDSDK.EvfOutputDevice_PC);
                if (newOutputDevice != currentOutputDevice)
                {
                    _logger.Verbose($"Setting EvfOutputDevice from {currentOutputDevice} to {newOutputDevice}.");
                    SetPropertyData(EDSDK.PropID_Evf_OutputDevice, newOutputDevice);
                }
            }
        }
        public bool IsFlashEnabled
        {
            get
            {
                try
                {
                    uint flashStatus = GetProperty<uint>(EDSDK.PropID_FlashOn);
                    return flashStatus != 0;
                }
                catch (CanonSdkException ex) when (ex.ErrorCode == EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE || ex.ErrorCode == EDSDK.EDS_ERR_NOT_SUPPORTED)
                {
                    _logger.Warning($"FlashOn property unavailable or not supported (0x{ex.ErrorCode:X}). Returning false.");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error getting IsFlashEnabled: {ex.Message}", ex);
                    return false;
                }
            }
            set
            {
                uint flashValue = value ? 1u : 0u;
                try
                {
                    SetPropertyData(EDSDK.PropID_FlashOn, flashValue);
                }
                catch (CanonSdkException ex) when (ex.ErrorCode == EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE || ex.ErrorCode == EDSDK.EDS_ERR_NOT_SUPPORTED)
                {
                    _logger.Warning($"Cannot set FlashOn property, it is unavailable or not supported (0x{ex.ErrorCode:X}).");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error setting IsFlashEnabled: {ex.Message}", ex);
                }
            }
        }
        public enum SaveDestination : uint
        {
            Camera = EDSDK.EdsSaveTo.Camera,
            Host = EDSDK.EdsSaveTo.Host,
            Both = EDSDK.EdsSaveTo.Both
        }
        public SaveDestination ImageSaveDestination
        {
            get
            {
                uint saveToValue = GetProperty<uint>(EDSDK.PropID_SaveTo);
                return (SaveDestination)saveToValue;
            }
            set
            {
                _logger.Verbose($"Setting ImageSaveDestination to {value}.");
                uint saveToValue = (uint)value;
                SetPropertyData(EDSDK.PropID_SaveTo, saveToValue);
                if (value == SaveDestination.Host || value == SaveDestination.Both)
                {
                    SetHostCapacity();
                }
            }
        }
        public uint AeMode => GetProperty<uint>(EDSDK.PropID_AEMode);
        public uint IsoSpeed => GetProperty<uint>(EDSDK.PropID_ISOSpeed);
        public uint ApertureValue => GetProperty<uint>(EDSDK.PropID_Av);
        public uint ShutterSpeedValue => GetProperty<uint>(EDSDK.PropID_Tv);
        public uint AvailableShots => GetProperty<uint>(EDSDK.PropID_AvailableShots);
        public uint ImageQuality => GetProperty<uint>(EDSDK.PropID_ImageQuality);

        // Nieuw toegevoegde properties
        public uint Orientation => GetProperty<uint>(EDSDK.PropID_Orientation);
        public EDSDK.EdsFocusInfo FocusInfo => GetProperty<EDSDK.EdsFocusInfo>(EDSDK.PropID_FocusInfo);
        public uint AEMode => GetProperty<uint>(EDSDK.PropID_AEMode);
        public uint ISOSpeed => GetProperty<uint>(EDSDK.PropID_ISOSpeed);
        public uint AFMode => GetProperty<uint>(EDSDK.PropID_AFMode);
        public uint ExposureCompensation => GetProperty<uint>(EDSDK.PropID_ExposureCompensation);
        public EDSDK.EdsRational FocalLength => GetProperty<EDSDK.EdsRational>(EDSDK.PropID_FocalLength);
        public bool IsFlashOn => GetProperty<uint>(EDSDK.PropID_FlashOn) == 1;
        public uint FlashMode => GetProperty<uint>(EDSDK.PropID_FlashMode);
        public uint Evf_OutputDevice
        {
            get => GetProperty<uint>(EDSDK.PropID_Evf_OutputDevice);
            set => SetPropertyData(EDSDK.PropID_Evf_OutputDevice, value);
        }
        public bool IsLiveViewActive
        {
            get => (Evf_OutputDevice & EDSDK.EvfOutputDevice_PC) != 0;
            set
            {
                uint currentDevice = Evf_OutputDevice;
                uint newDevice = value ? (currentDevice | EDSDK.EvfOutputDevice_PC) : (currentDevice & ~EDSDK.EvfOutputDevice_PC);
                if (currentDevice != newDevice) Evf_OutputDevice = newDevice;
            }
        }
        public uint Evf_Mode
        {
            get => GetProperty<uint>(EDSDK.PropID_Evf_Mode);
            set => SetPropertyData(EDSDK.PropID_Evf_Mode, value);
        }
        public uint Evf_AFMode => GetProperty<uint>(EDSDK.PropID_Evf_AFMode);
        public uint RecordState => GetProperty<uint>(EDSDK.PropID_Record);


        #endregion

        private void SetHostCapacity()
        {
            // Blijft hetzelfde, gebruikt EdsSetCapacity direct
            try
            {
                EDSDK.EdsCapacity capacity = new EDSDK.EdsCapacity
                {
                    NumberOfFreeClusters = 0x7FFFFFFF,
                    BytesPerSector = 0x1000,
                    Reset = 1
                };
                uint err = EDSDK.EdsSetCapacity(_cameraRef, capacity);
                if (err != EDSDK.EDS_ERR_OK && err != EDSDK.EDS_ERR_NOT_SUPPORTED)
                {
                    _logger.Warning($"Failed to set capacity for host saving. SDK Error: 0x{err:X}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error setting host capacity: {ex.Message}", ex);
            }
        }

        // Methoden voor SetPropertyEventHandler, SetObjectEventHandler, SetStateEventHandler blijven hetzelfde.
        // Ze registreren de C# delegates bij de native SDK.
        public void SetPropertyEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsPropertyEventHandler handler, IntPtr context)
        {
            _logger.Verbose("Setting property event handler.");
            uint err = EDSDKLib.EDSDK.EdsSetPropertyEventHandler(_cameraRef, eventTypes, handler, context);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK) throw new CanonSdkException("Failed to set property event handler", err);
        }
        public void SetObjectEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsObjectEventHandler handler, IntPtr context)
        {
            _logger.Verbose("Setting object event handler.");
            uint err = EDSDKLib.EDSDK.EdsSetObjectEventHandler(_cameraRef, eventTypes, handler, context);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK) throw new CanonSdkException("Failed to set object event handler", err);
        }
        public void SetStateEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsStateEventHandler handler, IntPtr context)
        {
            _logger.Verbose("Setting state event handler.");
            uint err = EDSDKLib.EDSDK.EdsSetCameraStateEventHandler(_cameraRef, eventTypes, handler, context);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK) throw new CanonSdkException("Failed to set state event handler", err);
        }
        // Overloads zonder context
        public void SetPropertyEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsPropertyEventHandler handler) => SetPropertyEventHandler(eventTypes, handler, GCHandle.ToIntPtr(_gcHandle));
        public void SetObjectEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsObjectEventHandler handler) => SetObjectEventHandler(eventTypes, handler, GCHandle.ToIntPtr(_gcHandle));
        public void SetStateEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsStateEventHandler handler) => SetStateEventHandler(eventTypes, handler, GCHandle.ToIntPtr(_gcHandle));


        // GetProperty<T> en SetPropertyData<T> blijven hetzelfde, deze zijn voor properties, niet commando's.
        internal T GetProperty<T>(uint propId, int param = 0)
        {
            _logger.Verbose($"Getting property ID 0x{propId:X} as type {typeof(T).Name}.");
            if (_disposed) throw new ObjectDisposedException(nameof(Camera));
            if (_cameraRef == IntPtr.Zero) throw new InvalidOperationException("Camera reference is not valid.");

            // --- Error handling for GetPropertySize ---
            EDSDKLib.EDSDK.EdsDataType dataType;
            int dataSize;
            uint err = EDSDKLib.EDSDK.EdsGetPropertySize(_cameraRef, propId, param, out dataType, out dataSize);

            // Catch specific, non critical errors here. Sometimes a property simply does not exists for the camera
            if (err == EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE || err == EDSDK.EDS_ERR_NOT_SUPPORTED)
            {
                // Log een waarschuwing in plaats van te crashen.
                _logger.Warning($"Property 0x{propId:X} is not available or supported. Returning default value for type {typeof(T).Name}.");
                return default(T); // Retourneer de standaardwaarde (0, null, etc.)
            }
            // Gooi wel een exceptie voor andere, onverwachte fouten.
            if (err != EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException($"Failed to get property size for PropID 0x{propId:X}", err);
            }

            object data;
            try
            {
                if (typeof(T) == typeof(uint) || typeof(T) == typeof(int)) // Gecombineerd voor eenvoud
                {
                    if (dataSize != Marshal.SizeOf(typeof(uint)) && dataSize != Marshal.SizeOf(typeof(int)))
                        throw new CanonSdkException($"Data size mismatch for uint/int. Expected {Marshal.SizeOf(typeof(uint))} or {Marshal.SizeOf(typeof(int))}, got {dataSize}", EDSDK.EDS_ERR_PROPERTIES_MISMATCH);
                    uint uintData;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out uintData);
                    data = (typeof(T) == typeof(int)) ? (object)Convert.ToInt32(uintData) : (object)uintData;
                }
                else if (typeof(T) == typeof(string))
                {
                    string stringData;
                    // dataSize voor string is niet altijd betrouwbaar van EdsGetPropertySize, gebruik een vaste buffer of dynamische allocatie.
                    // EdsGetPropertyData(string) in EDSDK.cs gebruikt al een vaste buffer (256).
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out stringData);
                    data = stringData;
                }
                // ... (andere type handlers voor EdsPoint, EdsRect, EdsSize, EdsFocusInfo, byte[])
                else if (typeof(T) == typeof(EDSDKLib.EDSDK.EdsPoint))
                {
                    EDSDKLib.EDSDK.EdsPoint pointData;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out pointData);
                    data = pointData;
                }
                else if (typeof(T) == typeof(EDSDKLib.EDSDK.EdsRect))
                {
                    EDSDKLib.EDSDK.EdsRect rectData;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out rectData);
                    data = rectData;
                }
                else if (typeof(T) == typeof(EDSDKLib.EDSDK.EdsSize))
                {
                    EDSDKLib.EDSDK.EdsSize sizeData;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out sizeData);
                    data = sizeData;
                }
                else if (typeof(T) == typeof(EDSDKLib.EDSDK.EdsFocusInfo))
                {
                    EDSDKLib.EDSDK.EdsFocusInfo focusInfo;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out focusInfo);
                    data = focusInfo;
                }
                else if (typeof(T) == typeof(byte[]))
                {
                    byte[] bytesData;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out bytesData);
                    data = bytesData;
                }
                else
                {
                    throw new ArgumentException($"Unsupported property data type: {typeof(T).Name}");
                }
            }
            catch (CanonSdkException ex)
            {
                // Vang de exceptie van de GetPropertyData aanroepen.
                if (ex.ErrorCode == EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE || ex.ErrorCode == EDSDK.EDS_ERR_NOT_SUPPORTED)
                {
                    _logger.Warning($"Property 0x{propId:X} became unavailable during data fetch. Returning default value.");
                    return default(T);
                }
                // Gooi andere fouten wel door.
                throw;
            }

            return (T)data;
        }

        public void SetPropertyData<T>(uint propId, T data, int param = 0)
        {
            _logger.Verbose($"Setting property ID 0x{propId:X} with data of type {typeof(T).Name}.");
            if (_disposed) throw new ObjectDisposedException(nameof(Camera));
            if (_cameraRef == IntPtr.Zero) throw new InvalidOperationException("Camera reference is not valid.");

            int size = Marshal.SizeOf(data);
            uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_cameraRef, propId, param, size, data);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK) throw new CanonSdkException($"Failed to set property data for PropID 0x{propId:X}", err);
        }
        public void SetPropertyData(uint propId, byte[] data, int param = 0) // Overload for byte[]
        {
            _logger.Verbose($"Setting property ID 0x{propId:X} with byte array data.");
            if (_disposed) throw new ObjectDisposedException(nameof(Camera));
            if (_cameraRef == IntPtr.Zero) throw new InvalidOperationException("Camera reference is not valid.");

            uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_cameraRef, propId, param, data.Length, data);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK) throw new CanonSdkException($"Failed to set byte[] property data for PropID 0x{propId:X}", err);
        }


        #region Event Handlers (Setup and Internal Callbacks)
        private void SetupEventHandlers()
        {
            _logger.Verbose("Setting up SDK event handlers.");
            // Deze methode wordt aangeroepen in de constructor
            uint err;
            IntPtr contextPtr = GCHandle.ToIntPtr(_gcHandle); // Context voor de callbacks

            err = EDSDKLib.EDSDK.EdsSetPropertyEventHandler(_cameraRef, EDSDK.PropertyEvent_All, _propertyEventHandler, contextPtr);
            if (err != EDSDK.EDS_ERR_OK) throw new CanonSdkException("Failed to set property event handler", err);

            err = EDSDKLib.EDSDK.EdsSetObjectEventHandler(_cameraRef, EDSDK.ObjectEvent_All, _objectEventHandler, contextPtr);
            if (err != EDSDK.EDS_ERR_OK) throw new CanonSdkException("Failed to set object event handler", err);

            err = EDSDKLib.EDSDK.EdsSetCameraStateEventHandler(_cameraRef, EDSDK.StateEvent_All, _stateEventHandler, contextPtr);
            if (err != EDSDK.EDS_ERR_OK) throw new CanonSdkException("Failed to set state event handler", err);
        }

        // Native -> Managed Callback Wrappers
        private uint HandlePropertyEvent(uint inEvent, uint inPropertyID, uint inParam, IntPtr inContext)
        {
            _logger.Verbose($"Property event received. Type: 0x{inEvent:X}, ID: 0x{inPropertyID:X}, Param: {inParam}");
            // Haal het 'this' object terug uit de context als nodig, of roep direct het C# event aan.
            // GCHandle currentHandle = GCHandle.FromIntPtr(inContext);
            // Camera thisCamera = currentHandle.Target as Camera;
            // if (thisCamera != null) { ... }
            try
            {
                PropertyChanged?.Invoke(this, new PropertyEventArgs(inEvent, inPropertyID, inParam));
            }
            catch (Exception ex) { _logger.Error($"Error in Camera.HandlePropertyEvent: {ex.Message}", ex); }
            return EDSDK.EDS_ERR_OK;
        }

        private uint HandleObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            _logger.Verbose($"HandleObjectEvent received. Type: 0x{inEvent:X}, Ref: 0x{inRef.ToInt64():X}. ObjectChanged subscribers? {ObjectChanged != null}");
            ObjectEventArgs eventArgs = new ObjectEventArgs(inEvent, inRef);
            try
            {
                ObjectChanged?.Invoke(this, eventArgs); // Roep het C# event aan

                // Als het event door de abonnee (CameraService) als afgehandeld is gemarkeerd
                // (specifiek voor DirItemRequestTransfer), dan hoeven we de inRef hier niet vrij te geven.
                if (eventArgs.Handled)
                {
                    _logger.Verbose($"HandleObjectEvent (Ref: 0x{inRef.ToInt64():X}) was handled by subscriber. Not releasing here.");
                    return EDSDK.EDS_ERR_OK;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error invoking ObjectChanged event: {ex.Message}", ex);
            }
            finally
            {
                // Als inRef niet IntPtr.Zero is EN niet afgehandeld door een subscriber,
                // dan moet de resource hier worden vrijgegeven om lekken te voorkomen.
                if (inRef != IntPtr.Zero && !eventArgs.Handled)
                {
                    _logger.Verbose($"Releasing inRef (0x{inRef.ToInt64():X}) in HandleObjectEvent as it was not marked as handled.");
                    EDSDK.EdsRelease(inRef);
                }
                else if (inRef != IntPtr.Zero && eventArgs.Handled)
                {
                    _logger.Verbose($"inRef (0x{inRef.ToInt64():X}) was marked handled, assuming subscriber released it.");
                }
            }
            return EDSDK.EDS_ERR_OK;
        }


        private uint HandleStateEvent(uint inEvent, uint inParameter, IntPtr inContext)
        {
            _logger.Verbose($"State event received. Type: 0x{inEvent:X}, Param: 0x{inParameter:X}");
            try
            {
                StateChanged?.Invoke(this, new StateEventArgs(inEvent, inParameter));
            }
            catch (Exception ex) { _logger.Error($"Error in Camera.HandleStateEvent: {ex.Message}", ex); }
            return EDSDK.EDS_ERR_OK;
        }

        // ProcessObjectEvent is niet langer nodig hier, de event handler _objectEventHandler
        // en het C# event ObjectChanged doen het werk. De CameraService abonneert hierop.
        // internal void ProcessObjectEvent(uint eventType, IntPtr objectRef) { ... }

        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _logger.Verbose("Disposing Camera object...");
                if (disposing)
                {
                    // Release managed resources
                    // (bijv. events nullen als je geen strong references wilt houden)
                    _logger.Verbose("Nullifying managed event handlers.");
                    PropertyChanged = null;
                    ObjectChanged = null;
                    StateChanged = null;
                    _eventHandler?.Dispose(); // _eventHandler class moet ook IDisposable zijn
                    _eventHandler = null;
                }

                // Vrijgeven van native event handlers.
                // Dit moet gebeuren VOORDAT de _cameraRef zelf wordt vrijgegeven of de sessie gesloten.
                if (_cameraRef != IntPtr.Zero) // Controleer of er een valide referentie is
                {
                    // Probeer de sessie te sluiten als die nog open is.
                    // Dit is een best-effort poging tijdens dispose.
                    try { CloseSession(); } catch (Exception ex) { _logger.Error($"Exception during CloseSession in Dispose: {ex.Message}", ex); }

                    // Deregistreer de handlers van de SDK
                    // Dit is belangrijk om te voorkomen dat de SDK callbacks probeert aan te roepen
                    // op een GCHandle die mogelijk al vrijgegeven is.
                    try
                    {
                        _logger.Verbose("Unregistering native SDK event handlers.");
                        EDSDKLib.EDSDK.EdsSetPropertyEventHandler(_cameraRef, EDSDK.PropertyEvent_All, null, IntPtr.Zero);
                        EDSDKLib.EDSDK.EdsSetObjectEventHandler(_cameraRef, EDSDK.ObjectEvent_All, null, IntPtr.Zero);
                        EDSDKLib.EDSDK.EdsSetCameraStateEventHandler(_cameraRef, EDSDK.StateEvent_All, null, IntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error unregistering event handlers during Dispose: {ex.Message}", ex);
                    }
                }


                // Free GCHandle
                if (_gcHandle.IsAllocated)
                {
                    _logger.Verbose("Freeing GCHandle for Camera object.");
                    _gcHandle.Free();
                }

                // Release unmanaged camera reference
                if (_cameraRef != IntPtr.Zero)
                {
                    _logger.Verbose($"Releasing native camera reference: 0x{_cameraRef.ToInt64():X}");
                    uint releaseErr = EDSDKLib.EDSDK.EdsRelease(_cameraRef);
                    if (releaseErr != EDSDK.EDS_ERR_OK) _logger.Warning($"EdsRelease on cameraRef failed during Dispose: 0x{releaseErr:X}");
                    _cameraRef = IntPtr.Zero;
                }

                _disposed = true;
                _logger.Verbose("Camera object disposed.");
            }
        }

        ~Camera()
        {
            Dispose(false);
        }
        #endregion
    }

    // Voeg deze enum definitie toe binnen de namespace maar buiten de Camera klasse
    public enum SaveDestination : uint
    {
        Camera = EDSDK.EdsSaveTo.Camera,
        Host = EDSDK.EdsSaveTo.Host,
        Both = EDSDK.EdsSaveTo.Both
    }
}