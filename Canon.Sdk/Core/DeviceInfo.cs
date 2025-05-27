// Path: Canon.Sdk/Core/DeviceInfo.cs

using System;
using EDSDKLib;

namespace Canon.Sdk.Core
{
    public class DeviceInfo
    {
        public string PortName { get; }
        public string DeviceDescription { get; }
        public uint DeviceSubType { get; }

        internal DeviceInfo(EDSDK.EdsDeviceInfo deviceInfo)
        {
            PortName = deviceInfo.szPortName;
            DeviceDescription = deviceInfo.szDeviceDescription;
            DeviceSubType = deviceInfo.DeviceSubType;
        }
    }
}