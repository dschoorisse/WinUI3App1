﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing; // Required for AppWindow and AppWindowPresenterKind
using WinRT.Interop;          // Required for WindowNative and Win32Interop
using Serilog;
using MQTTnet.Protocol;
using Microsoft.UI;
using Serilog.Events;
using WinUI3App;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

// Ensure this namespace matches your project, e.g., WinUI3App1
namespace WinUI3App1
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static ILogger Logger { get; private set; }

        public static string CurrentPageName { get; private set; } = "Initializing"; // Holds current page name

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
            // 1. Load settings FIRST - these are needed for logger and other initializations
            try
            {
                CurrentSettings = await SettingsManager.LoadSettingsAsync();
                if (CurrentSettings == null)
                {
                    // This case should ideally be handled by SettingsManager returning defaults
                    CurrentSettings = new PhotoBoothSettings(); // Emergency fallback
                    // Log this critical failure if possible with a pre-logger or Debug.WriteLine
                    System.Diagnostics.Debug.WriteLine("CRITICAL: Failed to load settings, CurrentSettings was null even after LoadSettingsAsync. Using emergency defaults.");
                    await SettingsManager.SaveSettingsAsync(CurrentSettings); // Attempt to save defaults
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL: Exception loading settings: {ex.Message}. Using emergency defaults.");
                CurrentSettings = new PhotoBoothSettings(); // Emergency fallback
            }

            // 2. Initialize PhotoboothIdentifier from loaded settings (needed for log topic)
            PhotoboothIdentifier = CurrentSettings.PhotoboothId;
            if (string.IsNullOrWhiteSpace(PhotoboothIdentifier) ||
                PhotoboothIdentifier.Contains("/") || PhotoboothIdentifier.Contains("+") || PhotoboothIdentifier.Contains("#"))
            {
                string oldId = PhotoboothIdentifier;
                PhotoboothIdentifier = $"Photobooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}";
                // Cannot use App.Logger here yet as it's not initialized. Use Debug.WriteLine or queue log.
                System.Diagnostics.Debug.WriteLine($"WARN: Invalid PhotoboothId ('{oldId}') in settings, using default: {PhotoboothIdentifier}");
                CurrentSettings.PhotoboothId = PhotoboothIdentifier;
                await SettingsManager.SaveSettingsAsync(CurrentSettings); // Save corrected ID
            }

            // 3. Now configure logging (Logger will be assigned here)
            ConfigureLogging(); // This will now have access to CurrentSettings and PhotoboothIdentifier

            Logger.Information("Application launching... Settings loaded. Photobooth ID: {PhotoboothId}", PhotoboothIdentifier);

            // 4. Initialize MQTT Service (for commands, status, etc.)
            // ... (Your existing MQTT Service initialization logic using CurrentSettings for broker, port, user, pass) ...
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
                    MqttServiceInstance.SettingsUpdatedRemotely += OnRemoteSettingsUpdated;
                    Logger.Information("MQTT Service instance created for Photobooth ID {PhotoboothId}.", PhotoboothIdentifier);
                }
                else { Logger.Error("MQTT configuration missing in settings. MQTT Service not started."); }
            }
            catch (Exception ex) { Logger.Error(ex, "Failed to initialize MQTT Service."); }

            // 5. Main window creation and Fullscreen Logic ---
            MainWindow = new MainWindow();
            Logger.Information("Main window created");

            const string targetPhotoboothComputerName = "DESKTOP-NJDEOAK"; // User's hardcoded name
            string currentComputerName = Environment.MachineName;
            Logger.Information("Current computer name: {ComputerName}. Target for fullscreen: {TargetComputerName}", currentComputerName, targetPhotoboothComputerName);

            // 6. Setup Navigation Tracking and Initial Page State for MQTT
            if (App.MainWindow is MainWindow mwInstance && mwInstance.AppFrame != null)
            {
                // Get initial page name after MainWindow constructor has navigated
                if (mwInstance.AppFrame.Content is Page initialPage)
                {
                    CurrentPageName = initialPage.GetType().Name;
                }
                else // Fallback if Content is not yet a Page (less likely here)
                {
                    CurrentPageName = mwInstance.AppFrame.SourcePageType?.Name ?? "MainPage";
                }
                Logger.Information("App: Initial page detected: {CurrentPageName}", CurrentPageName);

                // Subscribe for subsequent navigations
                mwInstance.AppFrame.Navigated += RootFrame_Navigated;
            }
            else { Logger.Error("App: Could not find AppFrame in MainWindow to subscribe to navigation events."); }


            #region fullscreen or windowed
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
            #endregion

            MainWindow.Activate();
            Logger.Information("Main window activated");

            // 7. Start MQTT Service Connection
            if (MqttServiceInstance != null)
            {
                try { await MqttServiceInstance.StartAsync(); }
                catch (Exception ex) { Logger.Error(ex, "MQTT Service failed to start connection on launch for {PhotoboothId}.", PhotoboothIdentifier); }
            }

            // 8. Setup other app-level handlers
            MainWindow.Closed += OnMainWindowClosed;
            this.UnhandledException += App_UnhandledException;
            Logger.Information("Application initialization complete.");

            this.UnhandledException += App_UnhandledException;
            Logger.Information("Application initialized and launched.");

            // Send status over MQTT
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                await TriggerStatusUpdate(appState: "Launched");
            }
            else
            {
                Logger.Warning("MQTT Service not connected. Status update not sent.");
            }
        }

        private static async void RootFrame_Navigated(object sender, NavigationEventArgs e)
        {
            string newPageName = "Unknown";
            if (e.Content is Page page) // Get name from the actual Page instance
            {
                newPageName = page.GetType().Name;
            }
            else if (e.SourcePageType != null) // Fallback to the type used for navigation
            {
                newPageName = e.SourcePageType.Name;
            }

            if (CurrentPageName != newPageName)
            {
                string previousPageName = CurrentPageName;
                CurrentPageName = newPageName;
                Logger?.Information("App: Navigated from {PreviousPageName} to {CurrentPageName}", previousPageName, CurrentPageName);

                // Trigger an MQTT status update to reflect the new page
                // Using a generic "active" state; specific page actions might set more detailed states.
                if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
                {
                    await TriggerStatusUpdate(appState: "PageChanged");
                }
            }
        }

        // Centralized method to publish the full JSON status
        public static async Task TriggerStatusUpdate(string appState = "active", bool? cameraConnectedStatus = null, bool retain = true)
        {
            // cameraConnectedStatus could be fetched from a global state or service if available,
            // otherwise it's passed in or defaults to null.
            // For now, we don't have a global camera status readily available here.
            await PublishStatusJsonAsync(appState, cameraConnectedStatus, retain);
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
                        currentPage = CurrentPageName, // Include current page name
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
            if (!Directory.Exists(logsDirectory)) Directory.CreateDirectory(logsDirectory);

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug() // Overall minimum level for logs processed by Serilog
                .Enrich.FromLogContext() // Allows adding contextual information to logs
                .Enrich.WithProperty("PhotoboothID", PhotoboothIdentifier) // Add PhotoboothID to all log events
                .Enrich.WithProperty("Application", "PhotoBoothApp") // Example static enrichment
                .WriteTo.File(
                    path: Path.Combine(logsDirectory, "photobooth-log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7, // Example: Keep 7 days of logs
                                               // Using a more detailed output template for file logs
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) ID:{PhotoboothID} {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug(); // Standard debug output

            // ---- Add MQTT Sink Configuration ----
            if (CurrentSettings != null &&
                !string.IsNullOrEmpty(CurrentSettings.MqttBrokerAddress) &&
                CurrentSettings.MqttBrokerPort > 0 &&
                !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                LogEventLevel mqttMinLevel = LogEventLevel.Information; // Default minimum level to send to MQTT
                if (Enum.TryParse<LogEventLevel>(CurrentSettings.LogLevel, true, out var configuredLevel))
                {
                    mqttMinLevel = configuredLevel; // Use LogLevel from settings.json for this sink
                }

                string mqttLogTopic = $"photobooth/{PhotoboothIdentifier}/logs"; // Single topic per device

                //
                // ** IMPORTANT: Placeholder for your chosen Serilog MQTT Sink Configuration **
                // You MUST replace the commented-out section below with the actual configuration
                // code for the Serilog MQTT sink NuGet package you have installed.
                // Refer to that package's documentation for the correct syntax.
                //
                // The example below is purely conceptual.
                //
                /*
                try
                {
                    // --- EXAMPLE: Configuring a hypothetical MQTT sink ---
                    // This assumes the sink package provides a .MQTT() extension method
                    // and handles its own MQTT client creation based on options.

                    // var mqttClientOptionsForLogging = new MqttClientOptionsBuilder()
                    //    .WithTcpServer(CurrentSettings.MqttBrokerAddress, CurrentSettings.MqttBrokerPort)
                    //    .WithClientId($"photobooth_{PhotoboothIdentifier}_logSink_{Guid.NewGuid().ToString("N").Substring(0,8)}") // Ensure unique client ID
                    //    // Add .WithCredentials(CurrentSettings.MqttUsername, CurrentSettings.MqttPassword) if your sink needs explicit client options with auth
                    //    .Build();
                    
                    // loggerConfiguration.WriteTo.SomeSpecificMqttSink( // Replace .SomeSpecificMqttSink with the actual method
                    //    clientOptions: mqttClientOptionsForLogging, // Or individual parameters like host, port, etc.
                    //    topic: mqttLogTopic,
                    //    formatter: new RenderedCompactJsonFormatter(), // For JSON structured logs
                    //    restrictedToMinimumLevel: mqttMinLevel,
                    //    retained: false, // Log messages should not be retained
                    //    qualityOfServiceLevel: MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce // Or AtLeastOnce
                    // );
                    // --- End of Example ---

                    // If the Logger is already created at this point, use it. Otherwise, this log line might be too early.
                    // System.Diagnostics.Debug.WriteLine($"INFO: MQTT Logging Sink configured (conceptually). Topic: {mqttLogTopic}, MinLevel: {mqttMinLevel}");
                }
                catch (Exception ex)
                {
                    // System.Diagnostics.Debug.WriteLine($"ERROR: Failed to configure MQTT Logging Sink: {ex.Message}");
                }
                */
                System.Diagnostics.Debug.WriteLine($"INFO: MQTT Logging Sink is a PLACEHOLDER. Topic: {mqttLogTopic}, MinLevel: {mqttMinLevel}. You need to install and configure a real Serilog MQTT sink.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("WARN: MQTT Broker details, PhotoboothIdentifier, or CurrentSettings not available. MQTT logging sink will NOT be enabled.");
            }
            // ---- End of MQTT Sink Configuration ----

            Logger = loggerConfiguration.CreateLogger();
            Log.Logger = Logger; // Assign to Serilog's global static logger for convenience
                                 // (some libraries or parts of your code might use Log.Information directly)
        }

        private async void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            Logger.Information("Main window closing for Photobooth ID: {PhotoboothId}. Disposing MQTT Service...", PhotoboothIdentifier);
            if (MqttServiceInstance != null)
            {
                MqttServiceInstance.ConnectionStatusChanged -= MqttService_ConnectionStatusChanged;
                MqttServiceInstance.SettingsUpdatedRemotely -= OnRemoteSettingsUpdated;
                await MqttServiceInstance.DisposeAsync();
                Logger.Information("MQTT Service disposed on window close for Photobooth ID: {PhotoboothId}.", PhotoboothIdentifier);
            }
            Logger.Information("Flushing logs and closing application.");
            await Log.CloseAndFlushAsync(); // Ensure Serilog flushes all sinks
        }


        private async void OnRemoteSettingsUpdated(object sender, EventArgs e)
        {
            Logger.Information("App: Event received - Remote settings have been updated. Reloading settings now.");

            // Ensure this runs on the UI thread if it has potential to touch UI or navigate,
            // though just updating App.CurrentSettings and PhotoboothIdentifier should be safe.
            // MainWindow.DispatcherQueue?.TryEnqueue(async () => { ... }); might be needed for more complex UI reactions.

            var newSettings = await SettingsManager.LoadSettingsAsync();
            if (newSettings != null)
            {
                CurrentSettings = newSettings; // Update the globally accessible settings object

                // Re-evaluate PhotoboothIdentifier if it could have changed remotely
                string oldId = PhotoboothIdentifier;
                PhotoboothIdentifier = CurrentSettings.PhotoboothId;
                if (string.IsNullOrWhiteSpace(PhotoboothIdentifier) || PhotoboothIdentifier.Contains("/") /*...etc...*/ )
                {
                    PhotoboothIdentifier = $"Photobooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}";
                    Logger.Warning("App: Invalid PhotoboothId ('{OldRemoteId}') in remotely updated settings, using default: {DefaultId}", oldId, PhotoboothIdentifier);
                    CurrentSettings.PhotoboothId = PhotoboothIdentifier;
                    // Save back the corrected ID if it was bad from remote source (rare, server should send valid ID)
                    await SettingsManager.SaveSettingsAsync(CurrentSettings, true); // true to keep remote timestamp if possible or handle appropriately
                }
                if (oldId != PhotoboothIdentifier)
                {
                    Logger.Information("App: PhotoboothIdentifier changed via remote settings from '{OldId}' to '{NewId}'. MQTT service might need re-initialization if critical topics changed.", oldId, PhotoboothIdentifier);
                    // TODO: Handle MqttService re-initialization if PhotoboothIdentifier affects its core topics/LWT.
                    // This might involve disposing and newing up MqttServiceInstance.
                }

                Logger.Information("App: Settings reloaded. Current Photobooth ID: {PhotoboothId}", PhotoboothIdentifier);

                // TODO: Implement a strategy to notify active pages to refresh their UI.
                // For example, if MainPage is active, call a public RefreshUI() method on it.
                if (MainWindow.Content is Frame rootFrame)
                {
                    if (rootFrame.Content is MainPage mainPageInstance)
                    {
                        Logger.Information("App: Requesting MainPage to refresh its UI from new settings.");
                        mainPageInstance.LoadDynamicUITexts(); // Re-load texts
                        _ = mainPageInstance.LoadBackgroundFromSettings(); // Re-load background (make LoadBackground public or call a wrapper)
                    }
                    else if (rootFrame.Content is PhotoBoothPage photoBoothPageInstance)
                    {
                        // PhotoBoothPage loads texts in its Loaded event. If it's already loaded,
                        // it might need an explicit refresh method or re-navigation.
                        // For now, it will pick up new texts if it's reloaded/re-navigated to.
                        // Consider adding a RefreshUITexts() method to PhotoBoothPage as well.
                        Logger.Information("App: PhotoBoothPage is active. It will use new settings on next load or if it has a refresh mechanism.");
                    }
                }
                // A more robust way is an event aggregator or messaging system for cross-page communication.
            }
            else
            {
                Logger.Error("App: Failed to reload settings after remote update notification.");
            }
        }

        // DllImport for SetDllDirectory can be removed if not actively used for other purposes.
        // If it was for a specific SDK path, ensure that SDK is now correctly referenced or its path managed.
        // [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        // private static extern bool SetDllDirectory(string lpPathName);

        // m_window field is not used, App.MainWindow static property is used instead.
        // private Window? m_window; 
    }
}