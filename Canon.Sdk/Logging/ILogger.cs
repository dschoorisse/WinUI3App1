using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Canon.Sdk.Logging
{
    public interface ILogger
    {
        void Verbose(string message);
        void Debug(string message);
        void Information(string message);
        void Warning(string message);
        void Error(string message, Exception ex = null);
        void Fatal(string message, Exception ex = null);
    }
}
