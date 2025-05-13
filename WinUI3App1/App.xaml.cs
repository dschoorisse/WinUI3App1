using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing; // Required for AppWindow and AppWindowPresenterKind
using WinRT.Interop;          // Required for WindowNative and Win32Interop
using Serilog;
using Serilog.Formatting.Compact;
using MQTTnet.Protocol;
using Microsoft.UI;
using WinUI3App;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Net.Http;
using Windows.Storage;
using System.Security.Cryptography;

// Ensure this namespace matches your project, e.g., WinUI3App1
namespace WinUI3App1
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static ILogger Logger { get; private set; }
        public static MqttService MqttServiceInstance { get; private set; }
        public static string CurrentPageName { get; private set; } = "Initializing"; // Holds current page name

        // These will be populated from PhotoBoothSettings loaded via SettingsManager
        public static string PhotoboothIdentifier { get; private set; }
        public static PhotoBoothSettings CurrentSettings { get; private set; } // Expose loaded settings

        // Heartbeat timer MQTT
        private static Timer _heartbeatTimerMqtt; 
        private static readonly TimeSpan HeartbeatIntervalMqtt = TimeSpan.FromSeconds(10); // Configurable interval

        // Heartbeat timer text logging
        private static Timer _heartbeatTimerLogging;
        private static readonly TimeSpan HeartbeatIntervalLogging = TimeSpan.FromSeconds(300); // Configurable interval

        // Global state
        private static PhotoBoothState _state = PhotoBoothState.Idle; // will be used to track the current state of the app, updates will automatically trigger MQTT status updates


        public App()
        {
            this.InitializeComponent();

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
                    Logger.Debug("MQTT Service instance created for Photobooth ID {PhotoboothId}.", PhotoboothIdentifier);
                }
                else { Logger.Error("MQTT configuration missing in settings. MQTT Service not started."); }
            }
            catch (Exception ex) { Logger.Error(ex, "Failed to initialize MQTT Service."); }

            // 5. Main window creation and Fullscreen Logic ---
            MainWindow = new MainWindow();
            Logger.Debug("Main window created");

            const string targetPhotoboothComputerName = "DESKTOP-NJDEOAK"; // User's hardcoded name, TODO: create list and populoate from JSON
            string currentComputerName = Environment.MachineName;
            Logger.Debug("Current computer name: {ComputerName}. Target for fullscreen: {TargetComputerName}", currentComputerName, targetPhotoboothComputerName);

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
                Logger.Debug("App: Initial page detected: {CurrentPageName}", CurrentPageName);

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
                        else if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                        {
                            Logger.Information("Window is already in Fullscreen mode.");
                        }
                        else
                        {
                            Logger.Warning("Window is currently in {PresenterKind} mode. Fullscreen was not applied.", appWindow.Presenter.Kind);
                        }
                    }
                    else Logger.Error("Could not retrieve AppWindow. Fullscreen mode cannot be set.");
                }
                catch (Exception ex) 
                { 
                    Logger.Error(ex, "An error occurred while trying to set fullscreen mode."); 
                }
            }
            else Logger.Information("Computer name does not match. Application will start in default windowed mode.");
            #endregion

            MainWindow.Activate();
            Logger.Debug("Main window activated");

            // 7. Start MQTT Service Connection
            if (MqttServiceInstance != null)
            {
                try { await MqttServiceInstance.StartAsync(); }
                catch (Exception ex) { Logger.Error(ex, "MQTT Service failed to start connection on launch for {PhotoboothId}.", PhotoboothIdentifier); }
            }

            // Send status over MQTT
            State = PhotoBoothState.Starting;

            // 8. Setup other app-level handlers
            MainWindow.Closed += OnMainWindowClosed;
            this.UnhandledException += App_UnhandledException;
            Logger.Information("Application initialization complete.");

            this.UnhandledException += App_UnhandledException;
            Logger.Information("Application initialized and launched.");

            // Subscribe to the event from SettingsManager, when settings are changed we send the new settings over MQTT
            SettingsManager.OnSettingsWrittenToDisk += App_OnSettingsWrittenToDisk_Handler; // Corrected handler name
                       
            #region Setup heartbeat timers
            // MQTT
            try
            {
                // Start the timer. First heartbeat after 'HeartbeatIntervalMqtt', then repeat at 'HeartbeatIntervalMqtt'.
                // If you want an immediate first heartbeat for testing, set the dueTime to TimeSpan.Zero or a small value.
                _heartbeatTimerMqtt = new Timer(
                    callback: LogHeartbeatMqtt,
                    state: null,             // No state object needed for the callback
                    dueTime: TimeSpan.Zero,  // Time to wait before the first tick, immediately 
                    period: HeartbeatIntervalMqtt); // Interval between subsequent ticks

                Logger.Verbose("Application MQTT Heartbeat logging timer started. Interval: {HeartbeatIntervalMqtt}", HeartbeatIntervalMqtt);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize or start the heartbeat timer.");
            }

            // Text logs
            try
            {
                // Start the timer. First heartbeat after 'HeartbeatIntervalMqtt', then repeat at 'HeartbeatIntervalMqtt'.
                // If you want an immediate first heartbeat for testing, set the dueTime to TimeSpan.Zero or a small value.
                _heartbeatTimerLogging = new Timer(
                    callback: LogHeartbeatLogging,
                    state: null,             // No state object needed for the callback
                    dueTime: TimeSpan.Zero,  // Time to wait before the first tick, immediately 
                    period: HeartbeatIntervalLogging); // Interval between subsequent ticks

                Logger.Verbose("Application Logging Heartbeat logging timer started. Interval: {HeartbeatIntervalLogging}", HeartbeatIntervalLogging);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize or start the heartbeat timer.");
            }

            #endregion
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

            // Log only if the page name has changed to avoid excessive logging
            if (CurrentPageName != newPageName)
            {
                string previousPageName = CurrentPageName;
                CurrentPageName = newPageName;
                Logger?.Information("App: Navigated from {PreviousPageName} to {CurrentPageName}", previousPageName, CurrentPageName);

                // Trigger an MQTT status update to reflect the new page
                // Using a generic "active" state; specific page actions might set more detailed states.
                if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
                {
                    await PublishPhotoBoothStatusJsonAsync();
                }
            }
        }

        private async void MqttService_ConnectionStatusChanged(object? sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Information("MQTT: Connected. Publishing initial 'Online' status.", PhotoboothIdentifier);
                await PublishConnectionStatusAsync("online"); // Retain online status
            }
            else
            {
                Logger.Information("MQTT: Disconnected.", PhotoboothIdentifier);
            }
        }

        public static async Task PublishConnectionStatusAsync(string statusPayload)
        {
            // The connection status should be retained to ensure that the last known status is available
            // in case of a client shutdown. 
            bool retain = true; 

            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/connection";
                try
                {
                    await MqttServiceInstance.PublishAsync(topic, statusPayload, MqttQualityOfServiceLevel.AtLeastOnce, retain);
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "MQTT: Failed to publish status '{Status}' to topic '{Topic}'", PhotoboothIdentifier, statusPayload, topic);
                }
            }
            else
            {
                Logger?.Warning("MQTT: Not connected. Cannot publish status '{Status}'", PhotoboothIdentifier, statusPayload);
            }
        }

        public static async Task PublishPhotoBoothStatusJsonAsync()
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/status";
                try
                {
                    var statusObject = new
                    {
                        photoboothId = PhotoboothIdentifier,
                        state = State,
                        currentPage = CurrentPageName, // Include current page name
                        timestamp = DateTime.UtcNow.ToString("o"),
                        cameraConnected = false, // Placeholder, replace with actual camera status
                    };
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(statusObject);
                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MqttQualityOfServiceLevel.AtLeastOnce, retain: false);
                }
                catch (Exception ex)
                {
                    Logger?.Error($"MQTT: Failed to publish JSON status '{State}' to topic '{topic}'. Exception: {ex}");
                }
            }
            else
            {
                Logger?.Warning("MQTT: Not connected. Cannot publish JSON status '{State}'", PhotoboothIdentifier);
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
            string localAppDataPath;
            try
            {
                // This is the standard and preferred way
                localAppDataPath = ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception ex) // InvalidOperationException can occur if LocalFolder is not accessible (e.g., certain contexts for unpackaged apps very early)
            {
                App.Logger?.Error(ex, "ConfigureLogging: Could not access ApplicationData.Current.LocalFolder.Path. Falling back to AppContext.BaseDirectory for logs path.");
                // Fallback path, be mindful of write permissions if app is installed in Program Files
                localAppDataPath = AppContext.BaseDirectory;
            }

            string logsDirectory = Path.Combine(localAppDataPath, "Logs"); // New base path

            if (!Directory.Exists(logsDirectory)) Directory.CreateDirectory(logsDirectory);

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug() // Overall minimum level for logs processed by Serilog
                .Enrich.FromLogContext()
                .Enrich.WithProperty("PhotoboothID", PhotoboothIdentifier ?? "UnknownID_AtLoggingConfig") // Use PhotoboothIdentifier if available
                .Enrich.WithProperty("Application", "PhotoBoothApp");

            // --- File Sink with Compact JSON Format ---
            try
            {
                loggerConfiguration.WriteTo.File(
                    formatter: new RenderedCompactJsonFormatter(), // Use the compact JSON formatter
                    path: Path.Combine(logsDirectory, "photobooth-log-.ndjson"), // Suggest .ndjson or .json.log extension
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Failed to configure File sink: {ex.Message}");
            }

            // --- Debug Sink (remains the same) ---
            loggerConfiguration.WriteTo.Debug(); // Standard Visual Studio debug output (usually text-based)

            Logger = loggerConfiguration.CreateLogger();
            Log.Logger = Logger; // Assign to Serilog's global static logger
        }

        private async void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            Logger.Information("Main window closing. Disposing MQTT Service...", PhotoboothIdentifier);

            // --- Dispose Heartbeat Timer ---
            if (_heartbeatTimerMqtt != null)
            {
                try
                {
                    // Dispose the timer and wait for any queued callbacks to complete
                    // (or use Dispose(WaitHandle) for more control if needed, but simple Dispose is usually fine).
                    _heartbeatTimerMqtt.Dispose();
                    _heartbeatTimerMqtt = null; // Nullify to prevent reuse
                    Logger.Information("Heartbeat timer disposed successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Exception while disposing heartbeat timer.");
                }
            }
            // --- End of Heartbeat Timer Disposal ---

            // --- Dispose MQTT Service ---
            if (MqttServiceInstance != null)
            {
                MqttServiceInstance.ConnectionStatusChanged -= MqttService_ConnectionStatusChanged;
                MqttServiceInstance.SettingsUpdatedRemotely -= OnRemoteSettingsUpdated;
                await MqttServiceInstance.DisposeAsync();
                Logger.Information("MQTT Service disposed on window close for Photobooth ID: {PhotoboothId}.", PhotoboothIdentifier);
            }
            // --- End of MQTT Service Disposal ---

            // --- Flush logs and close application ---
            Logger.Information("Flushing logs and closing application.");
            await Log.CloseAndFlushAsync();


        }

        private async void OnRemoteSettingsUpdated(object sender, EventArgs e)
        {
            App.Logger?.Information("App: Event received - Remote settings have been updated. Attempting to reload and apply.");
            PhotoBoothSettings newSettings = null;
            PhotoBoothSettings oldEffectiveSettings = CurrentSettings; // Keep a reference to settings before reload

            try { newSettings = await SettingsManager.LoadSettingsAsync(); }
            catch (Exception ex) { App.Logger?.Error(ex, "App: Failed to reload settings after remote update. Aborting apply."); return; }

            if (newSettings != null)
            {
                CurrentSettings = newSettings; // Update App.CurrentSettings to the latest from file (which was just saved by MqttService)
                                               // ... (PhotoboothIdentifier update logic as before) ...
                App.Logger?.Information("App: Settings reloaded into App.CurrentSettings. Current Photobooth ID: {PhotoboothId}", PhotoboothIdentifier);


                // --- Handle Background Image Download ---
                bool backgroundChanged = false;
                string newLocalBackgroundImagePath = oldEffectiveSettings?.BackgroundImagePath ?? ""; // Start with old path

                if (!string.IsNullOrEmpty(CurrentSettings.RemoteBackgroundImageUrl) &&
                    (CurrentSettings.RemoteBackgroundImageUrl != CurrentSettings.LastSuccessfullyDownloadedImageUrl ||
                     (!string.IsNullOrEmpty(CurrentSettings.RemoteBackgroundImageHash) && CurrentSettings.RemoteBackgroundImageHash != CurrentSettings.LastSuccessfullyDownloadedImageHash) ||
                     string.IsNullOrEmpty(CurrentSettings.BackgroundImagePath) || // If no local path is set yet
                     !File.Exists(CurrentSettings.BackgroundImagePath)) // Or if current local file doesn't exist
                   )
                {
                    App.Logger?.Information("App: New or updated remote background image URL/hash detected, or local file missing. Attempting download.");
                    string downloadedPath = await DownloadAndSaveImageAsync(
                        CurrentSettings.RemoteBackgroundImageUrl,
                        CurrentSettings.RemoteBackgroundImageHash,
                        // Use hash of URL as part of filename seed if generating unique filenames, or just use fixed name
                        (CurrentSettings.RemoteBackgroundImageHash ?? Guid.NewGuid().ToString())
                    );

                    if (!string.IsNullOrEmpty(downloadedPath))
                    {
                        newLocalBackgroundImagePath = downloadedPath;
                        CurrentSettings.BackgroundImagePath = newLocalBackgroundImagePath; // Update the path for UI use
                        CurrentSettings.LastSuccessfullyDownloadedImageUrl = CurrentSettings.RemoteBackgroundImageUrl;
                        CurrentSettings.LastSuccessfullyDownloadedImageHash = CurrentSettings.RemoteBackgroundImageHash;
                        backgroundChanged = true;
                        App.Logger?.Information("App: Background image updated locally to: {Path}", newLocalBackgroundImagePath);
                    }
                    else
                    {
                        App.Logger?.Warning("App: Failed to download new remote background. Current background (if any) will be kept or cleared if path was invalid.");
                        // If download fails, decide behavior: keep old BackgroundImagePath or clear it?
                        // Let's assume if RemoteBackgroundImageUrl was set but download failed, we clear the path to avoid using stale image.
                        if (!string.IsNullOrEmpty(CurrentSettings.RemoteBackgroundImageUrl))
                        {
                            newLocalBackgroundImagePath = ""; // Clear path on failure if a remote URL was specified
                            CurrentSettings.BackgroundImagePath = newLocalBackgroundImagePath;
                            backgroundChanged = true; // Path changed to empty
                        }
                    }
                }
                else if (string.IsNullOrEmpty(CurrentSettings.RemoteBackgroundImageUrl) && !string.IsNullOrEmpty(CurrentSettings.BackgroundImagePath))
                {
                    // Remote URL was cleared, so local background should also be cleared
                    App.Logger?.Information("App: RemoteBackgroundImageUrl is empty. Clearing local background image path.");
                    newLocalBackgroundImagePath = "";
                    CurrentSettings.BackgroundImagePath = newLocalBackgroundImagePath;
                    CurrentSettings.LastSuccessfullyDownloadedImageUrl = ""; // Clear tracking too
                    CurrentSettings.LastSuccessfullyDownloadedImageHash = "";
                    backgroundChanged = true;
                }


                // If background or other critical settings changed, save CurrentSettings again
                // The SaveSettingsAsync in MqttService saved what was received from MQTT.
                // Now we might have updated it further (e.g., BackgroundImagePath, LastSuccessfullyDownloaded info).
                if (backgroundChanged) // Or any other property modified here after loading from remote
                {
                    App.Logger?.Information("App: Settings changed after remote update. Saving updated settings.");
                    await SettingsManager.SaveSettingsAsync(CurrentSettings, true); // 'true' as this is part of remote update flow, preserve LastModifiedUtc from MQTT push
                }

                // --- Dispatch UI updates to the UI thread ---
                if (MainWindow?.DispatcherQueue != null)
                {
                    MainWindow.DispatcherQueue.TryEnqueue(async () =>
                    {
                        App.Logger?.Debug("App: Now on UI thread. Attempting to refresh active page UI.");
                        try
                        {
                            if (MainWindow.Content is Frame rootFrame) // Ensure 'Frame' is Microsoft.UI.Xaml.Controls.Frame
                            {
                                if (rootFrame.Content is MainPage mainPageInstance)
                                {
                                    App.Logger?.Debug("App: MainPage is active. Requesting its UI to refresh from newly loaded App.CurrentSettings.");
                                    // These methods in MainPage.xaml.cs must be public
                                    mainPageInstance.LoadDynamicUITexts();
                                    await mainPageInstance.LoadBackgroundFromSettings();
                                    App.Logger?.Debug("App: MainPage UI refresh calls completed.");
                                }
                                else if (rootFrame.Content is PhotoBoothPage photoBoothPageInstance)
                                {
                                    App.Logger?.Debug("App: PhotoBoothPage is active. New settings loaded into App.CurrentSettings.");
                                    // PhotoBoothPage loads its texts/settings in its Page_Loaded or StartPhotoProcedure.
                                    // If it needs to react to live changes while already active (e.g., for button texts if review screen is shown),
                                    // it would need a public method like RefreshConfigurableTexts().
                                    // photoBoothPageInstance.RefreshConfigurableTexts(); // Example call
                                    // Its background is also loaded on Page_Loaded. A live background change would need similar handling.
                                    // await photoBoothPageInstance.LoadPageBackgroundAsync(); // If such a public method exists
                                }
                                // Add 'else if' for other pages if they need live UI updates from settings
                            }
                            else
                            {
                                App.Logger?.Warning("App: MainWindow.Content is not a Frame on UI thread. Cannot determine active page to refresh.");
                            }
                        }
                        catch (Exception uiEx)
                        {
                            App.Logger?.Error(uiEx, "App: Exception occurred during UI refresh on UI thread from OnRemoteSettingsUpdated.");
                        }
                    });
                }
                else
                {
                    App.Logger?.Error("App: MainWindow or its DispatcherQueue is null. Cannot dispatch UI updates for remote settings.");
                }
            }
            else
            {
                App.Logger?.Error("App: Reloaded settings (newSettings) are null after remote update notification. No changes applied to App.CurrentSettings.");
            }
        }
        private static async Task<string> DownloadAndSaveImageAsync(string imageUrl, string expectedHash, string localFileNameSeed)
        {

            if (string.IsNullOrEmpty(imageUrl))
            {
                App.Logger?.Information("DownloadAndSaveImageAsync: Image URL is empty, skipping download.");
                return null; // No URL, no local path
            }

            App.Logger?.Information("DownloadAndSaveImageAsync: Attempting to download image from {ImageUrl}", imageUrl);
            try
            {
                using (var httpClient = new HttpClient())
                {
                    byte[] imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
                    App.Logger?.Debug("DownloadAndSaveImageAsync: Downloaded {ByteCount} bytes.", imageBytes.Length);

                    if (imageBytes.Length == 0)
                    {
                        App.Logger?.Warning("DownloadAndSaveImageAsync: Downloaded image is empty for URL {ImageUrl}", imageUrl);
                        return null;
                    }

                    // Optional: Verify hash
                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        App.Logger?.Debug("DownloadAndSaveImageAsync: Hash is provided, so verifying hash for {ImageUrl}", imageUrl);

                        using (var sha256 = SHA256.Create())
                        {
                            byte[] computedHashBytes = sha256.ComputeHash(imageBytes);
                            string computedHash = BitConverter.ToString(computedHashBytes).Replace("-", "").ToLowerInvariant();
                            if (!computedHash.Equals(expectedHash.ToLowerInvariant(), StringComparison.Ordinal))
                            {
                                App.Logger?.Error("DownloadAndSaveImageAsync: Hash mismatch for {ImageUrl}. Expected: {Expected}, Computed: {Computed}", imageUrl, expectedHash, computedHash);
                                return null; // Hash mismatch, don't use the image
                            }
                            App.Logger?.Debug("DownloadAndSaveImageAsync: Image hash verified successfully for {ImageUrl}.", imageUrl);
                        }
                    }
                    else
                    {
                        App.Logger?.Debug("DownloadAndSaveImageAsync: No hash provided, skipping verification for {ImageUrl}", imageUrl);
                    }

                    StorageFolder localCacheFolder = ApplicationData.Current.LocalFolder;
                    StorageFolder backgroundsFolder = await localCacheFolder.CreateFolderAsync("Backgrounds", CreationCollisionOption.OpenIfExists);

                    // Create a somewhat unique local filename to avoid conflicts if URL changes often,
                    // or simply overwrite a fixed name like "current_background.jpg".
                    // Using a hash of the URL or the expected image hash in the filename can help with caching.
                    string localFileName = $"remote_bg_{localFileNameSeed.ReplaceNonAlphaNumericChars("_")}.jpg";
                    // Ensure localFileNameSeed is reasonably unique, e.g. hash of URL.
                    // For simplicity, let's use a fixed name for now and just replace.
                    localFileName = "current_remote_background.jpg";


                    StorageFile imageFile = await backgroundsFolder.CreateFileAsync(localFileName, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBytesAsync(imageFile, imageBytes);
                    App.Logger?.Debug("DownloadAndSaveImageAsync: Image saved locally to {LocalPath}", imageFile.Path);
                    return imageFile.Path; // Return the local path
                }
            }
            catch (HttpRequestException httpEx)
            {
                App.Logger?.Error(httpEx, "DownloadAndSaveImageAsync: HTTP request failed for {ImageUrl}.", imageUrl);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DownloadAndSaveImageAsync: Failed to download or save image from {ImageUrl}.", imageUrl);
            }
            return null; // Return null if download or save failed
        }

        private static async void App_OnSettingsWrittenToDisk_Handler(object sender, PhotoBoothSettings activeSettings)
        {
            if (activeSettings == null)
            {
                Logger?.Warning("App: OnSettingsWrittenToDisk triggered with null settings. Cannot report current state.");
                return;
            }

            Logger?.Information("App: Settings have been written to disk. Reporting current active settings state via MQTT. Timestamp: {Timestamp}", activeSettings.LastModifiedUtc);

            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/settings/current_state";

                try
                {
                    // Serialize the entire 'activeSettings' object.
                    // This object already includes PhotoboothId, LastModifiedUtc, and all other settings
                    // that are defined in your PhotoBoothSettings class.
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(activeSettings,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = true, // Optional: for better readability on MQTT if debugging
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                        });

                    // This message should NOT be retained. It's a point-in-time snapshot of the current state.
                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, false);
                    Logger?.Information("App: Successfully published current settings state (full object) to {Topic}", topic);
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "App: Failed to publish current settings state (full object) to MQTT topic {Topic}", topic);
                }
            }
            else
            {
                Logger?.Warning("App: Cannot publish current settings state. MQTT not connected, service not available, or PhotoboothIdentifier is missing.");
            }
        }

        // In App.xaml.cs (within the App class)
        private async static void LogHeartbeatMqtt(object state) // 'state' parameter is required by TimerCallback delegate, but we won't use it here
        {
            // Publish MQTT Heartbeat
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                string heartbeatTopic = $"photobooth/{PhotoboothIdentifier}/heartbeat";
                // "o" format is the round-trip DateTime pattern (ISO 8601, includes Z for UTC)
                string heartbeatPayload = DateTime.UtcNow.ToString("o");

                try
                {
                    App.Logger?.Verbose("MQTT HEARTBEAT: Attempting to publish to {Topic} with payload {Payload}", heartbeatTopic, heartbeatPayload);

                    await MqttServiceInstance.PublishAsync(
                        topic: heartbeatTopic,
                        payload: heartbeatPayload,
                        qos: MqttQualityOfServiceLevel.AtMostOnce, // QoS 0 is usually sufficient for heartbeats
                        retain: false);

                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "MQTT HEARTBEAT: Failed to publish heartbeat to {Topic}", heartbeatTopic);
                }
            }
            else
            {
                // This log might be frequent if MQTT is often disconnected. Consider its verbosity.
                App.Logger?.Debug("MQTT HEARTBEAT: Cannot publish. MQTT service not connected, instance not available, or PhotoboothIdentifier is missing.");
            }

        }

        private async static void LogHeartbeatLogging(object state) // 'state' parameter is required by TimerCallback delegate, but we won't use it here
        {
            // 1. Log in text file
            // Ensure Logger is initialized and we have the necessary info.
            // This check is important because the timer callback might fire
            // during app shutdown if not disposed properly, or very early if not careful.
            if (Logger != null && CurrentSettings != null && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                Logger.Information("HEARTBEAT: Application is active. Current Page: {CurrentPageName}, Photobooth ID: {PhotoboothID}",
                    CurrentPageName, // Static property holding the current page name
                    PhotoboothIdentifier); // Static property holding the Photobooth ID
            }
            else
            {
                // Fallback to Debug.WriteLine if full logging isn't ready or there's an issue.
                // This should be rare after proper startup.
                System.Diagnostics.Debug.WriteLine($"HEARTBEAT (Debug fallback): App active. Page: {CurrentPageName}. Logger/Settings/ID might not be fully initialized.");
            }
        }

        public enum PhotoBoothState
        {
            Starting,
            Idle,
            LoadinPhotoBoothPage,
            ResettingPhotoBoothPage,
            ShowingInstructions,
            Countdown,
            TakingPhoto,
            DownloadingPhotoFromCamera,
            RecordingVideo,
            ShowingSinglePhoto,
            ReviewingPhotos,
            Saving, 
            Processing,
            Uploading,
            Finished
        }

        public static PhotoBoothState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    string oldStateForLog = _state.ToString();
                    _state = value;
                    Logger?.Information("App: Photobooth Operational State changed from {OldState} to: {NewOperationalState}", oldStateForLog, _state.ToString());

                    // Automatically publish the full status when this state changes
                    // Fire-and-forget. cameraConnected is null here as it's a general state change. Retain is true.
                    _ = PublishPhotoBoothStatusJsonAsync();
                }
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