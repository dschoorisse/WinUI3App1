// Path: Canon.Sdk/Events/CameraEventHandler.cs

using System;
using System.Runtime.InteropServices;
using System.IO;
using Canon.Sdk.Core;
using Canon.Sdk.Exceptions;
using EDSDKLib;

namespace Canon.Sdk.Events
{
    public class CameraEventHandler : IDisposable
    {
        private Camera _camera;
        private GCHandle _gcHandle;
        private IntPtr _context;

        // Event delegates
        private EDSDK.EdsPropertyEventHandler _propertyEventHandler;
        private EDSDK.EdsObjectEventHandler _objectEventHandler;
        private EDSDK.EdsStateEventHandler _stateEventHandler;

        // Events that clients can subscribe to
        public event EventHandler<PropertyEventArgs> PropertyChanged;
        public event EventHandler<ObjectEventArgs> ObjectChanged;
        public event EventHandler<StateEventArgs> StateChanged;

        public CameraEventHandler(Camera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            
            // Create a GCHandle to prevent garbage collection
            _gcHandle = GCHandle.Alloc(this);
            _context = GCHandle.ToIntPtr(_gcHandle);
            
            // Initialize delegates
            _propertyEventHandler = new EDSDK.EdsPropertyEventHandler(HandlePropertyEvent);
            _objectEventHandler = new EDSDK.EdsObjectEventHandler(HandleObjectEvent);
            _stateEventHandler = new EDSDK.EdsStateEventHandler(HandleStateEvent);
            
            // Register handlers with Canon SDK
            RegisterEventHandlers();
        }

        private void RegisterEventHandlers()
        {
            if (_camera.NativeReference == IntPtr.Zero)
                throw new CanonSdkException("Camera reference is invalid", 0);

            uint err;
            
            // Register property event handler
            err = EDSDK.EdsSetPropertyEventHandler(_camera.NativeReference, 
                EDSDK.PropertyEvent_All, _propertyEventHandler, _context);
            if (err != EDSDK.EDS_ERR_OK)
                throw new CanonSdkException($"Failed to register property event handler", err);
            
            // Register object event handler
            err = EDSDK.EdsSetObjectEventHandler(_camera.NativeReference, 
                EDSDK.ObjectEvent_All, _objectEventHandler, _context);
            if (err != EDSDK.EDS_ERR_OK)
                throw new CanonSdkException($"Failed to register object event handler", err);
            
            // Register state event handler
            err = EDSDK.EdsSetCameraStateEventHandler(_camera.NativeReference, 
                EDSDK.StateEvent_All, _stateEventHandler, _context);
            if (err != EDSDK.EDS_ERR_OK)
                throw new CanonSdkException($"Failed to register state event handler", err);
        }

        private uint HandlePropertyEvent(uint inEvent, uint inPropertyID, uint inParam, IntPtr inContext)
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyEventArgs(inEvent, inPropertyID, inParam));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in property event handler: {ex.Message}");
            }
            return EDSDK.EDS_ERR_OK;
        }

        private uint HandleObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            Console.WriteLine("HandleObjectEvent called");

            try
            {
                var args = new ObjectEventArgs(inEvent, inRef);
                ObjectChanged?.Invoke(this, args);
                
                // We need to handle DirItemRequestTransfer differently to auto-download files
                if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer && !args.Handled)
                {
                    // Let the camera know we're handling this
                    args.Handled = true;
                    
                    // Download the image
                    HandleDownload(inRef);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in object event handler: {ex.Message}");
            }
            return EDSDK.EDS_ERR_OK;
        }

        private uint HandleStateEvent(uint inEvent, uint inParameter, IntPtr inContext)
        {
            try
            {
                StateChanged?.Invoke(this, new StateEventArgs(inEvent, inParameter));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in state event handler: {ex.Message}");
            }
            return EDSDK.EDS_ERR_OK;
        }

        public void HandleDownload(IntPtr directoryItemRef)
        {

            bool downloadStarted = false;

            if (directoryItemRef == IntPtr.Zero)
            {
                Console.WriteLine("HandleDownload: directoryItemRef is Zero, cannot download.");
            }

            try
            {
                // Get information about the file
                EDSDK.EdsDirectoryItemInfo dirItemInfo;
                uint err = EDSDK.EdsGetDirectoryItemInfo(directoryItemRef, out dirItemInfo);
                if (err != EDSDK.EDS_ERR_OK)
                {
                    Console.WriteLine("Failed to get directory item info ");
                    throw new CanonSdkException("Failed to get directory item info.", err);
                }

                // Create a file path on desktop or pictures folder
                string savePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    $"Canon_{DateTime.Now:yyyyMMdd_HHmmss}_{dirItemInfo.szFileName}");

                Console.WriteLine($"HandleDownload: Attempting to download '{dirItemInfo.szFileName}' to '{savePath}'");

                // Create file stream
                IntPtr stream;
                err = EDSDK.EdsCreateFileStream(savePath, EDSDK.EdsFileCreateDisposition.CreateAlways,
                    EDSDK.EdsAccess.ReadWrite, out stream);
                if (err != EDSDK.EDS_ERR_OK)
                    throw new CanonSdkException("Failed to create file stream.", err);

                try
                {
                    // Download image data
                    err = EDSDK.EdsDownload(directoryItemRef, dirItemInfo.Size, stream);
                    downloadStarted = true; // Mark that download was attempted

                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        Console.WriteLine($"HandleDownload: EdsDownload failed for '{dirItemInfo.szFileName}'. SDK Error: 0x{err:X}");
                        // Do NOT throw here if you want DownloadComplete and Release to attempt to run
                        // But you might not want to call DownloadComplete if Download failed.
                        // The SDK docs should clarify if DownloadComplete is needed/allowed after a download error.
                        // For now, let's assume if download fails, we skip complete but still release.
                        downloadStarted = false; // Reset if download itself failed
                                                 // Consider EdsDownloadCancel if appropriate
                                                 // EDSDK.EdsDownloadCancel(directoryItemRef);
                    }
                    else
                    {
                        Console.WriteLine($"HandleDownload: EdsDownload successful for '{dirItemInfo.szFileName}'.");
                    }

                    // Complete download - ONLY if EdsDownload was successful
                    if (downloadStarted && err == EDSDK.EDS_ERR_OK) // Check original err from EdsDownload
                    {
                        uint completeErr = EDSDK.EdsDownloadComplete(directoryItemRef);
                        if (completeErr != EDSDK.EDS_ERR_OK)
                        {
                            // This is problematic, as the camera might not know the transfer is done.
                            Console.WriteLine($"HandleDownload: EdsDownloadComplete failed for '{dirItemInfo.szFileName}'. SDK Error: 0x{completeErr:X}");
                            // This could block subsequent transfers.
                        }
                        else
                        {
                            Console.WriteLine($"HandleDownload: EdsDownloadComplete successful for '{dirItemInfo.szFileName}'.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error downloading image '{(dirItemInfo.szFileName ?? "UnknownFile")}': {ex.Message}");
                }
                finally
                {
                    if (stream != IntPtr.Zero)
                    {
                        Console.WriteLine($"HandleDownload: Releasing stream for '{(dirItemInfo.szFileName ?? "UnknownFile")}'.");
                        EDSDK.EdsRelease(stream);
                    }
                    if (directoryItemRef != IntPtr.Zero) // Redundant check as first line covers it, but safe
                    {
                        Console.WriteLine($"HandleDownload: Releasing directoryItemRef (0x{directoryItemRef.ToInt64():X}) for '{(dirItemInfo.szFileName ?? "UnknownFile")}'.");
                        EDSDK.EdsRelease(directoryItemRef);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading image: {ex.Message}");
            }
            finally
            {
                // Release the directory item
                if (directoryItemRef != IntPtr.Zero)
                    EDSDK.EdsRelease(directoryItemRef);
            }
        }

        public void Dispose()
        {
            // Unregister event handlers
            if (_camera.NativeReference != IntPtr.Zero)
            {
                try
                {
                    EDSDK.EdsSetPropertyEventHandler(_camera.NativeReference, EDSDK.PropertyEvent_All, null, IntPtr.Zero);
                    EDSDK.EdsSetObjectEventHandler(_camera.NativeReference, EDSDK.ObjectEvent_All, null, IntPtr.Zero);
                    EDSDK.EdsSetCameraStateEventHandler(_camera.NativeReference, EDSDK.StateEvent_All, null, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error unregistering event handlers: {ex.Message}");
                }
            }

            // Free GCHandle
            if (_gcHandle.IsAllocated)
                _gcHandle.Free();
            
            // Clear delegates
            _propertyEventHandler = null;
            _objectEventHandler = null;
            _stateEventHandler = null;
        }
    }
}