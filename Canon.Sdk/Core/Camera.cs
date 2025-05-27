// Path: Canon.Sdk/Core/Camera.cs

using System;
using System.Runtime.InteropServices;
using Canon.Sdk.Events;
using Canon.Sdk.Exceptions;

namespace Canon.Sdk.Core
{
    /// <summary>
    /// Represents a Canon camera device
    /// </summary>
    public class Camera : IDisposable
    {
        private bool _disposed = false;
        private IntPtr _cameraRef;
        private CameraEventHandler _eventHandler;
        private DeviceInfo _deviceInfo;
        private GCHandle _gcHandle;

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
                if (_deviceInfo == null)
                {
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

        /// <summary>
        /// Initializes a new instance of the Camera class
        /// </summary>
        /// <param name="cameraRef">The native camera reference</param>
        internal Camera(IntPtr cameraRef)
        {
            _cameraRef = cameraRef;
            _eventHandler = new CameraEventHandler(this);

            // Create a GCHandle to prevent garbage collection
            _gcHandle = GCHandle.Alloc(this);

            // Initialize delegates
            _propertyEventHandler = new EDSDKLib.EDSDK.EdsPropertyEventHandler(HandlePropertyEvent);
            _objectEventHandler = new EDSDKLib.EDSDK.EdsObjectEventHandler(HandleObjectEvent); // necessary for auto-download
            _stateEventHandler = new EDSDKLib.EDSDK.EdsStateEventHandler(HandleStateEvent);

            // Initialize event handlers
            SetupEventHandlers();
        }

        /// <summary>
        /// Gets the name of the camera model
        /// </summary>
        /// <returns>The camera model name</returns>
        public string GetModelName()
        {
            string modelName;
            uint err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, EDSDKLib.EDSDK.PropID_ProductName, 0, out modelName);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to get camera model name", err);
            }

            return modelName;
        }

        /// <summary>
        /// Opens a session with the camera
        /// </summary>
        public void OpenSession()
        {
            uint err = EDSDKLib.EDSDK.EdsOpenSession(_cameraRef);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to open session with camera", err);
            }

            // Set camera to save to host
            SetSaveToHost();
        }



        /// <summary>
        /// Sets the camera to save images to the host computer
        /// </summary>
        // In Camera.cs -> SetSaveToHost()
        private void SetSaveToHost()
        {
            try
            {
                uint saveToValue = (uint)EDSDKLib.EDSDK.EdsSaveTo.Host;
                uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_cameraRef, EDSDKLib.EDSDK.PropID_SaveTo, 0, sizeof(uint), saveToValue);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    Console.WriteLine($"CRITICAL ERROR: Failed to set PropID_SaveTo to Host. SDK Error: 0x{err:X}");
                    throw new CanonSdkException($"Failed to set save location (PropID_SaveTo) to host. SDK Error: 0x{err:X}", err);
                }
                Console.WriteLine("Successfully set PropID_SaveTo to Host."); // Add success log

                EDSDKLib.EDSDK.EdsCapacity capacity = new EDSDKLib.EDSDK.EdsCapacity
                {
                    NumberOfFreeClusters = 0x7FFFFFFF, // Represents a very large capacity
                    BytesPerSector = 0x1000,           // A common sector size
                    Reset = 1                          // Resets the capacity logic on the camera for host
                };

                err = EDSDKLib.EDSDK.EdsSetCapacity(_cameraRef, capacity);
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    Console.WriteLine($"CRITICAL ERROR: Failed to set capacity for host saving. SDK Error: 0x{err:X}");
                    throw new CanonSdkException($"Failed to set capacity for host saving. SDK Error: 0x{err:X}", err);
                }
                Console.WriteLine("Successfully set capacity for host saving."); // Add success log
            }
            catch (CanonSdkException ex) // Catch specific SDK exception
            {
                Console.WriteLine($"CanonSdkException in SetSaveToHost: {ex.Message} (SDK Error: 0x{ex.ErrorCode:X})");
                // Decide if you want to re-throw or handle this as a critical failure
                // For event-driven downloads, this IS critical.
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Generic error in SetSaveToHost: {ex.Message}");
                throw new Exception("Failed to configure camera for host saving.", ex);
            }
        }

        /// <summary>
        /// Closes the session with the camera
        /// </summary>
        public void CloseSession()
        {
            uint err = EDSDKLib.EDSDK.EdsCloseSession(_cameraRef);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to close session with camera", err);
            }
        }

        /// <summary>
        /// Takes a picture with the camera
        /// </summary>
        // In Camera.cs
        public void TakePicture()
        {
            Console.WriteLine("CAMERA.CS: Entering TakePicture using PressShutterButton sequence...");
            uint err = EDSDKLib.EDSDK.EDS_ERR_OK;
            uint releaseErr = EDSDKLib.EDSDK.EDS_ERR_OK;

            try
            {
                // Press completely
                Console.WriteLine("CAMERA.CS: Sending ShutterButton_Completely...");
                err = EDSDKLib.EDSDK.EdsSendCommand(_cameraRef, EDSDKLib.EDSDK.CameraCommand_PressShutterButton, (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely);
                Console.WriteLine($"CAMERA.CS: ShutterButton_Completely command returned 0x{err:X}");

                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    throw new CanonSdkException($"Failed to take picture (PressShutterButton Completely). SDK Error: 0x{err:X}", err);
                }
                Console.WriteLine("CAMERA.CS: ShutterButton_Completely command successful.");

                Thread.Sleep(500); // Small delay to avoid hogging CPU
            }
            finally // Ensure OFF is sent even if Completely succeeds but subsequent code fails
            {
                // Release button (must be done)
                Console.WriteLine("CAMERA.CS: Sending ShutterButton_OFF...");
                releaseErr = EDSDKLib.EDSDK.EdsSendCommand(_cameraRef, EDSDKLib.EDSDK.CameraCommand_PressShutterButton, (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF);
                Console.WriteLine($"CAMERA.CS: ShutterButton_OFF command returned 0x{releaseErr:X}");

                if (releaseErr != EDSDKLib.EDSDK.EDS_ERR_OK && err == EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    // Only throw if the primary command succeeded but release failed,
                    // otherwise prioritize the original error.
                    // Or just log as a warning. For debugging, let's throw.
                    throw new CanonSdkException($"Picture taken (?), but failed to send ShutterButton_OFF. SDK Error: 0x{releaseErr:X}", releaseErr);
                }
                else if (releaseErr != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    Console.WriteLine($"WARNING: Original TakePicture command failed (0x{err:X}) AND ShutterButton_OFF failed (0x{releaseErr:X})");
                }
                else
                {
                    Console.WriteLine("CAMERA.CS: ShutterButton_OFF command successful.");
                }
            }
            Console.WriteLine("CAMERA.CS: Exiting TakePicture.");
        }

        /// <summary>
        /// Sets a property event handler for the camera
        /// </summary>
        /// <param name="eventTypes">Event types to listen for</param>
        /// <param name="handler">The handler function</param>
        public void SetPropertyEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsPropertyEventHandler handler)
        {
            SetPropertyEventHandler(eventTypes, handler, IntPtr.Zero);
        }

        /// <summary>
        /// Sets a property event handler for the camera
        /// </summary>
        /// <param name="eventTypes">Event types to listen for</param>
        /// <param name="handler">The handler function</param>
        /// <param name="context">User context data</param>
        public void SetPropertyEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsPropertyEventHandler handler, IntPtr context)
        {
            uint err = EDSDKLib.EDSDK.EdsSetPropertyEventHandler(_cameraRef, eventTypes, handler, context);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set property event handler", err);
            }
        }

        /// <summary>
        /// Sets an object event handler for the camera
        /// </summary>
        /// <param name="eventTypes">Event types to listen for</param>
        /// <param name="handler">The handler function</param>
        public void SetObjectEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsObjectEventHandler handler)
        {
            SetObjectEventHandler(eventTypes, handler, IntPtr.Zero);
        }

        /// <summary>
        /// Sets an object event handler for the camera
        /// </summary>
        /// <param name="eventTypes">Event types to listen for</param>
        /// <param name="handler">The handler function</param>
        /// <param name="context">User context data</param>
        public void SetObjectEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsObjectEventHandler handler, IntPtr context)
        {
            uint err = EDSDKLib.EDSDK.EdsSetObjectEventHandler(_cameraRef, eventTypes, handler, context);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set object event handler", err);
            }
        }

        /// <summary>
        /// Sets a state event handler for the camera
        /// </summary>
        /// <param name="eventTypes">Event types to listen for</param>
        /// <param name="handler">The handler function</param>
        public void SetStateEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsStateEventHandler handler)
        {
            SetStateEventHandler(eventTypes, handler, IntPtr.Zero);
        }

        /// <summary>
        /// Sets a state event handler for the camera
        /// </summary>
        /// <param name="eventTypes">Event types to listen for</param>
        /// <param name="handler">The handler function</param>
        /// <param name="context">User context data</param>
        public void SetStateEventHandler(uint eventTypes, EDSDKLib.EDSDK.EdsStateEventHandler handler, IntPtr context)
        {
            uint err = EDSDKLib.EDSDK.EdsSetCameraStateEventHandler(_cameraRef, eventTypes, handler, context);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set state event handler", err);
            }
        }

        /// <summary>
        /// Gets property data from the camera
        /// </summary>
        /// <typeparam name="T">The type of the property data</typeparam>
        /// <param name="propId">The property ID</param>
        /// <param name="param">Optional parameter</param>
        /// <returns>The property data</returns>
        public T GetProperty<T>(uint propId, int param = 0)
        {
            EDSDKLib.EDSDK.EdsDataType dataType;
            int dataSize;

            // Get the property size and data type
            uint err = EDSDKLib.EDSDK.EdsGetPropertySize(_cameraRef, propId, param, out dataType, out dataSize);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to get property size", err);
            }

            // Get the property data based on the type
            object data;

            if (typeof(T) == typeof(uint) || typeof(T) == typeof(int))
            {
                uint uintData;
                err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out uintData);
                data = uintData;
            }
            else if (typeof(T) == typeof(string))
            {
                string stringData;
                err = EDSDKLib.EDSDK.EdsGetPropertyData(_cameraRef, propId, param, out stringData);
                data = stringData;
            }
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

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to get property data", err);
            }

            return (T)data;
        }

        /// <summary>
        /// Sets property data on the camera
        /// </summary>
        /// <typeparam name="T">The type of the property data</typeparam>
        /// <param name="propId">The property ID</param>
        /// <param name="data">The data to set</param>
        /// <param name="param">Optional parameter</param>
        public void SetPropertyData<T>(uint propId, T data, int param = 0)
        {
            uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_cameraRef, propId, param, Marshal.SizeOf(data), data);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set property data", err);
            }
        }

        /// <summary>
        /// Sets property data on the camera (byte array overload)
        /// </summary>
        /// <param name="propId">The property ID</param>
        /// <param name="data">The data to set</param>
        /// <param name="param">Optional parameter</param>
        public void SetPropertyData(uint propId, byte[] data, int param = 0)
        {
            uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_cameraRef, propId, param, data.Length, data);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set property data", err);
            }
        }

        #region Event Handlers

        private void SetupEventHandlers()
        {
            // Set up event handlers for property events
            uint err = EDSDKLib.EDSDK.EdsSetPropertyEventHandler(
                _cameraRef,
                EDSDKLib.EDSDK.PropertyEvent_All,
                _propertyEventHandler,
                GCHandle.ToIntPtr(_gcHandle));

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set property event handler", err);
            }

            // Set up event handlers for object events
            err = EDSDKLib.EDSDK.EdsSetObjectEventHandler(
                _cameraRef,
                EDSDKLib.EDSDK.ObjectEvent_All,
                _objectEventHandler,
                GCHandle.ToIntPtr(_gcHandle));

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set object event handler", err);
            }

            // Set up event handlers for state events
            err = EDSDKLib.EDSDK.EdsSetCameraStateEventHandler(
                _cameraRef,
                EDSDKLib.EDSDK.StateEvent_All,
                _stateEventHandler,
                GCHandle.ToIntPtr(_gcHandle));

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException("Failed to set state event handler", err);
            }
        }

        private uint HandlePropertyEvent(uint inEvent, uint inPropertyID, uint inParam, IntPtr inContext)
        {
            try
            {
                var eventArgs = new PropertyEventArgs(inEvent, inPropertyID, inParam);
                PropertyChanged?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in property event handler: {ex.Message}");
            }
            return EDSDKLib.EDSDK.EDS_ERR_OK;
        }

        private uint HandleObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            Console.WriteLine($"CAMERA.CS: HandleObjectEvent received. Type: 0x{inEvent:X}. Is ObjectChanged null? {ObjectChanged == null}");
            ObjectEventArgs eventArgs = null; // Initialize to null
            try
            {
                eventArgs = new ObjectEventArgs(inEvent, inRef); // Assign here
                ObjectChanged?.Invoke(this, eventArgs);

                if (inEvent == EDSDKLib.EDSDK.ObjectEvent_DirItemRequestTransfer && !eventArgs.Handled)
                {
                    Console.WriteLine($"CAMERA.CS: DirItemRequestTransfer detected. Calling HandleDownload.");
                    eventArgs.Handled = true;
                    _eventHandler.HandleDownload(inRef); // HandleDownload is responsible for EdsRelease(inRef)
                    return EDSDKLib.EDSDK.EDS_ERR_OK; // Return because inRef is handled by HandleDownload
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Camera.HandleObjectEvent: {ex.Message}");
                // If an exception occurs, eventArgs might still be null or Handled might be false
            }
            finally // Use finally to ensure release if not handled
            {
                // Only release if inRef is valid AND it wasn't handled by HandleDownload path
                // or if an exception occurred before eventArgs.Handled could be set.
                if (inRef != IntPtr.Zero)
                {
                    bool shouldRelease = true;
                    if (eventArgs != null && eventArgs.Handled)
                    {
                        shouldRelease = false; // It was (or should have been) released by HandleDownload
                    }

                    if (shouldRelease)
                    {
                        Console.WriteLine($"CAMERA.CS: Releasing inRef (0x{inRef.ToInt64():X}) because it was not handled by DirItemRequestTransfer special path or an error occurred.");
                        EDSDKLib.EDSDK.EdsRelease(inRef);
                    }
                }
            }
            return EDSDKLib.EDSDK.EDS_ERR_OK;
        }

        private uint HandleStateEvent(uint inEvent, uint inParameter, IntPtr inContext)
        {
            try
            {
                var eventArgs = new StateEventArgs(inEvent, inParameter);
                StateChanged?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in state event handler: {ex.Message}");
            }
            return EDSDKLib.EDSDK.EDS_ERR_OK;
        }

        /// <summary>
        /// Processes an object event from the camera
        /// </summary>
        /// <param name="eventType">The event type</param>
        /// <param name="objectRef">Reference to the object</param>
        internal void ProcessObjectEvent(uint eventType, IntPtr objectRef)
        {

            Console.WriteLine("ProcessObjectEvent called");

            switch (eventType)
            {
                case EDSDKLib.EDSDK.ObjectEvent_DirItemRequestTransfer:
                    _eventHandler.HandleDownload(objectRef);
                    break;

                default:
                    // Release the object if we're not handling it
                    if (objectRef != IntPtr.Zero)
                    {
                        EDSDKLib.EDSDK.EdsRelease(objectRef);
                    }
                    break;
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Releases resources used by the camera
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources used by the camera
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Release managed resources
                    // Unregister event handlers
                    if (_cameraRef != IntPtr.Zero)
                    {
                        try
                        {
                            EDSDKLib.EDSDK.EdsSetPropertyEventHandler(_cameraRef, EDSDKLib.EDSDK.PropertyEvent_All, null, IntPtr.Zero);
                            EDSDKLib.EDSDK.EdsSetObjectEventHandler(_cameraRef, EDSDKLib.EDSDK.ObjectEvent_All, null, IntPtr.Zero);
                            EDSDKLib.EDSDK.EdsSetCameraStateEventHandler(_cameraRef, EDSDKLib.EDSDK.StateEvent_All, null, IntPtr.Zero);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error unregistering event handlers: {ex.Message}");
                        }
                    }
                }

                // Free GCHandle
                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();

                // Release unmanaged resources
                if (_cameraRef != IntPtr.Zero)
                {
                    EDSDKLib.EDSDK.EdsRelease(_cameraRef);
                    _cameraRef = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~Camera()
        {
            Dispose(false);
        }

        #endregion
    }
}