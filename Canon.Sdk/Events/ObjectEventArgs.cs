// Path: Canon.Sdk/Events/ObjectEventArgs.cs

using System;

namespace Canon.Sdk.Events
{
    public class ObjectEventArgs : EventArgs
    {
        public uint EventType { get; }
        public IntPtr ObjectPointer { get; }
        public bool Handled { get; set; }

        public ObjectEventArgs(uint eventType, IntPtr objectPointer)
        {
            EventType = eventType;
            ObjectPointer = objectPointer;
            Handled = false;
        }
    }
}