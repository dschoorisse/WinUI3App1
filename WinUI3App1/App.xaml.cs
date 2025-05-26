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
using Microsoft.UI.Dispatching;

// Ensure this namespace matches your project, e.g., WinUI3App1
namespace WinUI3App1
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static ILogger Logger { get; private set; }
        public static MqttService MqttServiceInstance { get; private set; }
        public static string CurrentPageName { get; private set; } = "Initializing"; // Holds current page name

        // Settings management
        public static string PhotoboothIdentifier { get; private set; }
        public static PhotoBoothSettings CurrentSettings { get; set; } // Expose loaded settings

        // Heartbeat timer MQTT
        private static Timer _heartbeatTimerMqtt; 
        private static readonly TimeSpan HeartbeatIntervalMqtt = TimeSpan.FromSeconds(10); // Configurable interval

        // Heartbeat timer text logging
        private static Timer _heartbeatTimerLogging;
        private static readonly TimeSpan HeartbeatIntervalLogging = TimeSpan.FromSeconds(300); // Configurable interval

        // Global state
        private static PhotoBoothState _state = PhotoBoothState.Idle; // will be used to track the current state of the app, updates will automatically trigger MQTT status updates

        // Background image preloading
        public static DateTime lastPreloadBackgroundUtc { get; set; }
        public static BitmapImage PreloadedBackgroundImage { get; private set; }

        // For DNP Status Service
        public static DnpStatusService DnpStatusMonitor { get; private set; }
        public static PrinterStatusEventArgs LastKnownPrinterStatus { get; private set; }  // Om de laatste status vast te houden
        private static System.Diagnostics.Stopwatch _printingFinishedStopwatch = new System.Diagnostics.Stopwatch(); // Moet static zijn als methode static is
        private static string _previousPrinterStatusForLight; // Moet static zijn
        public static DateTime lastPrintTime;

        // Printer light control
        private static System.Diagnostics.Stopwatch _lightStandbyDelayStopwatch = new System.Diagnostics.Stopwatch();
        private static string _previousLightControlStatus; // To track changes for light control logic
        private static string _currentLightCommandSent; // To track the last command sent to the light
        private const string PRINTER_LIGHT_MQTT_TOPIC = "printer-light/command"; // TODO: create configuration item
        private static DispatcherQueueTimer _printerLightFinishedToStandbyTimer; // TODO: create configuration item
        private const string LIGHT_CMD_STANDBY = "STANDBY";
        private const string LIGHT_CMD_PRINTING = "PRINTING";
        private const string LIGHT_CMD_FINISHED = "FINISHED";

        public App()
        {
            this.InitializeComponent();

            // Settings-dependent initializations are moved to OnLaunched after settings are loaded
            // to allow for async loading of settings.
        }

        /// Application entry point
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            //LastSettingsChange = DateTime.Now;

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

            // 4. Preload background image if specified
            await PreloadBackgroundImageAsync();


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
                    MqttServiceInstance.ApplyRemoteSettingsAsync += ProcessAndApplyRemoteSettingsAsync; 
                    Logger.Debug("MQTT Service instance created for Photobooth ID {PhotoboothId}.", PhotoboothIdentifier);
                }
                else { Logger.Error("MQTT configuration missing in settings. MQTT Service not started."); }
            }
            catch (Exception ex) { Logger.Error(ex, "Failed to initialize MQTT Service."); }

            // 5. Main window creation and Fullscreen Logic ---
            MainWindow = new MainWindow();
            Logger.Debug("Main window created");

            


            // Initialisation of DNP Status Service
            InitializeAndStartDnpStatusMonitoring();

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
                // Fallback if Content is not yet a Page (less likely here)
                else
                {
                    CurrentPageName = mwInstance.AppFrame.SourcePageType?.Name ?? "MainPage";
                }
                Logger.Debug("App: Initial page detected: {CurrentPageName}", CurrentPageName);

                // Subscribe for subsequent navigations
                mwInstance.AppFrame.Navigated += RootFrame_Navigated;
            }
            else 
            { 
                Logger.Error("App: Could not find AppFrame in MainWindow to subscribe to navigation events."); 
            }

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


            // 9. Set initial light states (LEDs)
            SetInitialPrinterLightState();

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

        // Initialize and start the DNP Status Monitoring service
        private void InitializeAndStartDnpStatusMonitoring()
        {
            // ---- HIER DE TIMER INITIALISEREN ----
            if (MainWindow?.DispatcherQueue != null)
            {
                _printerLightFinishedToStandbyTimer = MainWindow.DispatcherQueue.CreateTimer();
                _printerLightFinishedToStandbyTimer.Tick += PrinterLightFinishedToStandbyTimer_Tick;
                // Interval wordt later gezet wanneer de timer gestart wordt.
                Logger.Debug("App: _printerLightFinishedToStandbyTimer initialized.");
            }
            else
            {
                // Dit is een kritieke fout als de timer nodig is.
                Logger.Error("App: MainWindow.DispatcherQueue not available at timer initialization point. _printerLightFinishedToStandbyTimer will be null.");
            }
            // ------------------------------------

            // Called in OnLaunched AFTER MainWindow, CurrentSettings and Logger are initialized.
            if (CurrentSettings == null) // Logger wordt gecheckt in de DnpStatusService constructor
            {
                System.Diagnostics.Debug.WriteLine("App: Cannot initialize DnpStatusService, CurrentSettings is not ready.");
                Logger?.Error("App: Cannot initialize DnpStatusService, CurrentSettings is not ready.");
                // Set a default status so LastKnownPrinterStatus is not null
                LastKnownPrinterStatus = new PrinterStatusEventArgs("Settings Missing", isJsonAccessible: false, isConnected: false, isHotFolderActive: false);
                return;
            }
            if (MainWindow?.DispatcherQueue == null)
            {
                System.Diagnostics.Debug.WriteLine("App: Cannot initialize DnpStatusService, MainWindow.DispatcherQueue is not ready.");
                Logger?.Error("App: Cannot initialize DnpStatusService, MainWindow.DispatcherQueue is not ready.");
                LastKnownPrinterStatus = new PrinterStatusEventArgs("Dispatcher Missing", isJsonAccessible: false, isConnected: false, isHotFolderActive: false);
                return;
            }

            try
            {
                Logger?.Debug("App: Initializing DnpStatusService...");
                // Pass the main window's dispatcher queue to the service
                DnpStatusMonitor = new DnpStatusService(CurrentSettings, Logger, MainWindow.DispatcherQueue);
                DnpStatusMonitor.PrinterStatusUpdated += OnPrinterStatusUpdated; // Abonneer op het event

                // Start monitoring only if a DNP status file path is actually configured
                if (!string.IsNullOrEmpty(CurrentSettings.DnpPrinterStatusFilePath))
                {
                    DnpStatusMonitor.StartMonitoring();
                    Logger?.Information("App: DNP Status Monitoring service started for file: {FilePath}", CurrentSettings.DnpPrinterStatusFilePath);
                }
                else
                {
                    Logger?.Warning("App: DNP Printer Status File Path is not configured in settings. DNP Status Monitoring will not start automatically.");
                }
                // Set initial status based on what DnpStatusService constructor might have set
                LastKnownPrinterStatus = DnpStatusMonitor.CurrentPrinterStatus ?? new PrinterStatusEventArgs(status: "Initializing", isJsonAccessible: !string.IsNullOrEmpty(CurrentSettings.DnpPrinterStatusFilePath));

            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "App: Failed to initialize or start DnpStatusService.");
                LastKnownPrinterStatus = new PrinterStatusEventArgs("Init Exception", isJsonAccessible: false, isConnected: false, isHotFolderActive: false);
            }
        }

        // Event handler for printer status updates
        private static async void OnPrinterStatusUpdated(object sender, PrinterStatusEventArgs e)
        {
            LastKnownPrinterStatus = e; // Update de static property
            Logger?.Information("App: Printer status updated via DnpStatusService event - Status: {Status}, Connected: {IsConnected}, HotFolderActive: {IsHotFolderActive}, JSONOK: {IsJsonOK}, Remaining: {Remaining}",
                e.Status, e.IsPrinterLikelyConnected, e.IsHotFolderUtilityActive, e.IsJsonFileAccessible, e.RemainingMedia);

            // 1. Stuur de status naar MQTT (als MQTT service bestaat en verbonden is)
            await PublishPrinterStatusToMqttAsync(e);

            // 2. Stuur de verlichting aan (jouw logica hier)
            await ControlPrinterLightBasedOnStatus(e);
        }

        private static async Task PublishPrinterStatusToMqttAsync()
        {
            await PublishPrinterStatusToMqttAsync(LastKnownPrinterStatus); // Gebruik de laatste bekende status
        }

        // NIEUWE METHODE om printer status naar MQTT te sturen
        private static async Task PublishPrinterStatusToMqttAsync(PrinterStatusEventArgs status)
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                string topic = $"photobooth/{PhotoboothIdentifier}/printer/status";
                try
                {
                    // Maak een anoniem object of een DTO voor de JSON payload
                    var payloadObject = new
                    {
                        timestamp = DateTime.UtcNow.ToString("o"), // ISO 8601
                        status.Status, // printer status string
                        status.IsPrinterLikelyConnected,
                        status.IsHotFolderUtilityActive,
                        status.IsJsonFileAccessible,
                        status.RemainingMedia,
                        status.HeadTempCelsius,
                        status.HumidityPercentage,
                        status.LifeCounter,
                        status.SerialNumber,
                        status.RawStatus,
                        jsonFileTimestamp = status.JsonFileTimestamp?.ToString("o"), // ISO 8601, nullable
                        lastStatusChange = status.LastStatusChangeTime?.ToString("o") // ISO 8601, nullable
                    };
                    // Gebruik System.Text.Json voor serialisatie
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payloadObject,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = false, // Compact voor MQTT
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull // Optioneel: nul-waarden niet meesturen
                        });

                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, retain: true); // Retain last status
                    App.Logger?.Debug("App: Printer status published to MQTT topic {Topic}", topic);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "App: Failed to publish printer status to MQTT topic {Topic}", topic);
                }
            }
        }
        private static async void PrinterLightFinishedToStandbyTimer_Tick(object sender, object e)
        {
            _printerLightFinishedToStandbyTimer.Stop(); // Timer should only fire once per FINISHED state
            Logger?.Debug("App: Printer light FINISHED to STANDBY timer elapsed.");

            // Only send STANDBY if the current command is still FINISHED
            // (to avoid overriding a newer command if printer status changed again quickly)
            if (_currentLightCommandSent == LIGHT_CMD_FINISHED)
            {
                await SendPrinterLightCommandAsync(LIGHT_CMD_STANDBY);
            }
        }

        private static async Task SendPrinterLightCommandAsync(string command)
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                if (_currentLightCommandSent == command && command != LIGHT_CMD_PRINTING && command != LIGHT_CMD_FINISHED) // Avoid spamming STANDBY if already in STANDBY
                {
                    // Exception for PRINTING and FINISHED as they might be re-triggered if status flaps.
                    // For STANDBY, only send if it's a change.
                    // This logic can be refined based on how "sticky" you want commands to be.
                    Logger?.Verbose("App: Light command '{Command}' is the same as last sent. Not sending again.", command);
                    return;
                }

                Logger?.Information("App: Sending printer light command '{Command}' to topic '{Topic}'.", command, PRINTER_LIGHT_MQTT_TOPIC);
                try
                {
                    await MqttServiceInstance.PublishAsync(PRINTER_LIGHT_MQTT_TOPIC, command, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, retain: false);
                    _currentLightCommandSent = command; // Update last sent command
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "App: Failed to publish light command '{Command}'", command);
                }
            }
            else
            {
                Logger?.Warning("App: MQTT service not available. Cannot publish light command '{Command}'.", command);
            }
        }


        private static async Task ControlPrinterLightBasedOnStatus(PrinterStatusEventArgs currentStatusArgs)
        {
            if (currentStatusArgs == null)
            {
                Logger?.Warning("ControlPrinterLight: Received null currentStatusEventArgs, cannot control light.");
                return;
            }

            string newDesiredLightState = null; // Represents the conceptual state, not necessarily the command string yet
            string currentPrinterStatus = currentStatusArgs.Status;
            bool isPrinterConnected = currentStatusArgs.IsPrinterLikelyConnected;

            if (!isPrinterConnected && App.CurrentSettings?.EnablePrinting == true)
            {
                Logger?.Warning("ControlPrinterLight: Printer not connected (printing enabled). Light to STANDBY/ERROR.");
                newDesiredLightState = LIGHT_CMD_STANDBY; // Or a specific "ERROR_NO_PRINTER" command
                _printerLightFinishedToStandbyTimer.Stop(); // Stop any pending transition to STANDBY
            }
            else if (currentPrinterStatus == PrinterStatuses.Printing) //
            {
                newDesiredLightState = LIGHT_CMD_PRINTING;
                _printerLightFinishedToStandbyTimer.Stop();
            }
            else if (currentPrinterStatus == PrinterStatuses.Idle) //
            {
                if (_previousPrinterStatusForLight == PrinterStatuses.Printing) // Just finished printing
                {
                    newDesiredLightState = LIGHT_CMD_FINISHED;
                    // Start the timer to transition from FINISHED to STANDBY
                    int delaySeconds = App.CurrentSettings?.PrinterIdleLightDelaySeconds ?? 20;
                    _printerLightFinishedToStandbyTimer.Interval = TimeSpan.FromSeconds(delaySeconds);
                    _printerLightFinishedToStandbyTimer.Start();
                    Logger?.Debug("ControlPrinterLight: Printer finished. Light to FINISHED. Standby timer started for {Delay}s.", delaySeconds);
                }
                else if (_currentLightCommandSent != LIGHT_CMD_STANDBY && !_printerLightFinishedToStandbyTimer.IsRunning)
                {
                    // If it's idle, wasn't just printing, and the FINISHED->STANDBY timer isn't running,
                    // it should probably be STANDBY.
                    newDesiredLightState = LIGHT_CMD_STANDBY;
                }
                // If timer is running, newDesiredLightState remains null, current light command (_currentLightCommandSent) should be FINISHED.
                // The timer tick will handle the transition to STANDBY.
            }
            // Handle specific error states from DNP JSON to set a distinct error light
            else if (currentStatusArgs.Status != null &&
                     (currentStatusArgs.Status == PrinterStatuses.PaperOut ||         //
                      currentStatusArgs.Status == PrinterStatuses.RibbonOut ||        //
                      currentStatusArgs.Status == PrinterStatuses.CoverOpen ||        //
                      currentStatusArgs.Status == PrinterStatuses.PaperJam ||         //
                      currentStatusArgs.Status.Contains("ERR", StringComparison.OrdinalIgnoreCase))) // Generic error check
            {
                Logger?.Warning("ControlPrinterLight: Printer error state ({Status}). Light to STANDBY/ERROR.", currentStatusArgs.Status);
                newDesiredLightState = LIGHT_CMD_STANDBY; // Or a specific "ERROR_PRINTER_ISSUE" command
                _printerLightFinishedToStandbyTimer.Stop();
            }
            else // Default to STANDBY for other unknown/unhandled states or if printing is disabled
            {
                if (_currentLightCommandSent != LIGHT_CMD_STANDBY && !_printerLightFinishedToStandbyTimer.IsRunning)
                {
                    newDesiredLightState = LIGHT_CMD_STANDBY;
                }
                _printerLightFinishedToStandbyTimer.Stop();
            }

            if (!string.IsNullOrEmpty(newDesiredLightState) && _currentLightCommandSent != newDesiredLightState)
            {
                // Use Task.Run to ensure SendPrinterLightCommandAsync doesn't block this thread
                // if MqttServiceInstance.PublishAsync has internal awaits.
                _ = Task.Run(async () => await SendPrinterLightCommandAsync(newDesiredLightState));
            }

            _previousPrinterStatusForLight = currentPrinterStatus;
        }

        // Roep deze methode aan bij het opstarten van de applicatie, nadat MQTT is geïnitialiseerd en verbonden.
        // En mogelijk ook wanneer de DNP Status voor het eerst als "Idle" wordt gedetecteerd.
        public static void SetInitialPrinterLightState()
        {
            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected)
            {
                string initialCommand = "STANDBY";
                Logger?.Information("App: Setting initial printer light state to '{LightCommand}' on topic '{Topic}'.", initialCommand, PRINTER_LIGHT_MQTT_TOPIC);
                _ = Task.Run(async () => {
                    try
                    {
                        await MqttServiceInstance.PublishAsync(PRINTER_LIGHT_MQTT_TOPIC, initialCommand, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, retain: false);
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex, "App: Failed to publish initial light command '{LightCommand}'", initialCommand);
                    }
                });
                // Initialize _previousLightControlStatus, so the first real status update is seen as a change if different from Idle/Standby
                // If printer starts as Idle, ControlPrinterLightBasedOnStatus will send STANDBY if logic matches.
                // _previousLightControlStatus = PrinterStatuses.Idle; // Or null initially
            }
            else
            {
                Logger?.Warning("App: MQTT not connected, cannot set initial printer light state.");
            }
        }

        private async void MqttService_ConnectionStatusChanged(object? sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Information("MQTT: Connected. Publishing initial 'Online' status.", PhotoboothIdentifier);
                await PublishConnectionStatusAsync("online"); // Retain online status

                // Now that MQTT is connected and initial "online" is sent,
                // publish the full current settings to the /settings/current_state topic.
                Logger.Information("MQTT: Connection established, now publishing full current settings for {PhotoboothId}.", PhotoboothIdentifier);
                await PublishCurrentSettingsToMqttAsync(CurrentSettings); // Maybe have to change to fire and forget _ = if this holds up the UI thread
                await PublishPrinterStatusToMqttAsync();
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

                    // Add JsonSerializerOptions with Enum Converter
                    // In order to send enum values as strings instead of integers.
                    var serializerOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true, // Optional: for readable JSON in MQTT
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // Keeps JSON clean
                        Converters = { new JsonStringEnumConverter() } 
                    };

                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(statusObject, serializerOptions);

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

            string logsDirectory = System.IO.Path.Combine(localAppDataPath, "Logs"); // New base path

            if (!Directory.Exists(logsDirectory)) Directory.CreateDirectory(logsDirectory);

            // TODO: retrieve this from settings
            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Verbose() // Overall minimum level for logs processed by Serilog
                .Enrich.FromLogContext()
                .Enrich.WithProperty("PhotoboothID", PhotoboothIdentifier ?? "UnknownID_AtLoggingConfig") // Use PhotoboothIdentifier if available
                .Enrich.WithProperty("Application", "PhotoBoothApp");

            // --- File Sink with Compact JSON Format ---
            try
            {
                loggerConfiguration.WriteTo.File(
                    formatter: new RenderedCompactJsonFormatter(), // Use the compact JSON formatter
                    path: System.IO.Path.Combine(logsDirectory, "photobooth-log-.ndjson"), // Suggest .ndjson or .json.log extension
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Failed to configure File sink: {ex.Message}");
            }

            // --- Debug Sink (remains the same) ---
            // TODO: change this so that only warnings are logged to the console
            //loggerConfiguration.WriteTo.Debug(); // Standard Visual Studio debug output (usually text-based)
            // Disabled; consumes to much CPU and is not needed in production

            // TODO: create a sink that logs to a remote server
            // Maybe HTTP or other system

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
                MqttServiceInstance.ApplyRemoteSettingsAsync -= ProcessAndApplyRemoteSettingsAsync;
                await MqttServiceInstance.DisposeAsync();
                Logger.Information("MQTT Service disposed on window close for Photobooth ID: {PhotoboothId}.", PhotoboothIdentifier);
            }
            // --- End of MQTT Service Disposal ---

            // --- Stop DNP Status Monitoring ---
            DnpStatusMonitor?.StopMonitoring();
            DnpStatusMonitor?.Dispose();
            Logger?.Debug("DnpStatusService disposed on window close.");
            // --- End of DNP Status Monitoring Disposal ---

            // --- Flush logs and close application ---
            Logger.Information("Flushing logs and closing application.");
            await Log.CloseAndFlushAsync();


        }

        private async Task ProcessAndApplyRemoteSettingsAsync(PhotoBoothSettings incomingSettings)
        {
            App.Logger?.Information("App: Received incoming settings from MQTT for processing. Incoming Timestamp: {Timestamp}", incomingSettings.LastModifiedUtc);

            // Behoud de PhotoboothId en LastModifiedUtc van de inkomende settings, deze zijn leidend.
            // De 'incomingSettings' bevat de waarden zoals ze via MQTT zijn gestuurd.

            // 1. Achtergrondafbeelding logica
            string newLocalBackgroundImagePath = incomingSettings.BackgroundImagePath; // Start met wat binnenkomt
            bool requiresSave = false; // Wordt true als we iets wijzigen aan incomingSettings

            if (!string.IsNullOrEmpty(incomingSettings.RemoteBackgroundImageUrl))
            {
                // Er is een RemoteBackgroundImageUrl opgegeven. Probeer te downloaden.
                App.Logger?.Information("App: RemoteBackgroundImageUrl ('{Url}') is specified. Attempting download.", incomingSettings.RemoteBackgroundImageUrl);
                string downloadedPath = await DownloadAndSaveImageAsync(
                    incomingSettings.RemoteBackgroundImageUrl,
                    incomingSettings.RemoteBackgroundImageHash,
                    (incomingSettings.RemoteBackgroundImageHash ?? Guid.NewGuid().ToString()) // Filename seed
                );

                if (!string.IsNullOrEmpty(downloadedPath))
                {
                    // Download gelukt
                    App.Logger?.Information("App: Background image downloaded successfully to: {Path}", downloadedPath);
                    newLocalBackgroundImagePath = downloadedPath;
                    incomingSettings.LastSuccessfullyDownloadedImageUrl = incomingSettings.RemoteBackgroundImageUrl; // Houd deze vast voor eventuele latere vergelijking
                    incomingSettings.LastSuccessfullyDownloadedImageHash = incomingSettings.RemoteBackgroundImageHash; // Houd deze vast

                    // WIS RemoteBackgroundImageUrl en RemoteBackgroundImageHash NU ze succesvol verwerkt zijn.
                    App.Logger?.Information("App: Clearing RemoteBackgroundImageUrl and RemoteBackgroundImageHash from settings object as download was successful and path is now local.");
                    incomingSettings.RemoteBackgroundImageUrl = "";
                    incomingSettings.RemoteBackgroundImageHash = "";
                    // De requiresSave wordt hieronder al getriggerd door de wijziging in BackgroundImagePath
                }
                else
                {
                    // Download mislukt
                    App.Logger?.Warning("App: Failed to download background image from '{Url}'. The existing local BackgroundImagePath (if any was provided in MQTT message) will be cleared, or previous local path (if any) will be lost.", incomingSettings.RemoteBackgroundImageUrl);
                    // Volgens jouw wens: als download mislukt, mag het lokale pad niet leeg zijn,
                    // maar de JSON/MQTT moet wel het *resultaat* van de poging reflecteren.
                    // Als download mislukt, dan is er geen *nieuw* lokaal pad.
                    // Als er in `incomingSettings` een `BackgroundImagePath` was meegegeven samen met de `RemoteBackgroundImageUrl`,
                    // dan wordt die `BackgroundImagePath` nu effectief genegeerd omdat de remote download prioriteit had en mislukte.
                    newLocalBackgroundImagePath = ""; // Geen geldig nieuw lokaal pad
                    incomingSettings.LastSuccessfullyDownloadedImageUrl = ""; // Mislukte download
                    incomingSettings.LastSuccessfullyDownloadedImageHash = "";

                    // TODO: maybe create a MQTT topic to publish responses to commands?
                }
                // Update BackgroundImagePath in het object dat we gaan opslaan
                if (incomingSettings.BackgroundImagePath != newLocalBackgroundImagePath)
                {
                    incomingSettings.BackgroundImagePath = newLocalBackgroundImagePath;
                    requiresSave = true; // Markeer dat er een wijziging is die opslag vereist
                }

                // Zorg ervoor dat 'requiresSave' ook true is als RemoteBackgroundImageUrl of RemoteBackgroundImageHash gewijzigd (gewist) is.
                // Dit gebeurt impliciet als ze onderdeel zijn van het 'incomingSettings' object dat vergeleken wordt
                // of expliciet als we de 'requiresSave' vlag hier zetten.
                // Omdat we incomingSettings direct aanpassen, zal de SaveSettingsAsync de gewijzigde (gewiste) URLs opslaan.
                if (string.IsNullOrEmpty(incomingSettings.RemoteBackgroundImageUrl) && !string.IsNullOrEmpty(incomingSettings.LastSuccessfullyDownloadedImageUrl))
                {
                    // Dit dekt het geval dat de RemoteBackgroundImageUrl is gewist (omdat download succesvol was)
                    // terwijl LastSuccessfullyDownloadedImageUrl nog wel de oude URL bevatte.
                    requiresSave = true;
                }
            }
            else // Geen RemoteBackgroundImageUrl opgegeven
            {
                App.Logger?.Information("App: No RemoteBackgroundImageUrl specified. Using BackgroundImagePath ('{Path}') from incoming settings directly.", incomingSettings.BackgroundImagePath);
                // Het `incomingSettings.BackgroundImagePath` (dat je via MQTT stuurde) blijft behouden.
                // We moeten wel zorgen dat 'LastSuccessfullyDownloaded' info wordt gewist als er geen remote URL meer is.
                if (!string.IsNullOrEmpty(incomingSettings.LastSuccessfullyDownloadedImageUrl))
                {
                    incomingSettings.LastSuccessfullyDownloadedImageUrl = "";
                    requiresSave = true;
                }
                if (!string.IsNullOrEmpty(incomingSettings.LastSuccessfullyDownloadedImageHash))
                {
                    incomingSettings.LastSuccessfullyDownloadedImageHash = "";
                    requiresSave = true;
                }
                // `newLocalBackgroundImagePath` is hier al `incomingSettings.BackgroundImagePath`.
                // Als `incomingSettings.BackgroundImagePath` veranderd is t.o.v. wat er al in `CurrentSettings` stond,
                // dan zal dat een `requiresSave` triggeren als we `CurrentSettings` updaten.
                // De check `incomingSettings.BackgroundImagePath != newLocalBackgroundImagePath` is hierboven al gedaan
                // of de `incomingSettings.BackgroundImagePath` wordt hier direct gerespecteerd.
            }

            // Vergelijk de resulterende 'incomingSettings' met 'CurrentSettings' om te zien of er echt iets is veranderd
            // dat opslag en UI update vereist. Dit is een beetje lastig zonder een diepe vergelijking.
            // Voor nu gaan we ervan uit dat als dit pad wordt aangeroepen, we de `incomingSettings` als de nieuwe waarheid beschouwen.
            // De `LastModifiedUtc` in `incomingSettings` is leidend.

            // Controleer of de PhotoboothId is veranderd en valideer deze.
            if (string.IsNullOrWhiteSpace(incomingSettings.PhotoboothId) ||
                incomingSettings.PhotoboothId.Contains("/") || incomingSettings.PhotoboothId.Contains("+") || incomingSettings.PhotoboothId.Contains("#"))
            {
                string oldId = incomingSettings.PhotoboothId;
                incomingSettings.PhotoboothId = $"PhotoBooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}";
                Logger?.Warning("App: Invalid PhotoboothId ('{OldId}') in remote settings, corrected to: {NewId}", oldId, incomingSettings.PhotoboothId);
                requiresSave = true;
            }
            // Update de globale PhotoboothIdentifier als deze is gewijzigd.
            if (PhotoboothIdentifier != incomingSettings.PhotoboothId)
            {
                PhotoboothIdentifier = incomingSettings.PhotoboothId;
                // TODO: Overweeg MQTT client opnieuw te verbinden als PhotoboothId wijzigt,
                //       omdat topics ervan afhangen. Voor nu loggen we alleen.
                Logger?.Warning("App: PhotoboothIdentifier changed to {NewId} due to remote settings. MQTT topics might need re-subscription if service was already connected.", PhotoboothIdentifier);
                requiresSave = true;
            }


            // Update de globale CurrentSettings
            PhotoBoothSettings oldEffectiveSettings = CurrentSettings; // Voor UI vergelijking
            CurrentSettings = incomingSettings; // 'incomingSettings' is nu de nieuwe autoriteit
            App.Logger?.Information("App: App.CurrentSettings updated with processed remote settings. New Timestamp: {Timestamp}", CurrentSettings.LastModifiedUtc);

            // 3. Sla de definitief verwerkte instellingen op.
            // De 'true' voor isFromRemoteUpdate zorgt ervoor dat de LastModifiedUtc van incomingSettings wordt behouden.
            await SettingsManager.SaveSettingsAsync(CurrentSettings, true);
            // Het OnSettingsWrittenToDisk event in SettingsManager zal nu
            // PublishCurrentSettingsToMqttAsync aanroepen met de *definitieve* staat.

            // 4. Dispatch UI updates naar de UI thread
            if (MainWindow?.DispatcherQueue != null)
            {
                MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    App.Logger?.Debug("App: Now on UI thread. Attempting to refresh active page UI after remote settings update.");
                    // Logica om de UI te verversen (vergelijkbaar met wat je had in OnRemoteSettingsUpdated)
                    // Dit is belangrijk als de pagina al geladen is en moet reageren op de nieuwe settings.
                    if (oldEffectiveSettings?.BackgroundImagePath != CurrentSettings.BackgroundImagePath ||
                        oldEffectiveSettings?.UiMainPageTitleText != CurrentSettings.UiMainPageTitleText /* etc. voor andere UI-gebonden settings */)
                    {
                        await PreloadBackgroundImageAsync(); // Preload opnieuw als pad is gewijzigd
                    }

                    try
                    {
                        if (MainWindow.Content is Frame rootFrame)
                        {
                            if (rootFrame.Content is MainPage mainPageInstance)
                            {
                                mainPageInstance.LoadDynamicUITexts();
                                await mainPageInstance.LoadPageBackgroundAsync();
                            }
                            else if (rootFrame.Content is PhotoBoothPage photoBoothPageInstance)
                            {
                                // Roep methoden aan op photoBoothPageInstance om UI te vernieuwen
                                // photoBoothPageInstance.LoadConfigurableTexts(); // Als je zo'n methode hebt
                                // await photoBoothPageInstance.LoadPageBackgroundAsync(); // Als je zo'n methode hebt
                            }
                        }
                    }
                    catch (Exception uiEx)
                    {
                        App.Logger?.Error(uiEx, "App: Exception occurred during UI refresh on UI thread from ProcessAndApplyRemoteSettingsAsync.");
                    }
                });
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

            await PublishCurrentSettingsToMqttAsync(activeSettings);

        }

        // Publishes the current settings to MQTT
        private static async Task PublishCurrentSettingsToMqttAsync(PhotoBoothSettings settingsToPublish)
        {
            if (settingsToPublish == null)
            {
                Logger?.Warning("App: PublishCurrentSettingsToMqttAsync called with null settings. Aborting.");
                return;
            }

            if (MqttServiceInstance != null && MqttServiceInstance.IsConnected && !string.IsNullOrEmpty(PhotoboothIdentifier))
            {
                // Use the PhotoboothIdentifier from the settings object being published for consistency in the topic
                string currentIdForTopic = settingsToPublish.PhotoboothId ?? PhotoboothIdentifier;
                string topic = $"photobooth/{currentIdForTopic}/settings/current_state";

                Logger?.Information("App: Publishing current settings to MQTT. Topic: {Topic}, Timestamp: {Timestamp}", topic, settingsToPublish.LastModifiedUtc);

                try
                {
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(settingsToPublish,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = true,
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                        });

                    // This message should NOT be retained; it's a point-in-time snapshot.
                    await MqttServiceInstance.PublishAsync(topic, jsonPayload, MqttQualityOfServiceLevel.AtLeastOnce, true);
                    Logger?.Information("App: Successfully published current settings (full object) to {Topic}", topic);
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "App: Failed to publish current settings (full object) to MQTT topic {Topic}", topic);
                }
            }
            else
            {
                Logger?.Warning("App: Cannot publish current settings. MQTT not connected, service not available, or PhotoboothIdentifier for topic is missing. Settings Timestamp: {Timestamp}", settingsToPublish.LastModifiedUtc);
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
            LoadingMainPage,
            Idle,
            LoadingPhotoBoothPage,
            ResettingPhotoBoothPage,
            ShowingInstructions,
            Countdown,
            TakingPhoto,
            DownloadingPhotoFromCamera,
            RecordingVideo,
            ShowingSinglePhoto,
            ReviewingPhotos,
            ReviewingPhotosTimedOut,
            Saving, 
            Processing,
            Uploading,
            ShowingQrCode,
            QrCodeTimedOut,
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
                    Logger?.Debug("App: State changed from {OldState} to: {NewOperationalState}", oldStateForLog, _state.ToString());

                    // Automatically publish the full status when this state changes
                    _ = PublishPhotoBoothStatusJsonAsync();
                }
            }
        }

        // New public static method to update CurrentSettings
        public static void UpdateAppSettings(PhotoBoothSettings newSettings)
        {
            if (newSettings != null)
            {
                CurrentSettings = newSettings;
                Logger?.Information("App: App.CurrentSettings has been updated programmatically.");

                //LastSettingsChange = DateTime.Now;

                // Optionally, if PhotoboothIdentifier could change via SettingsPage (unlikely for now), re-validate it here.
                // string oldId = PhotoboothIdentifier;
                // PhotoboothIdentifier = CurrentSettings.PhotoboothId;
                // ... (validation logic for PhotoboothIdentifier) ...
            }
            else
            {
                Logger?.Warning("App: UpdateAppSettings called with null settings. No update performed.");
            }
        }

        public static async Task PreloadBackgroundImageAsync()
        {
            Logger?.Debug($"Preloading background images");

            if (CurrentSettings == null || string.IsNullOrEmpty(CurrentSettings.BackgroundImagePath))
            {
                Logger?.Information("App Preloader: No background image path set in settings, skipping preload.");
                PreloadedBackgroundImage = null; // Ensure it's null if no path
                return;
            }

            if (!File.Exists(CurrentSettings.BackgroundImagePath))
            {
                Logger?.Warning("App Preloader: Background image file not found at {Path}, skipping preload.", CurrentSettings.BackgroundImagePath);
                PreloadedBackgroundImage = null;
                return;
            }

            try
            {
                Logger?.Information("App Preloader: Preloading background image from {Path}", CurrentSettings.BackgroundImagePath);
                BitmapImage bitmap = new BitmapImage();
                using (FileStream stream = File.OpenRead(CurrentSettings.BackgroundImagePath))
                {
                    // Set DecodePixelWidth/Height here if you want to decode to a specific size during preload
                    // This is useful if your original images are very large.
                    // Example:
                    // Find target screen dimensions or a reasonable max size
                    // var mainDisplayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(MainWindow.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                    // if (mainDisplayArea != null) {
                    //    bitmap.DecodePixelWidth = mainDisplayArea.WorkArea.Width;
                    // } else {
                    //    bitmap.DecodePixelWidth = 1920; // Fallback
                    // }
                    bitmap.DecodePixelWidth = 1920; // Or a sensible default based on your target display

                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                }
                PreloadedBackgroundImage = bitmap;
                Logger?.Information("App Preloader: Background image preloaded successfully.");
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "App Preloader: Failed to preload background image from {Path}", CurrentSettings.BackgroundImagePath);
                PreloadedBackgroundImage = null;
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