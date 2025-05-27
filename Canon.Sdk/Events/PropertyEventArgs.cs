// Path: Canon.Sdk/Events/PropertyEventArgs.cs

using System;

namespace Canon.Sdk.Events
{
    public class PropertyEventArgs : EventArgs
    {
        public uint EventType { get; }
        public uint PropertyId { get; }
        public uint Parameter { get; }

        public PropertyEventArgs(uint eventType, uint propertyId, uint parameter)
        {
            EventType = eventType;
            PropertyId = propertyId;
            Parameter = parameter;
        }
    }
}