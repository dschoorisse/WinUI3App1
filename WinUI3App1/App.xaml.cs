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
using MQTTnet.Protocol;
using System.Threading.Tasks;
using Microsoft.UI.Windowing; // Required for AppWindow and AppWindowPresenterKind
using WinRT.Interop;
using Microsoft.UI;          // Required for WindowNative and Win32Interop

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUI3App1
{
    // NL-L-PF4ZZ1V0
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static ILogger Logger { get; private set; }
        public static string PhotoboothIdentifier { get; private set; } // Store the ID here
        public static MqttService MqttServiceInstance { get; private set; } // Add MQTT Service instance

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            // Initialize Serilog logger
            ConfigureLogging();

            // --- Lees Photobooth ID ---
            PhotoboothIdentifier = AppSettings.PhotoboothId;
            if (string.IsNullOrWhiteSpace(PhotoboothIdentifier) || PhotoboothIdentifier.Contains("/") || PhotoboothIdentifier.Contains("+") || PhotoboothIdentifier.Contains("#"))
            {
                // Use a safe default if the ID is invalid for MQTT topics
                PhotoboothIdentifier = $"Photobooth_{Environment.MachineName.Replace(" ", "_")}";
                Logger.Warning("Invalid PhotoboothId found in settings, using default: {DefaultId}", PhotoboothIdentifier);
                AppSettings.PhotoboothId = PhotoboothIdentifier; // Save the safe default back
            }
            Logger.Information("Using Photobooth ID: {PhotoboothId}", PhotoboothIdentifier);
            // ---------------------------


            // Load MQTT settings (ensure these exist in AppSettings or add them)
            string mqttBroker = AppSettings.MqttBrokerAddress; // Needs to be added to AppSettings
            int mqttPort = AppSettings.MqttBrokerPort;         // Needs to be added to AppSettings
            string mqttUser = AppSettings.MqttUsername;       // Needs to be added to AppSettings
            string mqttPassword = AppSettings.MqttPassword;     // Needs to be added to AppSettings

            // ---- Initialize MQTT Service ----
            try
            {
                // Ensure required settings are present before initializing
                if (!string.IsNullOrEmpty(mqttBroker) && mqttPort > 0)
                {
                    // --- Geef PhotoboothIdentifier door aan de constructor ---
                    MqttServiceInstance = new MqttService(Logger, PhotoboothIdentifier, mqttBroker, mqttPort, mqttUser, mqttPassword);
                    MqttServiceInstance.ConnectionStatusChanged += MqttService_ConnectionStatusChanged;
                    Logger.Information("MQTT Service created for ID {PhotoboothId}.", PhotoboothIdentifier);
                }
                else
                {
                    Logger.Error("MQTT configuration missing (Broker Address or Port). MQTT Service not started.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize MQTT Service.");
                // Handle the error appropriately, maybe show a message to the user
            }
            // ---------------------------------

            Logger.Information("Application initialized");

            // Optional: Handle application exit to dispose MQTT service cleanly
            this.UnhandledException += App_UnhandledException; // Log unhandled exceptions
                                                               // Consider using Window Closed event or Suspending event for clean disposal
                                                               // For unpackaged apps, process exit might be the most reliable.
        }

        private async void MqttService_ConnectionStatusChanged(object? sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Information("MQTT [{PhotoboothId}] Connected. Publishing initial 'Online' status.", PhotoboothIdentifier);
                await PublishStatusAsync("online");
                // await PublishStatusJsonAsync("Online", true); // Voorbeeld met JSON
            }
            else
            {
                Logger.Information("MQTT [{PhotoboothId}] Disconnected.", PhotoboothIdentifier);
                // LWT handles the "Offline" message automatically
            }
        }

        // --- Helper Aangepast: Gebruikt PhotoboothIdentifier voor topic ---
        public static async Task PublishStatusAsync(string statusPayload, bool retain = false) // Added retain flag option
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                // Construct topic dynamically
                string topic = $"photobooth/{PhotoboothIdentifier}/status";
                try
                {
                    // Publish met QoS 1 en retain flag (optioneel, maar vaak nuttig voor status)
                    await MqttServiceInstance.PublishAsync(topic, statusPayload, MqttQualityOfServiceLevel.AtLeastOnce, retain);
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "MQTT [{PhotoboothId}] Failed to publish status '{Status}' to topic '{Topic}'", PhotoboothIdentifier, statusPayload, topic);
                }
            }
            else 
            {
                Logger?.Warning("MQTT [{PhotoboothId}] Not connected. Cannot publish status '{Status}'", PhotoboothIdentifier, statusPayload);
            }
        }

        // --- Helper Aangepast: Gebruikt ID voor topic en payload ---
        public static async Task PublishStatusJsonAsync(string state, bool? cameraConnected = null, bool retain = false) // Added retain flag
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                // Construct topic dynamically
                string topic = $"photobooth/{PhotoboothIdentifier}/status/json";
                try
                {
                    var statusObject = new
                    {
                        photoboothId = PhotoboothIdentifier, // Include ID in payload
                        state = state,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        cameraConnected = cameraConnected
                    };
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(statusObject);
                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MqttQualityOfServiceLevel.AtLeastOnce, retain); // QoS 1 & Retain
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "MQTT [{PhotoboothId}] Failed to publish JSON status '{State}' to topic '{Topic}'", PhotoboothIdentifier, state, topic);
                }
            }
            else
            {
                Logger?.Warning("MQTT [{PhotoboothId}] Not connected. Cannot publish JSON status '{Status}'", PhotoboothIdentifier);
            }
        }


        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger.Fatal(e.Exception, "Unhandled application exception");
            // Optionally: Show a message to the user before crashing
            e.Handled = true; // Prevent the application from crashing immediately, but it might still terminate
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
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args) // Make async if not already for MQTT start
        {
            MainWindow = new MainWindow();
            Logger.Information("Main window created");

            // --- Fullscreen Logic Based on Computer Name ---
            const string targetPhotoboothComputerName = "DESKTOP-NJDEOAK"; // <- IMPORTANT: Change this to your actual target computer name
            string currentComputerName = Environment.MachineName;

            Logger.Information("Current computer name: {ComputerName}. Target for fullscreen: {TargetComputerName}", currentComputerName, targetPhotoboothComputerName);

            if (currentComputerName.Equals(targetPhotoboothComputerName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Information("Computer name matches. Attempting to set window to fullscreen.");
                try
                {
                    // Get the AppWindow for the current MainWindow
                    IntPtr hWnd = WindowNative.GetWindowHandle(MainWindow);
                    WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                    AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

                    if (appWindow != null)
                    {
                        // Check if the current presenter is Overlapped, which is the default windowed mode.
                        // This check helps avoid errors if the window is already in a compact or fullscreen mode.
                        if (appWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped)
                        {
                            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen); // Set to Fullscreen
                            Logger.Information("Fullscreen mode has been set.");
                        }
                        else if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                        {
                            Logger.Information("Window is already in Fullscreen mode.");
                        }
                        else
                        {
                            // If it's in some other presenter kind (e.g., CompactOverlay),
                            // transitioning directly to FullScreen might not be desired or could fail.
                            Logger.Warning("Window is currently in {PresenterKind} mode. Fullscreen was not applied to avoid conflicts.", appWindow.Presenter.Kind);
                        }
                    }
                    else
                    {
                        Logger.Error("Could not retrieve AppWindow. Fullscreen mode cannot be set.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "An error occurred while trying to set fullscreen mode.");
                }
            }
            else
            {
                Logger.Information("Computer name does not match. Application will start in default windowed mode.");
                // Optionally, you could maximize the window on non-target machines if desired:
                // IntPtr hWnd = WindowNative.GetWindowHandle(MainWindow);
                // WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                // AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                // if (appWindow != null && appWindow.Presenter is OverlappedPresenter overlappedPresenter)
                // {
                //     overlappedPresenter.Maximize();
                //     Logger.Information("Window maximized on non-target machine.");
                // }
            }
            // --- End of Fullscreen Logic ---

            MainWindow.Activate();
            Logger.Information("Main window activated");

            // ---- Start MQTT Service ----
            if (MqttServiceInstance != null)
            {
                try
                {
                    // Using await here if OnLaunched is async, otherwise _ = ...
                    await MqttServiceInstance.StartAsync(); // Assuming StartAsync doesn't block UI for too long
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "MQTT [{PhotoboothId}] Failed to start MQTT Service on launch.", PhotoboothIdentifier);
                }
            }
            // ---------------------------

            MainWindow.Closed += async (sender, e) =>
            {
                Logger.Information("Main window closing for Photobooth ID: {PhotoboothId}. Disposing MQTT Service...", PhotoboothIdentifier);
                if (MqttServiceInstance != null)
                {
                    MqttServiceInstance.ConnectionStatusChanged -= MqttService_ConnectionStatusChanged;
                    await MqttServiceInstance.DisposeAsync();
                    Logger.Information("MQTT Service disposed on window close for Photobooth ID: {PhotoboothId}.", PhotoboothIdentifier);
                }
            };

            // Your existing m_window field seems unused, can be m_mainWindow or just use App.MainWindow static prop
            // m_window = MainWindow; 
        }

        private Window? m_window;
    }
}