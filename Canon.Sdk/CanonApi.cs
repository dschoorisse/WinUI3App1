// Path: Canon.Sdk/Core/CanonAPI.cs

using System;
using System.Runtime.InteropServices;
using Canon.Sdk.Events;
using Canon.Sdk.Exceptions;
using Canon.Sdk.Logging;
using EDSDKLib;

namespace Canon.Sdk.Core
{
    public class CanonAPI : IDisposable
    {
        private bool _initialized;
        private bool _disposed;
        private EDSDK.EdsCameraAddedHandler _cameraAddedHandler;
        private GCHandle _cameraAddedHandlerHandle;
        private Action<Camera> _onCameraAdded;
        private readonly ILogger _logger;

        public CanonAPI(ILogger logger)
        {
            _logger = logger;
            _initialized = false;
            _disposed = false;
        }

        public void Initialize()
        {
            ThrowIfDisposed();

            if (_initialized)
                return;

            uint err = EDSDK.EdsInitializeSDK();
            if (err != EDSDK.EDS_ERR_OK)
                throw new CanonSdkException($"Failed to initialize Canon SDK: {err}", err);

            _initialized = true;
        }

        public void SetCameraAddedHandler(Action<Camera> callback)
        {
            ThrowIfDisposed();

            if (!_initialized)
                throw new InvalidOperationException("Canon SDK has not been initialized");

            // Store the callback
            _onCameraAdded = callback;

            // Create a delegate for the camera added event
            _cameraAddedHandler = new EDSDK.EdsCameraAddedHandler(HandleCameraAdded);

            // Keep a reference to the delegate to prevent garbage collection
            _cameraAddedHandlerHandle = GCHandle.Alloc(_cameraAddedHandler);

            // Register the handler with the Canon SDK
            uint err = EDSDK.EdsSetCameraAddedHandler(_cameraAddedHandler, IntPtr.Zero);
            if (err != EDSDK.EDS_ERR_OK)
                throw new CanonSdkException($"Failed to set camera added handler: {err}", err);
        }

        private uint HandleCameraAdded(IntPtr inContext)
        {
            try
            {
                // Get the camera list
                CameraList cameraList = GetCameraList();

                // Get the first camera
                if (cameraList.Count > 0)
                {
                    Camera camera = cameraList[0];
                    _onCameraAdded?.Invoke(camera);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"CanonApi: Error in camera added handler: {ex.Message}");
            }

            return EDSDK.EDS_ERR_OK;
        }

        public CameraList GetCameraList()
        {
            _logger.Debug("CanonApi: GetCameraList called...");
            ThrowIfDisposed();

            if (!_initialized)
                throw new InvalidOperationException("Canon SDK has not been initialized");

            IntPtr cameraList;
            uint err = EDSDK.EdsGetCameraList(out cameraList);
            if (err != EDSDK.EDS_ERR_OK)
            {
                throw new CanonSdkException($"Failed to get camera list: {err}", err);
            }

            return new CameraList(cameraList, _logger);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CanonAPI));
        }

        public void Dispose()
        {
            _logger.Debug("CanonApi: Disposing CanonApi...");

            if (_disposed)
                return;

            if (_cameraAddedHandlerHandle.IsAllocated)
                _cameraAddedHandlerHandle.Free();

            if (_initialized)
            {
                EDSDK.EdsTerminateSDK();
                _initialized = false;
            }

            _logger.Debug("CanonApi: Disposed CanonApi!");
            _disposed = true;
        }
    }
}