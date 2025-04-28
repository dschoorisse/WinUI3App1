using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Core;

namespace WinUI3App1
{
    // Extension methods to help with camera properties
    public static class CameraPropertyMethods
    {
        // Get available ISO speed values
        public static List<uint> GetAvailableIsoSpeeds(IntPtr camera, ILogger logger)
        {
            List<uint> isoSpeeds = new List<uint>();

            try
            {
                EDSDK.EdsPropertyDesc propertyDesc;
                uint err = EDSDK.EdsGetPropertyDesc(camera, EDSDK.PropID_ISOSpeed, out propertyDesc);

                if (err == EDSDK.EDS_ERR_OK)
                {
                    for (int i = 0; i < propertyDesc.NumElements; i++)
                    {
                        isoSpeeds.Add((uint)propertyDesc.PropDesc[i]);
                    }

                    logger.Information("Found {Count} available ISO speeds", isoSpeeds.Count);
                }
                else
                {
                    logger.Error("Failed to get ISO speed property description: 0x{Error:X}", err);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception in GetAvailableIsoSpeeds");
            }

            return isoSpeeds;
        }

        // Get available aperture values
        public static List<uint> GetAvailableApertures(IntPtr camera, ILogger logger)
        {
            List<uint> apertures = new List<uint>();

            try
            {
                EDSDK.EdsPropertyDesc propertyDesc;
                uint err = EDSDK.EdsGetPropertyDesc(camera, EDSDK.PropID_Av, out propertyDesc);

                if (err == EDSDK.EDS_ERR_OK)
                {
                    for (int i = 0; i < propertyDesc.NumElements; i++)
                    {
                        apertures.Add((uint)propertyDesc.PropDesc[i]);
                    }

                    logger.Information("Found {Count} available aperture values", apertures.Count);
                }
                else
                {
                    logger.Error("Failed to get aperture property description: 0x{Error:X}", err);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception in GetAvailableApertures");
            }

            return apertures;
        }

        // Get available shutter speed values
        public static List<uint> GetAvailableShutterSpeeds(IntPtr camera, ILogger logger)
        {
            List<uint> shutterSpeeds = new List<uint>();

            try
            {
                EDSDK.EdsPropertyDesc propertyDesc;
                uint err = EDSDK.EdsGetPropertyDesc(camera, EDSDK.PropID_Tv, out propertyDesc);

                if (err == EDSDK.EDS_ERR_OK)
                {
                    for (int i = 0; i < propertyDesc.NumElements; i++)
                    {
                        shutterSpeeds.Add((uint)propertyDesc.PropDesc[i]);
                    }

                    logger.Information("Found {Count} available shutter speed values", shutterSpeeds.Count);
                }
                else
                {
                    logger.Error("Failed to get shutter speed property description: 0x{Error:X}", err);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception in GetAvailableShutterSpeeds");
            }

            return shutterSpeeds;
        }

        // Convert aperture value (Av) to F-number string
        public static string ApertureValueToFNumber(uint avValue)
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

        // Convert shutter speed value (Tv) to exposure time string
        public static string ShutterSpeedValueToExposureTime(uint tvValue)
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

        // Convert ISO speed value to ISO string
        public static string IsoSpeedValueToString(uint isoValue)
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
    }

    //// Extension to the CanonCameraController with camera setting methods
    //public class CanonCameraAdvancedController : CanonCameraController
    //{
    //    public CanonCameraAdvancedController(ILogger logger) : base(logger)
    //    {
    //    }

    //    // Set ISO speed
    //    public bool SetIsoSpeed(uint isoValue)
    //    {
    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return false;
    //        }

    //        try
    //        {
    //            Logger.Information("Setting ISO speed to {ISO}...", CameraPropertyMethods.IsoSpeedValueToString(isoValue));

    //            uint err = EDSDK.EdsSetPropertyData(CameraRef, EDSDK.PropID_ISOSpeed, 0, sizeof(uint), isoValue);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to set ISO speed: 0x{Error:X}", err);
    //                return false;
    //            }

    //            Logger.Information("ISO speed set successfully");
    //            return true;
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in SetIsoSpeed");
    //            return false;
    //        }
    //    }

    //    // Set aperture value
    //    public bool SetAperture(uint apertureValue)
    //    {
    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return false;
    //        }

    //        try
    //        {
    //            Logger.Information("Setting aperture to {Aperture}...", CameraPropertyMethods.ApertureValueToFNumber(apertureValue));

    //            uint err = EDSDK.EdsSetPropertyData(CameraRef, EDSDK.PropID_Av, 0, sizeof(uint), apertureValue);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to set aperture: 0x{Error:X}", err);
    //                return false;
    //            }

    //            Logger.Information("Aperture set successfully");
    //            return true;
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in SetAperture");
    //            return false;
    //        }
    //    }

    //    // Set shutter speed
    //    public bool SetShutterSpeed(uint shutterSpeedValue)
    //    {
    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return false;
    //        }

    //        try
    //        {
    //            Logger.Information("Setting shutter speed to {ShutterSpeed}...",
    //                CameraPropertyMethods.ShutterSpeedValueToExposureTime(shutterSpeedValue));

    //            uint err = EDSDK.EdsSetPropertyData(CameraRef, EDSDK.PropID_Tv, 0, sizeof(uint), shutterSpeedValue);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to set shutter speed: 0x{Error:X}", err);
    //                return false;
    //            }

    //            Logger.Information("Shutter speed set successfully");
    //            return true;
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in SetShutterSpeed");
    //            return false;
    //        }
    //    }

    //    // Get current camera settings (ISO, aperture, shutter speed)
    //    public bool GetCurrentSettings(out uint isoSpeed, out uint aperture, out uint shutterSpeed)
    //    {
    //        isoSpeed = 0;
    //        aperture = 0;
    //        shutterSpeed = 0;

    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return false;
    //        }

    //        try
    //        {
    //            Logger.Information("Getting current camera settings...");

    //            // Get ISO
    //            uint err = EDSDK.EdsGetPropertyData(CameraRef, EDSDK.PropID_ISOSpeed, 0, out isoSpeed);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to get ISO speed: 0x{Error:X}", err);
    //                return false;
    //            }

    //            // Get aperture
    //            err = EDSDK.EdsGetPropertyData(CameraRef, EDSDK.PropID_Av, 0, out aperture);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to get aperture: 0x{Error:X}", err);
    //                return false;
    //            }

    //            // Get shutter speed
    //            err = EDSDK.EdsGetPropertyData(CameraRef, EDSDK.PropID_Tv, 0, out shutterSpeed);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to get shutter speed: 0x{Error:X}", err);
    //                return false;
    //            }

    //            Logger.Information("Current settings - ISO: {ISO}, Aperture: {Aperture}, Shutter Speed: {ShutterSpeed}",
    //                CameraPropertyMethods.IsoSpeedValueToString(isoSpeed),
    //                CameraPropertyMethods.ApertureValueToFNumber(aperture),
    //                CameraPropertyMethods.ShutterSpeedValueToExposureTime(shutterSpeed));

    //            return true;
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in GetCurrentSettings");
    //            return false;
    //        }
    //    }

    //    // Set exposure compensation
    //    public bool SetExposureCompensation(uint exposureCompValue)
    //    {
    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return false;
    //        }

    //        try
    //        {
    //            Logger.Information("Setting exposure compensation...");

    //            uint err = EDSDK.EdsSetPropertyData(CameraRef, EDSDK.PropID_ExposureCompensation, 0, sizeof(uint), exposureCompValue);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to set exposure compensation: 0x{Error:X}", err);
    //                return false;
    //            }

    //            Logger.Information("Exposure compensation set successfully");
    //            return true;
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in SetExposureCompensation");
    //            return false;
    //        }
    //    }

    //    // Set AE mode (Program, Tv, Av, Manual, etc.)
    //    public bool SetAEMode(uint aeModeValue)
    //    {
    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return false;
    //        }

    //        try
    //        {
    //            Logger.Information("Setting AE mode...");

    //            uint err = EDSDK.EdsSetPropertyData(CameraRef, EDSDK.PropID_AEMode, 0, sizeof(uint), aeModeValue);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to set AE mode: 0x{Error:X}", err);
    //                return false;
    //            }

    //            Logger.Information("AE mode set successfully");
    //            return true;
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in SetAEMode");
    //            return false;
    //        }
    //    }

    //    // Get available image quality options
    //    public List<uint> GetAvailableImageQualities()
    //    {
    //        List<uint> imageQualities = new List<uint>();

    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return imageQualities;
    //        }

    //        try
    //        {
    //            EDSDK.EdsPropertyDesc propertyDesc;
    //            uint err = EDSDK.EdsGetPropertyDesc(CameraRef, EDSDK.PropID_ImageQuality, out propertyDesc);

    //            if (err == EDSDK.EDS_ERR_OK)
    //            {
    //                for (int i = 0; i < propertyDesc.NumElements; i++)
    //                {
    //                    imageQualities.Add((uint)propertyDesc.PropDesc[i]);
    //                }

    //                Logger.Information("Found {Count} available image quality options", imageQualities.Count);
    //            }
    //            else
    //            {
    //                Logger.Error("Failed to get image quality property description: 0x{Error:X}", err);
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in GetAvailableImageQualities");
    //        }

    //        return imageQualities;
    //    }

    //    // Set image quality
    //    public bool SetImageQuality(uint imageQualityValue)
    //    {
    //        if (!IsConnected || CameraRef == IntPtr.Zero)
    //        {
    //            Logger.Error("Camera not connected");
    //            return false;
    //        }

    //        try
    //        {
    //            Logger.Information("Setting image quality...");

    //            uint err = EDSDK.EdsSetPropertyData(CameraRef, EDSDK.PropID_ImageQuality, 0, sizeof(uint), imageQualityValue);
    //            if (err != EDSDK.EDS_ERR_OK)
    //            {
    //                Logger.Error("Failed to set image quality: 0x{Error:X}", err);
    //                return false;
    //            }

    //            Logger.Information("Image quality set successfully");
    //            return true;
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.Error(ex, "Exception in SetImageQuality");
    //            return false;
    //        }
    //    }

        // Expose CameraRef and IsConnected for external use
     //   internal IntPtr CameraRef => _camera;
     //   internal bool IsConnected => _isConnected;
}