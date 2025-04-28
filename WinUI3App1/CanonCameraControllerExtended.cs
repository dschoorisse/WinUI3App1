using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Core;

namespace WinUI3App1
{
    // Example of how to extend the CanonCameraController to save images
    public class CanonCameraControllerExtended : CanonCameraController
    {
        private readonly string _saveDirectory;

        public CanonCameraControllerExtended(ILogger logger, string saveDirectory) : base(logger)
        {
            _saveDirectory = saveDirectory;

            // Create the save directory if it doesn't exist
            if (!Directory.Exists(_saveDirectory))
            {
                Directory.CreateDirectory(_saveDirectory);
            }
        }

        /*
        // Override the ObjectEventHandler to save the image to a file
        protected override uint ObjectEventHandler(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer)
            {
                Logger.Information("Image ready for download");

                try
                {
                    // Get directory item info to get the filename
                    EDSDK.EdsDirectoryItemInfo dirItemInfo;
                    uint err = EDSDK.EdsGetDirectoryItemInfo(inRef, out dirItemInfo);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        Logger.Error("Failed to get directory item info: 0x{Error:X}", err);
                        return err;
                    }

                    // Create file path
                    string fileName = dirItemInfo.szFileName;
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string extension = Path.GetExtension(fileName);
                    string newFileName = $"Photo_{timestamp}{extension}";
                    string filePath = Path.Combine(_saveDirectory, newFileName);

                    // Create file stream for saving the image
                    IntPtr stream = IntPtr.Zero;
                    err = EDSDK.EdsCreateFileStream(filePath, EDSDK.EdsFileCreateDisposition.CreateAlways, EDSDK.EdsAccess.ReadWrite, out stream);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        Logger.Error("Failed to create file stream: 0x{Error:X}", err);
                        return err;
                    }

                    // Download the image
                    err = EDSDK.EdsDownload(inRef, dirItemInfo.Size, stream);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        Logger.Error("Failed to download image: 0x{Error:X}", err);
                        EDSDK.EdsRelease(stream);
                        return err;
                    }

                    // Signal that download is complete
                    err = EDSDK.EdsDownloadComplete(inRef);
                    if (err != EDSDK.EDS_ERR_OK)
                    {
                        Logger.Error("Failed to complete download: 0x{Error:X}", err);
                        EDSDK.EdsRelease(stream);
                        return err;
                    }

                    // Release the stream
                    EDSDK.EdsRelease(stream);

                    Logger.Information("Image saved successfully to: {FilePath}", filePath);

                    // Raise image saved event if needed
                    OnImageSaved(filePath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Exception in ObjectEventHandler while saving image");
                }
            }

            return EDSDK.EDS_ERR_OK;
        }

        */

        // Event for when an image is saved
        public event EventHandler<string> ImageSaved;

        protected virtual void OnImageSaved(string filePath)
        {
            ImageSaved?.Invoke(this, filePath);
        }
    }
}