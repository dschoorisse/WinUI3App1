// Path: Canon.Sdk/Events/StateEventArgs.cs

using System;

namespace Canon.Sdk.Events
{
    public class StateEventArgs : EventArgs
    {
        public uint EventType { get; }
        public uint Parameter { get; }

        public StateEventArgs(uint eventType, uint parameter)
        {
            EventType = eventType;
            Parameter = parameter;
        }
    }
}