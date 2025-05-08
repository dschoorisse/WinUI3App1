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
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            Logger.Information("Main window created");

            MainWindow.Activate();
            Logger.Information("Main window activated");

            // ---- Start MQTT Service ----
            if (MqttServiceInstance != null)
            {
                try
                {
                    // Start the MQTT client asynchronously
                    // We don't necessarily await this here, it will run in the background
                    try { _ = MqttServiceInstance.StartAsync(); }
                    catch (Exception ex) { Logger.Error(ex, "MQTT [{PhotoboothId}] Failed to start MQTT Service.", PhotoboothIdentifier); }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to start MQTT Service.");
                }
            }
            // ---------------------------

            // Optional: Handle Window Closed event for cleanup
            MainWindow.Closed += async (sender, e) =>
            {
                Logger.Information("Main window closing for Photobooth ID: {PhotoboothId}. Disposing MQTT Service...", PhotoboothIdentifier);
                if (MqttServiceInstance != null)
                {
                    // BELANGRIJK: Koppel de event handler los VOORDAT je DisposeAsync aanroept
                    MqttServiceInstance.ConnectionStatusChanged -= MqttService_ConnectionStatusChanged;
                    await MqttServiceInstance.DisposeAsync();
                    Logger.Information("MQTT Service disposed on window close for Photobooth ID: {PhotoboothId}.", PhotoboothIdentifier);
                }
            };
        }

        private Window? m_window;
    }
}