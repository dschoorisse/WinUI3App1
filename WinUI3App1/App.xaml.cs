using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Serilog;
using Serilog.Events;
using Path = System.IO.Path;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUI3App1
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static ILogger Logger { get; private set; }

        public static EDSDK EDSDK { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            // Initialize Serilog logger
            ConfigureLogging();

            // Optional: Set DLL directory if needed
            string dllPath = System.IO.Path.Combine(AppContext.BaseDirectory);
            SetDllDirectory(dllPath);

            Logger.Information("Application initialized");
        }

        private void ConfigureLogging()
        {
            // Create logs directory if it doesn't exist
            string logsDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }

            // Configure Serilog
            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: Path.Combine(logsDirectory, "photobooth-log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug()
                .CreateLogger();

            Logger.Information("Logging initialized");
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            Logger.Information("Main window created");

            MainWindow.Activate();
            Logger.Information("Main window activated");

            LoadCanonSdk();
        }

        private void LoadCanonSdk()
        {
            Logger.Information("Starting Canon SDK initialization");

            try
            {
                // Step 1: Check process architecture
                string processArchitecture = Environment.Is64BitProcess ? "64-bit" : "32-bit";
                Logger.Information("Process is {ProcessArchitecture}", processArchitecture);

                // Step 2: Check if DLL exists
                string dllPath = Path.Combine(Path.Combine(AppContext.BaseDirectory, "EDSDK"), "EDSDK.dll");
                bool exists = File.Exists(dllPath);
                Logger.Information("EDSDK.dll exists at primary path: {Exists}", exists);

                if (!exists)
                {
                    // Try alternative path
                    dllPath = Path.Combine(AppContext.BaseDirectory, "NativeDlls", "EDSDK.dll");
                    exists = File.Exists(dllPath);
                    Logger.Information("EDSDK.dll exists at alternative path (NativeDlls): {Exists}", exists);
                }

                if (exists)
                {
                    // Step 3: Try to initialize with error handling around each step
                    Logger.Information("Attempting SDK initialization");

                    uint err = EDSDK.EdsInitializeSDK();
                    Logger.Information("SDK initialization result: 0x{Result:X}", err);

                    if (err == EDSDK.EDS_ERR_OK)
                    {
                        // Successfully initialized
                        Logger.Information("SDK was initialized successfully");
                        EDSDK.EdsTerminateSDK();
                        Logger.Information("SDK was terminated successfully");
                    }
                    else
                    {
                        Logger.Error("SDK initialization failed with error code: 0x{Result:X}", err);
                    }
                }
                else
                {
                    Logger.Error("Could not find EDSDK.dll in any of the expected locations");
                }
            }
            catch (DllNotFoundException ex)
            {
                Logger.Error(ex, "DLL not found");
            }
            catch (BadImageFormatException ex)
            {
                Logger.Error(ex, "Architecture mismatch - This usually means you're mixing 32-bit and 64-bit components");
            }
            catch (AccessViolationException ex)
            {
                Logger.Error(ex, "Memory access violation - This may indicate a calling convention or parameter marshaling issue");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exception occurred while initializing Canon SDK");
            }
        }

        private Window? m_window;
    }
}