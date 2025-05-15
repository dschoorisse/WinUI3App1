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
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media.Imaging;


using MQTTnet.Protocol;
using System.Text.Json;
using Windows.Storage.Streams;
using Microsoft.UI.Dispatching;

namespace WinUI3App1
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static ILogger Logger { get; private set; }
        public static MqttService MqttServiceInstance { get; private set; }
        public static string CurrentPageName { get; private set; } = "Initializing";

        public static string PhotoboothIdentifier { get; private set; }
        public static BitmapImage PreloadedBackgroundImage { get; private set; }

        private static Timer _heartbeatTimerMqtt;
        private static readonly TimeSpan HeartbeatIntervalMqtt = TimeSpan.FromSeconds(10);
        private static Timer _heartbeatTimerLogging;
        private static readonly TimeSpan HeartbeatIntervalLogging = TimeSpan.FromSeconds(300);
        private static PhotoBoothState _state = PhotoBoothState.Idle;

        private static PhotoBoothSettings _currentSettings;
        public static PhotoBoothSettings CurrentSettings
        {
            get => _currentSettings;
            set
            {
                PhotoBoothSettings oldSettings = _currentSettings;
                _currentSettings = value;

                if (_currentSettings == null)
                {
                    Logger?.Warning("App.CurrentSettings was set to null. No further processing.");
                    PreloadedBackgroundImage = null;
                    NotifyActivePageToRefreshBackground();
                    return;
                }

                Logger?.Information("App.CurrentSettings has been updated. Old Path: '{OldPath}', New Path: '{NewPath}'",
                                    oldSettings?.BackgroundImagePath,
                                    _currentSettings.BackgroundImagePath);

                // Check if background image related settings have changed
                bool backgroundPotentiallyChanged = oldSettings == null ||
                                                    oldSettings.BackgroundImagePath != _currentSettings.BackgroundImagePath ||
                                                    oldSettings.RemoteBackgroundImageUrl != _currentSettings.RemoteBackgroundImageUrl ||
                                                    oldSettings.RemoteBackgroundImageHash != _currentSettings.RemoteBackgroundImageHash;

                if (backgroundPotentiallyChanged)
                {
                    Logger?.Debug("App.CurrentSettings: Background related settings changed or initial load. Triggering background processing.");
                    // Offload the initiation of preload and notification to ensure setter remains quick
                    // The PreloadBackgroundImageAsync itself will handle dispatching its core UI work.
                    _ = Task.Run(async () =>
                    {
                        // Note: The download logic (if remote URL is used) is handled in OnRemoteSettingsUpdated
                        // before this setter is called. This setter primarily reacts to the final BackgroundImagePath.
                        await PreloadBackgroundImageAsync(); // This method now internally dispatches to UI thread for UI work
                        NotifyActivePageToRefreshBackground(); // This method also dispatches to UI thread
                    });
                }
            }
        }

        public App()
        {
            this.InitializeComponent();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            PhotoBoothSettings initialSettings = null;
            try
            {
                initialSettings = await SettingsManager.LoadSettingsAsync();
                if (initialSettings == null)
                {
                    System.Diagnostics.Debug.WriteLine("CRITICAL: Failed to load settings, SettingsManager returned null. Using emergency defaults.");
                    initialSettings = new PhotoBoothSettings();
                    await SettingsManager.SaveSettingsAsync(initialSettings);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL: Exception loading settings: {ex.Message}. Using emergency defaults.");
                initialSettings = new PhotoBoothSettings();
            }

            // Assign to _currentSettings directly first to avoid setter logic during initial load here.
            _currentSettings = initialSettings;
            PhotoboothIdentifier = _currentSettings.PhotoboothId; // Initialize before logger

            if (string.IsNullOrWhiteSpace(PhotoboothIdentifier) ||
                PhotoboothIdentifier.Contains("/") || PhotoboothIdentifier.Contains("+") || PhotoboothIdentifier.Contains("#"))
            {
                string oldId = PhotoboothIdentifier;
                PhotoboothIdentifier = $"PhotoBooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}";
                System.Diagnostics.Debug.WriteLine($"WARN: Invalid PhotoboothId ('{oldId}') in settings, using default: {PhotoboothIdentifier}");
                _currentSettings.PhotoboothId = PhotoboothIdentifier; // Update the in-memory object
                await SettingsManager.SaveSettingsAsync(_currentSettings); // Save corrected ID
            }

            ConfigureLogging();
            Logger.Information("Application launching... Initial settings loaded. Photobooth ID: {PhotoboothId}", PhotoboothIdentifier);

            // Create MainWindow
            MainWindow = new MainWindow(); // MainWindow constructor navigates to initial page
            Logger.Debug("Main window created");

            // Activate the main window to ensure its content (and initial page) loads and DispatcherQueue is active
            MainWindow.Activate();
            Logger.Debug("Main window activated");

            // Now that MainWindow is created and activated, its DispatcherQueue is available.
            // Preload the background image. OnLaunched is on the UI thread.
            await PreloadBackgroundImageAsync();
            Logger.Debug("App.OnLaunched: After PreloadBackgroundImageAsync, App.PreloadedBackgroundImage is {Status}", App.PreloadedBackgroundImage == null ? "NULL" : "SET");

            // Explicitly notify the now-active page to refresh its background with the preloaded image.
            // This ensures that if the page loaded before PreloadBackgroundImageAsync completed, it still gets updated.
            NotifyActivePageToRefreshBackground();


            // Fullscreen Logic
            const string targetPhotoboothComputerName = "DESKTOP-NJDEOAK"; // TODO: Make this configurable
            string currentComputerName = Environment.MachineName;
            Logger.Debug("Current computer name: {ComputerName}. Target for fullscreen: {TargetComputerName}", currentComputerName, targetPhotoboothComputerName);

            if (MainWindow.Content is Frame appFrame)
            {
                // This might be slightly early if navigation hasn't fully completed,
                // but CurrentPageName is mostly for logging/status.
                // The RootFrame_Navigated handler will update it more reliably.
                if (appFrame.Content is Page initialPage) CurrentPageName = initialPage.GetType().Name;
                else CurrentPageName = appFrame.SourcePageType?.Name ?? "MainPage";
                Logger.Debug("App: Initial page (potentially) detected: {CurrentPageName}", CurrentPageName);
                appFrame.Navigated += RootFrame_Navigated;
            }
            else Logger.Error("App: Could not find AppFrame in MainWindow to subscribe to navigation events.");


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
                        else Logger.Information("Window is already in Fullscreen or a non-Overlapped mode.");
                    }
                    else Logger.Error("Could not retrieve AppWindow. Fullscreen mode cannot be set.");
                }
                catch (Exception ex) { Logger.Error(ex, "An error occurred while trying to set fullscreen mode."); }
            }
            else Logger.Information("Computer name does not match. Application will start in default windowed mode.");

            // Initialize MQTT Service
            string mqttBroker = _currentSettings.MqttBrokerAddress;
            int mqttPort = _currentSettings.MqttBrokerPort;
            string mqttUser = _currentSettings.MqttUsername;
            string mqttPassword = _currentSettings.MqttPassword;
            try
            {
                if (!string.IsNullOrEmpty(mqttBroker) && mqttPort > 0)
                {
                    MqttServiceInstance = new MqttService(Logger, PhotoboothIdentifier, mqttBroker, mqttPort, mqttUser, mqttPassword);
                    MqttServiceInstance.ConnectionStatusChanged += MqttService_ConnectionStatusChanged;
                    MqttServiceInstance.SettingsUpdatedRemotely += OnRemoteSettingsUpdated;
                    Logger.Debug("MQTT Service instance created for Photobooth ID {PhotoboothId}.", PhotoboothIdentifier);
                    _ = MqttServiceInstance.StartAsync(); // Fire and forget, StartAsync handles retries
                }
                else { Logger.Error("MQTT configuration missing in settings. MQTT Service not started."); }
            }
            catch (Exception ex) { Logger.Error(ex, "Failed to initialize MQTT Service."); }


            State = PhotoBoothState.Starting; // Initial state
            MainWindow.Closed += OnMainWindowClosed;
            this.UnhandledException += App_UnhandledException;
            SettingsManager.OnSettingsWrittenToDisk += App_OnSettingsWrittenToDisk_Handler;

            // Initialize heartbeat timers
            _heartbeatTimerMqtt = new Timer(LogHeartbeatMqtt, null, TimeSpan.Zero, HeartbeatIntervalMqtt);
            _heartbeatTimerLogging = new Timer(LogHeartbeatLogging, null, TimeSpan.Zero, HeartbeatIntervalLogging);
            Logger.Verbose("Application Heartbeat timers started.");

            Logger.Information("Application initialization complete.");
        }

        private static async void RootFrame_Navigated(object sender, NavigationEventArgs e)
        {
            string newPageName = (e.Content as Page)?.GetType().Name ?? e.SourcePageType?.Name ?? "Unknown";
            if (CurrentPageName != newPageName)
            {
                string previousPageName = CurrentPageName;
                CurrentPageName = newPageName;
                Logger?.Information("App: Navigated from {PreviousPageName} to {CurrentPageName}", previousPageName, CurrentPageName);

                // When navigation completes, if it's the initial page load, ensure background is applied.
                // This helps if PreloadBackgroundImageAsync finishes before the page's own Loaded event.
                // We can check if PreloadedBackgroundImage is set and tell the new page to use it.
                // The page's LoadPageBackgroundAsync or RefreshBackgroundDisplay should handle this.
                if (e.NavigationMode == NavigationMode.New) // Or check specific page types
                {
                    NotifyActivePageToRefreshBackground();
                }

                if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
                {
                    await PublishPhotoBoothStatusJsonAsync();
                }
            }
        }

        private async void MqttService_ConnectionStatusChanged(object sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Information("MQTT: Connected. Publishing initial 'Online' status for {PhotoboothId}.", PhotoboothIdentifier);
                await PublishConnectionStatusAsync("online");
                await PublishCurrentSettingsToMqttAsync(CurrentSettings); // Publish current state after connecting
            }
            else Logger.Information("MQTT: Disconnected for {PhotoboothId}.", PhotoboothIdentifier);
        }

        public static async Task PublishConnectionStatusAsync(string statusPayload)
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/connection";
                try
                {
                    await MqttServiceInstance.PublishAsync(topic, statusPayload, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, true);
                }
                catch (Exception ex) { Logger?.Error(ex, "MQTT: Failed to publish status '{Status}' to topic '{Topic}' for {PhotoboothId}", statusPayload, topic, PhotoboothIdentifier); }
            }
            else Logger?.Warning("MQTT: Not connected. Cannot publish status '{Status}' for {PhotoboothId}", statusPayload, PhotoboothIdentifier);
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
                        state = State.ToString(), // Send enum as string
                        currentPage = CurrentPageName,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        cameraConnected = false, // Placeholder
                    };
                    var serializerOptions = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                    string jsonPayload = JsonSerializer.Serialize(statusObject, serializerOptions);
                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, false);
                }
                catch (Exception ex) { Logger?.Error(ex, "MQTT: Failed to publish JSON status '{State}' to topic '{Topic}' for {PhotoboothId}", State, topic, PhotoboothIdentifier); }
            }
            else Logger?.Warning("MQTT: Not connected. Cannot publish JSON status '{State}' for {PhotoboothId}", State, PhotoboothIdentifier);
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger?.Fatal(e.Exception, "Unhandled application exception for {PhotoboothId}. Attempting to handle.", PhotoboothIdentifier);
            e.Handled = true; // Prevent app crash
        }

        private void ConfigureLogging()
        {
            string localAppDataPath;
            try { localAppDataPath = ApplicationData.Current.LocalFolder.Path; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting LocalFolder path for logs: {ex.Message}. Falling back to BaseDirectory.");
                localAppDataPath = AppContext.BaseDirectory;
            }
            string logsDirectory = Path.Combine(localAppDataPath, "Logs");
            if (!Directory.Exists(logsDirectory)) Directory.CreateDirectory(logsDirectory);

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("PhotoboothID", PhotoboothIdentifier ?? "UnknownID_AtLogConfig")
                .Enrich.WithProperty("Application", "PhotoBoothApp")
                .WriteTo.File(
                    formatter: new Serilog.Formatting.Compact.RenderedCompactJsonFormatter(),
                    path: Path.Combine(logsDirectory, "photobooth-log-.ndjson"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);

            // Optionally add Debug sink if needed during development
            // #if DEBUG
            // loggerConfiguration.WriteTo.Debug();
            // #endif

            Logger = loggerConfiguration.CreateLogger();
            Log.Logger = Logger; // Assign to Serilog's global static logger
        }

        private async void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            Logger?.Information("Main window closing for {PhotoboothId}. Disposing resources...", PhotoboothIdentifier);
            _heartbeatTimerMqtt?.Dispose();
            _heartbeatTimerLogging?.Dispose();
            if (MqttServiceInstance != null)
            {
                MqttServiceInstance.ConnectionStatusChanged -= MqttService_ConnectionStatusChanged;
                MqttServiceInstance.SettingsUpdatedRemotely -= OnRemoteSettingsUpdated;
                await MqttServiceInstance.DisposeAsync();
                Logger?.Information("MQTT Service disposed for {PhotoboothId}.", PhotoboothIdentifier);
            }
            SettingsManager.OnSettingsWrittenToDisk -= App_OnSettingsWrittenToDisk_Handler;
            Logger?.Information("Flushing logs and closing application for {PhotoboothId}.", PhotoboothIdentifier);
            await Log.CloseAndFlushAsync();
        }

        private async void OnRemoteSettingsUpdated(object sender, EventArgs e)
        {
            App.Logger?.Information("App: Event received - Remote settings have been updated. Attempting to reload and apply.");
            PhotoBoothSettings newSettingsFromFile = null;
            try { newSettingsFromFile = await SettingsManager.LoadSettingsAsync(); } // Loads what MqttService saved
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "App: Failed to reload settings after remote update. Aborting apply.");
                return;
            }

            if (newSettingsFromFile == null)
            {
                App.Logger?.Error("App: Reloaded settings (newSettingsFromFile) are null after remote update. No changes applied.");
                return;
            }

            PhotoBoothSettings effectiveSettings = newSettingsFromFile;
            bool settingsModifiedByDownload = false;

            // --- Handle Background Image Download if URL/Hash changed or local file is missing ---
            bool needsDownload = !string.IsNullOrEmpty(effectiveSettings.RemoteBackgroundImageUrl) &&
                                 (effectiveSettings.RemoteBackgroundImageUrl != effectiveSettings.LastSuccessfullyDownloadedImageUrl ||
                                  (!string.IsNullOrEmpty(effectiveSettings.RemoteBackgroundImageHash) && effectiveSettings.RemoteBackgroundImageHash != effectiveSettings.LastSuccessfullyDownloadedImageHash) ||
                                  string.IsNullOrEmpty(effectiveSettings.BackgroundImagePath) ||
                                  !File.Exists(effectiveSettings.BackgroundImagePath));

            if (needsDownload)
            {
                App.Logger?.Information("App (OnRemoteSettingsUpdated): New/updated remote background or missing local. Attempting download.");
                string downloadedPath = await DownloadAndSaveImageAsync(
                    effectiveSettings.RemoteBackgroundImageUrl,
                    effectiveSettings.RemoteBackgroundImageHash,
                    (effectiveSettings.RemoteBackgroundImageHash ?? Guid.NewGuid().ToString().Substring(0, 8))
                );

                if (!string.IsNullOrEmpty(downloadedPath))
                {
                    effectiveSettings.BackgroundImagePath = downloadedPath;
                    effectiveSettings.LastSuccessfullyDownloadedImageUrl = effectiveSettings.RemoteBackgroundImageUrl;
                    // Assuming DownloadAndSaveImageAsync verifies hash or you trust the source
                    effectiveSettings.LastSuccessfullyDownloadedImageHash = effectiveSettings.RemoteBackgroundImageHash;
                    settingsModifiedByDownload = true;
                    App.Logger?.Information("App (OnRemoteSettingsUpdated): Background image downloaded/updated to: {Path}", downloadedPath);
                }
                else
                {
                    App.Logger?.Warning("App (OnRemoteSettingsUpdated): Failed to download new remote background.");
                    // Decide if BackgroundImagePath should be cleared or retain old value
                    // effectiveSettings.BackgroundImagePath = ""; // Optional: clear path on failure
                    // settingsModifiedByDownload = true;
                }
            }
            else if (string.IsNullOrEmpty(effectiveSettings.RemoteBackgroundImageUrl) && !string.IsNullOrEmpty(effectiveSettings.BackgroundImagePath))
            {
                // If remote URL is cleared, clear local path if it was likely from a remote source.
                App.Logger?.Information("App (OnRemoteSettingsUpdated): RemoteBackgroundImageUrl is empty. Clearing local background image path.");
                effectiveSettings.BackgroundImagePath = "";
                effectiveSettings.LastSuccessfullyDownloadedImageUrl = "";
                effectiveSettings.LastSuccessfullyDownloadedImageHash = "";
                settingsModifiedByDownload = true;
            }

            if (settingsModifiedByDownload)
            {
                App.Logger?.Information("App (OnRemoteSettingsUpdated): Settings modified by background download. Saving.");
                await SettingsManager.SaveSettingsAsync(effectiveSettings, true); // Preserve remote LastModifiedUtc
            }

            // Assign to App.CurrentSettings. The setter will handle preloading if BackgroundImagePath changed.
            CurrentSettings = effectiveSettings;
            // UI text updates, etc., are handled by pages on load/navigation.
            // The setter for CurrentSettings now triggers background refresh if needed.
        }

        private static async Task<string> DownloadAndSaveImageAsync(string imageUrl, string expectedHash, string localFileNameSeed)
        {
            if (string.IsNullOrEmpty(imageUrl))
            {
                App.Logger?.Information("DownloadAndSaveImageAsync: Image URL is empty, skipping download.");
                return null;
            }
            App.Logger?.Information("DownloadAndSaveImageAsync: Attempting to download image from {ImageUrl}", imageUrl);
            try
            {
                using (var httpClient = new HttpClient())
                {
                    byte[] imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
                    if (imageBytes.Length == 0)
                    {
                        App.Logger?.Warning("DownloadAndSaveImageAsync: Downloaded image is empty for URL {ImageUrl}", imageUrl);
                        return null;
                    }

                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        using (var sha256 = SHA256.Create())
                        {
                            byte[] computedHashBytes = sha256.ComputeHash(imageBytes);
                            string computedHash = BitConverter.ToString(computedHashBytes).Replace("-", "").ToLowerInvariant();
                            if (!computedHash.Equals(expectedHash.ToLowerInvariant(), StringComparison.Ordinal))
                            {
                                App.Logger?.Error("DownloadAndSaveImageAsync: Hash mismatch for {ImageUrl}. Expected: {Expected}, Computed: {Computed}", imageUrl, expectedHash, computedHash);
                                return null;
                            }
                            App.Logger?.Debug("DownloadAndSaveImageAsync: Image hash verified for {ImageUrl}.", imageUrl);
                        }
                    }

                    StorageFolder localCacheFolder = ApplicationData.Current.LocalFolder;
                    StorageFolder backgroundsFolder = await localCacheFolder.CreateFolderAsync("Backgrounds", CreationCollisionOption.OpenIfExists);
                    string localFileName = $"remote_bg_{localFileNameSeed.ReplaceNonAlphaNumericChars("_")}.jpg"; // Or a fixed name
                    localFileName = "current_remote_background.jpg"; // Using a fixed name for simplicity

                    StorageFile imageFile = await backgroundsFolder.CreateFileAsync(localFileName, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBytesAsync(imageFile, imageBytes);
                    App.Logger?.Information("DownloadAndSaveImageAsync: Image saved locally to {LocalPath}", imageFile.Path);
                    return imageFile.Path;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DownloadAndSaveImageAsync: Failed to download or save image from {ImageUrl}.", imageUrl);
                return null;
            }
        }

        private static async void App_OnSettingsWrittenToDisk_Handler(object sender, PhotoBoothSettings activeSettings)
        {
            if (activeSettings == null)
            {
                Logger?.Warning("App: OnSettingsWrittenToDisk triggered with null settings. Cannot report current state.");
                return;
            }
            await PublishCurrentSettingsToMqttAsync(activeSettings);
        }

        private static async Task PublishCurrentSettingsToMqttAsync(PhotoBoothSettings settingsToPublish)
        {
            if (settingsToPublish == null)
            {
                Logger?.Warning("App: PublishCurrentSettingsToMqttAsync called with null settings. Aborting.");
                return;
            }
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                string topic = $"photobooth/{settingsToPublish.PhotoboothId ?? PhotoboothIdentifier}/settings/current_state";
                Logger?.Information("App: Publishing current settings to MQTT. Topic: {Topic}, Timestamp: {Timestamp}", topic, settingsToPublish.LastModifiedUtc);
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                    string jsonPayload = JsonSerializer.Serialize(settingsToPublish, options);
                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, true); // Retain current settings
                }
                catch (Exception ex) { Logger?.Error(ex, "App: Failed to publish current settings to MQTT topic {Topic}", topic); }
            }
            else Logger?.Warning("App: Cannot publish current settings. MQTT not connected/available or PhotoboothIdentifier missing. Settings Timestamp: {Timestamp}", settingsToPublish.LastModifiedUtc);
        }

        private static void NotifyActivePageToRefreshBackground()
        {
            if (MainWindow?.DispatcherQueue != null && MainWindow.DispatcherQueue.HasThreadAccess)
            {
                // Already on UI thread, call directly
                RefreshActivePageBackgroundInternal();
            }
            else if (MainWindow?.DispatcherQueue != null)
            {
                // Not on UI thread, dispatch
                bool Succeeded = MainWindow.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    RefreshActivePageBackgroundInternal();
                });
                if (!Succeeded) Logger?.Error("App: Failed to enqueue background refresh notification.");
            }
            else Logger?.Warning("App: MainWindow or its DispatcherQueue is null. Cannot dispatch background refresh notification.");
        }

        private static void RefreshActivePageBackgroundInternal()
        {
            App.Logger?.Debug("App: RefreshActivePageBackgroundInternal - Notifying active page to refresh background display.");
            if (MainWindow.Content is Frame rootFrame)
            {
                if (rootFrame.Content is MainPage mainPageInstance)
                {
                    Logger?.Debug("App: Active page is MainPage. Calling RefreshBackgroundDisplay.");
                    mainPageInstance.RefreshBackgroundDisplay();
                }
                else if (rootFrame.Content is PhotoBoothPage photoBoothPageInstance)
                {
                    Logger?.Debug("App: Active page is PhotoBoothPage. Calling RefreshBackgroundDisplay.");
                    photoBoothPageInstance.RefreshBackgroundDisplay();
                }
                else if (rootFrame.Content is Page genericPage) // Fallback for any other page type
                {
                    Logger?.Warning("App: Active page is {PageType}, which may not have RefreshBackgroundDisplay. Attempting dynamic call if method exists.", genericPage.GetType().Name);
                    // You could use reflection here if absolutely necessary, but it's brittle.
                    // It's better if pages that need this implement a common interface or base class method.
                    // For now, we'll assume MainPage and PhotoBoothPage are the primary targets.
                }
                else
                {
                    Logger?.Warning("App: rootFrame.Content is not a Page. Cannot refresh background.");
                }
            }
            else Logger?.Warning("App: MainWindow.Content is not a Frame on UI thread. Cannot determine active page to refresh background.");
        }

        private static async Task PreloadBackgroundImageAsync()
        {
            Logger?.Debug("App Preloader: Attempting to preload background image.");
            string imagePath = CurrentSettings?.BackgroundImagePath; // Capture for UI thread access

            if (MainWindow?.DispatcherQueue == null)
            {
                Logger?.Error("App Preloader: MainWindow or DispatcherQueue not available. Cannot preload image on UI thread.");
                PreloadedBackgroundImage = null; // Ensure it's cleared if we can't proceed
                return;
            }

            // Create a TaskCompletionSource to await the result of the dispatched operation
            var tcs = new TaskCompletionSource<object>(); // Using object as we just care about completion or exception

            // Dispatch the image loading logic to the UI thread using TryEnqueue
            bool enqueued = MainWindow.DispatcherQueue.TryEnqueue(async () => // This lambda becomes async void
            {
                try
                {
                    if (string.IsNullOrEmpty(imagePath))
                    {
                        Logger?.Information("App Preloader (UI Thread): No background image path set. Clearing preloaded image.");
                        PreloadedBackgroundImage = null;
                        tcs.TrySetResult(null); // Signal completion
                        return;
                    }

                    if (!File.Exists(imagePath))
                    {
                        Logger?.Warning("App Preloader (UI Thread): Background image file not found at {Path}. Clearing preloaded image.", imagePath);
                        PreloadedBackgroundImage = null;
                        tcs.TrySetResult(null); // Signal completion
                        return;
                    }

                    // Inner try-catch for the actual image loading operations
                    try
                    {
                        Logger?.Information("App Preloader (UI Thread): Preloading background image from {Path}", imagePath);
                        BitmapImage bitmap = new BitmapImage(); // Created on UI thread

                        // Use Windows.Storage.Streams.FileRandomAccessStream for BitmapImage
                        using (IRandomAccessStream fileStream = await FileRandomAccessStream.OpenAsync(imagePath, FileAccessMode.Read))
                        {
                            bitmap.DecodePixelWidth = 1920; // Example: decode to a max width of 1920px
                            await bitmap.SetSourceAsync(fileStream); // Used on UI thread
                        }
                        PreloadedBackgroundImage = bitmap; // Assigned on UI thread
                        Logger?.Information("App Preloader (UI Thread): Background image preloaded successfully.");
                        tcs.TrySetResult(null); // Signal successful completion
                    }
                    catch (Exception exInner) // Catch exceptions during image loading (e.g., invalid image format)
                    {
                        Logger?.Error(exInner, "App Preloader (UI Thread): Failed to load background image from {Path}.", imagePath);
                        PreloadedBackgroundImage = null;
                        tcs.TrySetException(exInner); // Signal completion with an error
                    }
                }
                catch (Exception exOuter) // Catch any other unexpected exception from the dispatched lambda
                {
                    Logger?.Error(exOuter, "App Preloader (UI Thread): Outer exception in dispatched lambda for background loading.");
                    PreloadedBackgroundImage = null;
                    tcs.TrySetException(exOuter); // Signal completion with an error
                }
            });

            if (enqueued)
            {
                await tcs.Task; // Wait for the enqueued work (and its TaskCompletionSource) to complete
            }
            else
            {
                Logger?.Error("App Preloader: Failed to enqueue background image loading operation to UI thread.");
                PreloadedBackgroundImage = null;
                // Optionally, throw an exception or handle this scenario as a failure
                // For example: throw new InvalidOperationException("Failed to enqueue background image load.");
            }
        }

        private static void LogHeartbeatMqtt(object state)
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/heartbeat";
                string payload = DateTime.UtcNow.ToString("o");
                try
                {
                    // Fire-and-forget is acceptable for heartbeats if not critical for them to always succeed
                    _ = MqttServiceInstance.PublishAsync(topic, payload, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce, false);
                    Logger?.Verbose("MQTT HEARTBEAT: Published to {Topic}", topic);
                }
                catch (Exception ex) { Logger?.Error(ex, "MQTT HEARTBEAT: Failed to publish to {Topic}", topic); }
            }
            else Logger?.Debug("MQTT HEARTBEAT: Cannot publish. MQTT not connected/available or PhotoboothIdentifier missing.");
        }

        private static void LogHeartbeatLogging(object state)
        {
            if (Logger != null && CurrentSettings != null && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                Logger.Information("HEARTBEAT: App active. Page: {CurrentPageName}, ID: {PhotoboothID}", CurrentPageName, PhotoboothIdentifier);
            }
            else System.Diagnostics.Debug.WriteLine($"HEARTBEAT (Debug fallback): App active. Page: {CurrentPageName}. Logger/Settings/ID might not be fully initialized.");
        }

        public enum PhotoBoothState { Starting, LoadingMainPage, Idle, LoadingPhotoBoothPage, ResettingPhotoBoothPage, ShowingInstructions, Countdown, TakingPhoto, DownloadingPhotoFromCamera, RecordingVideo, ShowingSinglePhoto, ReviewingPhotos, ReviewingPhotosTimedOut, Saving, Processing, Uploading, Finished }
        public static PhotoBoothState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    string oldStateForLog = _state.ToString();
                    _state = value;
                    Logger?.Information("App: State changed from {OldState} to: {NewState} for {PhotoboothId}", oldStateForLog, _state.ToString(), PhotoboothIdentifier);
                    _ = PublishPhotoBoothStatusJsonAsync();
                }
            }
        }
        public static void UpdateAppSettings(PhotoBoothSettings newSettings)
        {
            if (newSettings != null) CurrentSettings = newSettings; // Uses the property setter
            else Logger?.Warning("App: UpdateAppSettings called with null settings. No update performed.");
        }
    }
}
