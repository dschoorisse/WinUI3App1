using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing; // Required for AppWindow and AppWindowPresenterKind
using WinRT.Interop;          // Required for WindowNative and Win32Interop
using Serilog;
// Removed: using Windows.ApplicationModel; // No longer explicitly needed here
// Removed: using Windows.ApplicationModel.Activation; // Covered by LaunchActivatedEventArgs argument type
// Using System.Text.Json implicitly via SettingsManager if needed, but not directly here.
using MQTTnet.Protocol;
using Microsoft.UI; // Already present

// Ensure this namespace matches your project, e.g., WinUI3App1
namespace WinUI3App1
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static ILogger Logger { get; private set; }

        // These will be populated from PhotoBoothSettings loaded via SettingsManager
        public static string PhotoboothIdentifier { get; private set; }
        public static PhotoBoothSettings CurrentSettings { get; private set; } // Expose loaded settings

        public static MqttService MqttServiceInstance { get; private set; }

        public App()
        {
            this.InitializeComponent();
            ConfigureLogging(); // Logger should be configured first

            // Settings-dependent initializations are moved to OnLaunched after settings are loaded
            // to allow for async loading of settings.
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Logger.Information("Application launching...");

            // Load settings first
            try
            {
                CurrentSettings = await SettingsManager.LoadSettingsAsync();
                if (CurrentSettings == null)
                {
                    Logger.Error("Failed to load settings, CurrentSettings is null. Using emergency defaults.");
                    CurrentSettings = new PhotoBoothSettings(); // Fallback to code defaults
                    // Optionally, try to save these emergency defaults if SettingsManager didn't
                    await SettingsManager.SaveSettingsAsync(CurrentSettings);
                }
                Logger.Information("Settings loaded/initialized.");
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "CRITICAL: Failed to load settings during OnLaunched. Application might not function correctly.");
                // Handle critical failure: show error, use hardcoded emergency defaults, or exit.
                CurrentSettings = new PhotoBoothSettings(); // Use code defaults as a last resort
            }

            // --- Initialize PhotoboothIdentifier from loaded settings ---
            PhotoboothIdentifier = CurrentSettings.PhotoboothId;
            if (string.IsNullOrWhiteSpace(PhotoboothIdentifier) ||
                PhotoboothIdentifier.Contains("/") ||
                PhotoboothIdentifier.Contains("+") ||
                PhotoboothIdentifier.Contains("#"))
            {
                string oldId = PhotoboothIdentifier;
                PhotoboothIdentifier = $"Photobooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}";
                Logger.Warning("Invalid PhotoboothId ('{OldId}') found in settings, using default: {DefaultId}", oldId, PhotoboothIdentifier);
                CurrentSettings.PhotoboothId = PhotoboothIdentifier; // Update the model
                await SettingsManager.SaveSettingsAsync(CurrentSettings); // Save the corrected ID back to JSON
            }
            Logger.Information("Using Photobooth ID: {PhotoboothId}", PhotoboothIdentifier);

            // --- Initialize MQTT Service with loaded settings ---
            string mqttBroker = CurrentSettings.MqttBrokerAddress;
            int mqttPort = CurrentSettings.MqttBrokerPort;
            string mqttUser = CurrentSettings.MqttUsername;
            string mqttPassword = CurrentSettings.MqttPassword;

            try
            {
                if (!string.IsNullOrEmpty(mqttBroker) && mqttPort > 0)
                {
                    MqttServiceInstance = new MqttService(Logger, PhotoboothIdentifier, mqttBroker, mqttPort, mqttUser, mqttPassword);
                    MqttServiceInstance.ConnectionStatusChanged += MqttService_ConnectionStatusChanged;
                    Logger.Information("MQTT Service created for ID {PhotoboothId}.", PhotoboothIdentifier);
                }
                else
                {
                    Logger.Error("MQTT configuration missing in settings (Broker Address or Port). MQTT Service not started.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize MQTT Service using settings from JSON.");
            }

            // --- MainWindow Creation and Fullscreen Logic ---
            MainWindow = new MainWindow();
            Logger.Information("Main window created");

            const string targetPhotoboothComputerName = "DESKTOP-NJDEOAK"; // User's hardcoded name
            string currentComputerName = Environment.MachineName;
            Logger.Information("Current computer name: {ComputerName}. Target for fullscreen: {TargetComputerName}", currentComputerName, targetPhotoboothComputerName);

            if (currentComputerName.Equals(targetPhotoboothComputerName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Information("Computer name matches. Attempting to set window to fullscreen.");
                try
                {
                    IntPtr hWnd = WindowNative.GetWindowHandle(MainWindow);
                    WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                    AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                    if (appWindow != null)
                    {
                        if (appWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped)
                        {
                            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                            Logger.Information("Fullscreen mode has been set.");
                        }
                        // ... (other presenter kind checks as before) ...
                        else if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen) Logger.Information("Window is already in Fullscreen mode.");
                        else Logger.Warning("Window is currently in {PresenterKind} mode. Fullscreen was not applied.", appWindow.Presenter.Kind);
                    }
                    else Logger.Error("Could not retrieve AppWindow. Fullscreen mode cannot be set.");
                }
                catch (Exception ex) { Logger.Error(ex, "An error occurred while trying to set fullscreen mode."); }
            }
            else Logger.Information("Computer name does not match. Application will start in default windowed mode.");

            MainWindow.Activate();
            Logger.Information("Main window activated");

            // ---- Start MQTT Service (actual connection attempt) ----
            if (MqttServiceInstance != null)
            {
                try
                {
                    await MqttServiceInstance.StartAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "MQTT [{PhotoboothId}] Failed to start MQTT Service connection on launch.", PhotoboothIdentifier);
                }
            }

            // --- MainWindow Closed Event Handler ---
            MainWindow.Closed += async (sender, e) =>
            {
                Logger.Information("Main window closing for Photobooth ID: {PhotoboothId}. Disposing MQTT Service...", PhotoboothIdentifier);
                if (MqttServiceInstance != null)
                {
                    MqttServiceInstance.ConnectionStatusChanged -= MqttService_ConnectionStatusChanged;
                    await MqttServiceInstance.DisposeAsync();
                    Logger.Information("MQTT Service disposed on window close for Photobooth ID: {PhotoboothId}.", PhotoboothIdentifier);
                }
                Log.CloseAndFlush(); // Ensure Serilog flushes on exit
            };

            this.UnhandledException += App_UnhandledException;
            Logger.Information("Application initialized and launched.");
        }

        private async void MqttService_ConnectionStatusChanged(object? sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Information("MQTT [{PhotoboothId}] Connected. Publishing initial 'Online' status.", PhotoboothIdentifier);
                await PublishStatusAsync("online", true); // Retain online status
            }
            else
            {
                Logger.Information("MQTT [{PhotoboothId}] Disconnected.", PhotoboothIdentifier);
            }
        }

        public static async Task PublishStatusAsync(string statusPayload, bool retain = false)
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/status";
                try
                {
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

        public static async Task PublishStatusJsonAsync(string state, bool? cameraConnected = null, bool retain = false)
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/status/json";
                try
                {
                    var statusObject = new
                    {
                        photoboothId = PhotoboothIdentifier,
                        state,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        cameraConnected
                    };
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(statusObject);
                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MqttQualityOfServiceLevel.AtLeastOnce, retain);
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "MQTT [{PhotoboothId}] Failed to publish JSON status '{State}' to topic '{Topic}'", PhotoboothIdentifier, state, topic);
                }
            }
            else
            {
                Logger?.Warning("MQTT [{PhotoboothId}] Not connected. Cannot publish JSON status '{State}'", PhotoboothIdentifier, state);
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger.Fatal(e.Exception, "Unhandled application exception. Attempting to handle.");
            e.Handled = true;
            // Consider showing a dialog to the user here if it's a UI thread exception
            // and then perhaps gracefully shutting down or attempting recovery.
        }

        private void ConfigureLogging()
        {
            string logsDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: Path.Combine(logsDirectory, "photobooth-log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7, // Keep logs for 7 days
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug()
                .Enrich.FromLogContext() // Enables SourceContext for MqttService logging
                .CreateLogger();
            Log.Logger = Logger; // Assign to Serilog's global logger if MqttService uses Log.ForContext
            Logger.Information("Logging initialized");
        }

        // DllImport for SetDllDirectory can be removed if not actively used for other purposes.
        // If it was for a specific SDK path, ensure that SDK is now correctly referenced or its path managed.
        // [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        // private static extern bool SetDllDirectory(string lpPathName);

        // m_window field is not used, App.MainWindow static property is used instead.
        // private Window? m_window; 
    }
}