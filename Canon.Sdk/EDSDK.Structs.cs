using System;
using System.Runtime.InteropServices;

namespace EDSDKLib
{
    public partial class EDSDK
    {
        public const int EDS_MAX_NAME = 256;
        public const int EDS_TRANSFER_BLOCK_SIZE = 512;

        /*-----------------------------------------------------------------------------
         Point
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsPoint
        {
            public int x;
            public int y;
        }

        /*-----------------------------------------------------------------------------
         Rectangle
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsRect
        {
            public int x;
            public int y;
            public int width;
            public int height;
        }

        /*-----------------------------------------------------------------------------
         Size
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsSize
        {
            public int width;
            public int height;
        }

        /*-----------------------------------------------------------------------------
         Rational
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsRational
        {
            public int Numerator;
            public uint Denominator;
        }

        /*-----------------------------------------------------------------------------
         Time
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsTime
        {
            public int Year;
            public int Month;
            public int Day;
            public int Hour;
            public int Minute;
            public int Second;
            public int Milliseconds;
        }

        /*-----------------------------------------------------------------------------
         Device Info
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsDeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = EDS_MAX_NAME)]
            public string szPortName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = EDS_MAX_NAME)]
            public string szDeviceDescription;

            public uint DeviceSubType;

            public uint reserved;
        }

        /*-----------------------------------------------------------------------------
         Volume Info
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsVolumeInfo
        {
            public uint StorageType;
            public uint Access;
            public ulong MaxCapacity;
            public ulong FreeSpaceInBytes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = EDS_MAX_NAME)]
            public string szVolumeLabel;
        }


        /*-----------------------------------------------------------------------------
         DirectoryItem Info
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsDirectoryItemInfo
        {
            public UInt64 Size;
            public int isFolder;
            public uint GroupID;
            public uint Option;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = EDS_MAX_NAME)]
            public string szFileName;

            public uint format;
            public uint dateTime;
        }


        /*-----------------------------------------------------------------------------
         Image Info
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsImageInfo
        {
            public uint Width;                  // image width 
            public uint Height;                 // image height

            public uint NumOfComponents;        // number of color components in image.
            public uint ComponentDepth;         // bits per sample.  8 or 16.

            public EdsRect EffectiveRect;       // Effective rectangles except 
                                                // a black line of the image. 
                                                // A black line might be in the top and bottom
                                                // of the thumbnail image. 

            public uint reserved1;
            public uint reserved2;

        }

        /*-----------------------------------------------------------------------------
         SaveImage Setting
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsSaveImageSetting
        {
            public uint JPEGQuality;
            IntPtr iccProfileStream;
            public uint reserved;
        }

        /*-----------------------------------------------------------------------------
         Property Desc
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsPropertyDesc
        {
            public int Form;
            public uint Access;
            public int NumElements;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public int[] PropDesc;
        }


        /*-----------------------------------------------------------------------------
         Property DescEx
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsPropertyDescEx
        {
            public int Form;
            public uint Access;
            public int NumElements;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public Int64[] PropDesc;
        }


        /*-----------------------------------------------------------------------------
         Picture Style Desc
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsPictureStyleDesc
        {
            public int contrast;
            public uint sharpness;
            public int saturation;
            public int colorTone;
            public uint filterEffect;
            public uint toningEffect;
            public uint sharpFineness;
            public uint sharpThreshold;
        }

        /*-----------------------------------------------------------------------------
         Focus Info
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsFocusPoint
        {
            public uint valid;
            public uint selected;
            public uint justFocus;
            public EdsRect rect;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EdsFocusInfo
        {
            public EdsRect imageRect;
            public uint pointNumber;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1053)]
            public EdsFocusPoint[] focusPoint;
            public uint executeMode;
        }


        /*-----------------------------------------------------------------------------
         Capacity
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        public struct EdsCapacity
        {
            public int NumberOfFreeClusters;
            public int BytesPerSector;
            public int Reset;
        }

        /*-----------------------------------------------------------------------------
         AngleInformation
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsCameraPos
        {
            public int status;
            public int position;
            public int rolling;
            public int pitching;
        }

        /*-----------------------------------------------------------------------------
        FocusBractingSetting
       -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct FocusShiftSetting
        {
            public uint Version;
            public uint FocusShiftFunction;
            public uint ShootingNumber;
            public uint StepWidth;
            public uint ExposureSmoothing;
            public uint FocusStackingFunction;
            public uint FocusStackingTrimming;
            public uint FlashInterval;
        }

        /*-----------------------------------------------------------------------------
         Manual WhiteBalance Data
        -----------------------------------------------------------------------------*/
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsManualWBData
        {
            public uint Valid;
            public uint dataSize;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szCaption;

            [MarshalAs(UnmanagedType.ByValArray)]
            public byte[] data;
        }
    }
}