using Canon.Sdk.Logging; // Reference the SDK project's interface
using Serilog;
using System;


namespace WinUI3App1
{
    public class SerilogSdkAdapter : Canon.Sdk.Logging.ILogger
    {
        private readonly Serilog.ILogger _serilogLogger;

        // The adapter takes the real Serilog logger
        public SerilogSdkAdapter(Serilog.ILogger serilogLogger)
        {
            _serilogLogger = serilogLogger;
        }

        // Implement the interface by forwarding calls to Serilog
        public void Debug(string message) => _serilogLogger.Debug(message);
        public void Information(string message) => _serilogLogger.Information(message);
        public void Warning(string message) => _serilogLogger.Warning(message);
        public void Error(string message, Exception ex = null)
        {
            if (ex != null)
                _serilogLogger.Error(ex, message);
            else
                _serilogLogger.Error(message);
        }
    }
}
