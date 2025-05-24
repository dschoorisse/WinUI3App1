// DnpStatusService.cs
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WinUI3App1; // For App.CurrentSettings, App.Logger

// Define the DTOs for JSON deserialization (can be in this file or a separate DTOs.cs)
// These are based on your Printer.cs
public class DnpJsonRoot
{
    [JsonPropertyName("printers")]
    public DnpPrinter[] Printers { get; set; }
}

public class DnpPrinter
{
    public string Name { get; set; }
    public string StatusString { get; set; }
    public string RemainingMedia { get; set; }
    public string SerialNumber { get; set; }
    public string PaperType { get; set; }
    public string LifeCounter { get; set; }
    public string FirmwareVer { get; set; }
    public string ColorDataVer { get; set; }
    public string LastPrinterStatusChange { get; set; } // String from JSON
    public string RawStatus { get; set; }
    public string PrinterType { get; set; }
    public string HeadTemp { get; set; } // String from JSON
    public string Humidity { get; set; } // String from JSON
    public string Time { get; set; } // Hot Folder Utility timestamp string from JSON
}

// Define a class to hold the parsed and more usable printer status
public class PrinterStatusEventArgs : EventArgs
{
    public string Status { get; set; }
    public string Name { get; set; }
    public string PaperType { get; set; } 
    public int? RemainingMedia { get; set; }
    public string RawStatus { get; set; }
    public int? HeadTempCelsius { get; set; }
    public int? HumidityPercentage { get; set; }
    public string SerialNumber { get; set; }
    public int? LifeCounter { get; set; }
    public string FirmwareVersion { get; set; }
    public DateTimeOffset? LastStatusChangeTime { get; set; } // Parsed DateTimeOffset
    public DateTimeOffset? JsonFileTimestamp { get; set; }    // Parsed DateTimeOffset
    public bool IsJsonFileAccessible { get; set; }
    public bool IsPrinterLikelyConnected { get; set; } // Combined status: HotFolder utility active & printer not in system error
    public bool IsHotFolderUtilityActive { get; set; }

    public PrinterStatusEventArgs(string status = null, bool isConnected = false, bool isHotFolderActive = false, bool isJsonAccessible = true)
    {
        Status = status ?? PrinterStatuses.Unknown; // Default to Unknown if null
        IsPrinterLikelyConnected = isConnected;
        IsHotFolderUtilityActive = isHotFolderActive;
        IsJsonFileAccessible = isJsonAccessible;
    }
}


public static class PrinterStatuses // Keep your status constants
{
    // ... (all your const string statuses like Printing, Idle, SystemError, etc.) ...
    public const string Printing = "Printing"; //
    public const string Idle = "Idle"; //
    public const string SystemError = "STATUS_SYSTEMERR"; //
    public const string Ok = "STATUS_OK"; //
    public const string Unknown = "STATUS_UNKNOWN"; //
    public const string NoStatus = "STATUS_NOSTATUS"; //
    public const string PaperOut = "STATUS_PAPEROUT"; //
    public const string RibbonOut = "STATUS_RIBBONOUT"; //
    public const string CoverOpen = "STATUS_COVEROPEN"; //
    public const string RibbonError = "STATUS_RIBBONERR"; //
    public const string PaperError = "STATUS_PAPERERR"; //
    public const string PaperJam = "STATUS_PAPERJAM"; //
    public const string ScrapBoxError = "STATUS_SCRAPBOXERR"; //
    public const string MotCooling = "STATUS_MOTCOOLING"; //
    public const string DataError = "STATUS_DATAERR"; //
    public const string HardwareError = "STATUS_HARDWAREERR"; //
    public const string NotInitialized = "STATUS_NOT_INITIALIZED"; //
    public const string Offline = "STATUS_OFFLINE"; //
}

namespace WinUI3App // Your main application namespace
{
    public class DnpStatusService : IDisposable
    {
        private readonly string _statusFilePath;
        private readonly DispatcherQueueTimer _timer;
        private readonly ILogger _logger;
        private readonly CultureInfo _parsingCulture = CultureInfo.InvariantCulture;
        private readonly string _dateTimeFormat = "M/d/yyyy h:mm:ss tt"; // Format from your Printer.cs

        public event EventHandler<PrinterStatusEventArgs> PrinterStatusUpdated;
        public PrinterStatusEventArgs CurrentPrinterStatus { get; private set; }

        private DateTime _lastLogNoPrinterFound = DateTime.MinValue;
        private string _previousRawPrinterStatus; // To detect actual status changes

        public DnpStatusService(PhotoBoothSettings settings, ILogger logger, DispatcherQueue dispatcherQueue)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _statusFilePath = settings?.DnpPrinterStatusFilePath;

            if (string.IsNullOrEmpty(_statusFilePath))
            {
                _logger.Warning("DnpStatusService: DNP Printer Status File Path is not configured. Service will not start.");
                CurrentPrinterStatus = new PrinterStatusEventArgs("Not Configured", isJsonAccessible: false);
                PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                return; // Do not start timer if path is not set
            }

            _logger.Information("DnpStatusService: Initializing. Monitoring DNP status file: {FilePath}", _statusFilePath);
            CurrentPrinterStatus = new PrinterStatusEventArgs("Initializing"); // Initial status

            _timer = dispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(2); // Check every 2 seconds (configurable)
            _timer.Tick += Timer_Tick;
            _timer.IsRepeating = true;
        }

        public void StartMonitoring()
        {
            if (string.IsNullOrEmpty(_statusFilePath))
            {
                _logger.Warning("DnpStatusService: Cannot start monitoring, status file path is not set.");
                return;
            }
            _logger.Information("DnpStatusService: Starting to monitor DNP status file.");
            _timer.Start();
            // Perform an initial read
            ReadAndProcessStatusFile();
        }

        public void StopMonitoring()
        {
            _logger.Information("DnpStatusService: Stopping DNP status file monitoring.");
            _timer.Stop();
        }

        private async void Timer_Tick(object sender, object e)
        {
            await ReadAndProcessStatusFile();
        }

        private async Task ReadAndProcessStatusFile()
        {
            if (!File.Exists(_statusFilePath))
            {
                if (CurrentPrinterStatus.IsJsonFileAccessible || CurrentPrinterStatus.Status != PrinterStatuses.NoStatus)
                {
                    _logger.Warning("DnpStatusService: Status file not found at {FilePath}", _statusFilePath);
                    CurrentPrinterStatus = new PrinterStatusEventArgs(PrinterStatuses.NoStatus, isConnected: false, isHotFolderActive: false, isJsonAccessible: false);
                    PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                }
                return;
            }

            try
            {
                string jsonContent = await File.ReadAllTextAsync(_statusFilePath);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    if (CurrentPrinterStatus.Status != PrinterStatuses.NoStatus || CurrentPrinterStatus.IsJsonFileAccessible)
                    {
                        _logger.Warning("DnpStatusService: Status file is empty at {FilePath}", _statusFilePath);
                        CurrentPrinterStatus = new PrinterStatusEventArgs(PrinterStatuses.NoStatus, isConnected: false, isHotFolderActive: false, isJsonAccessible: true); // File exists but empty
                        PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                    }
                    return;
                }

                DnpJsonRoot dnpData = JsonSerializer.Deserialize<DnpJsonRoot>(jsonContent);

                if (dnpData?.Printers != null && dnpData.Printers.Any())
                {
                    DnpPrinter printerInfo = dnpData.Printers[0]; // Assume first printer, TODO: make configurable 
                    var newStatus = new PrinterStatusEventArgs(isJsonAccessible: true);

                    newStatus.Name = printerInfo.Name;
                    newStatus.Status = printerInfo.StatusString;
                    newStatus.RawStatus = printerInfo.RawStatus;
                    newStatus.SerialNumber = printerInfo.SerialNumber;
                    newStatus.FirmwareVersion = printerInfo.FirmwareVer;
                    newStatus.PaperType = printerInfo.PaperType;


                    if (int.TryParse(printerInfo.RemainingMedia, out int remaining))
                        newStatus.RemainingMedia = remaining;
                    if (int.TryParse(printerInfo.HeadTemp, out int tempF))
                        newStatus.HeadTempCelsius = (int)Math.Round((tempF - 32) * 5.0 / 9.0); // F to C
                    if (int.TryParse(printerInfo.Humidity, out int humidityVal))
                        newStatus.HumidityPercentage = humidityVal;
                    if (int.TryParse(printerInfo.LifeCounter, out int life))
                        newStatus.LifeCounter = life;

                    if (DateTimeOffset.TryParseExact(printerInfo.LastPrinterStatusChange, _dateTimeFormat, _parsingCulture, DateTimeStyles.None, out var lastChange))
                        newStatus.LastStatusChangeTime = lastChange;
                    else
                        _logger.Verbose("DnpStatusService: Could not parse LastPrinterStatusChange: {Value}", printerInfo.LastPrinterStatusChange);

                    if (DateTimeOffset.TryParseExact(printerInfo.Time, _dateTimeFormat, _parsingCulture, DateTimeStyles.None, out var jsonTime))
                        newStatus.JsonFileTimestamp = jsonTime;
                    else
                        _logger.Verbose("DnpStatusService: Could not parse DNP utility JSON Time: {Value}", printerInfo.Time);

                    // Check if HotFolder utility is active (based on its own timestamp)
                    if (newStatus.JsonFileTimestamp.HasValue)
                    {
                        newStatus.IsHotFolderUtilityActive = (DateTimeOffset.Now - newStatus.JsonFileTimestamp.Value) < TimeSpan.FromMinutes(App.CurrentSettings?.DnpStatusFileMaxAgeMinutes ?? 5); //
                    }
                    else
                    {
                        newStatus.IsHotFolderUtilityActive = false; // Cannot determine if no timestamp
                    }

                    // Determine overall printer connectivity
                    newStatus.IsPrinterLikelyConnected = newStatus.IsHotFolderUtilityActive &&
                                                        newStatus.Status != PrinterStatuses.SystemError &&
                                                        newStatus.Status != PrinterStatuses.Offline && // Add offline check
                                                        newStatus.Status != PrinterStatuses.NotInitialized;


                    // Only invoke update if there's a meaningful change to avoid excessive events
                    if (newStatus.RawStatus != _previousRawPrinterStatus ||
                        newStatus.IsPrinterLikelyConnected != CurrentPrinterStatus.IsPrinterLikelyConnected ||
                        !CurrentPrinterStatus.IsJsonFileAccessible) // Always update if file was previously inaccessible
                    {
                        CurrentPrinterStatus = newStatus;
                        PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                        _previousRawPrinterStatus = newStatus.RawStatus;
                        _logger.Debug("DnpStatusService: Printer status updated - Status: {Status}, Connected: {Connected}, HotFolderActive: {HotFolderActive}, Remaining: {Remaining}",
                            CurrentPrinterStatus.Status, CurrentPrinterStatus.IsPrinterLikelyConnected, CurrentPrinterStatus.IsHotFolderUtilityActive, CurrentPrinterStatus.RemainingMedia);
                    }
                    _lastLogNoPrinterFound = DateTime.MinValue; // Reset this if printer is found
                }
                else
                {
                    if (CurrentPrinterStatus.Status != PrinterStatuses.NoStatus || CurrentPrinterStatus.IsPrinterLikelyConnected)
                    {
                        // Log only if status changes or if it's been a while
                        if (DateTime.Now - _lastLogNoPrinterFound > TimeSpan.FromMinutes(1) || CurrentPrinterStatus.IsPrinterLikelyConnected) //
                        {
                            _logger.Warning("DnpStatusService: No printer data found in JSON file, though file was read.");
                            _lastLogNoPrinterFound = DateTime.Now;
                        }
                        CurrentPrinterStatus = new PrinterStatusEventArgs(PrinterStatuses.NoStatus, isConnected: false, isHotFolderActive: false, isJsonAccessible: true);
                        PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                        _previousRawPrinterStatus = null;
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                if (CurrentPrinterStatus.IsJsonFileAccessible)
                { // Log only if it was previously accessible
                    _logger.Error(jsonEx, "DnpStatusService: Failed to deserialize DNP status JSON from {FilePath}", _statusFilePath);
                    CurrentPrinterStatus = new PrinterStatusEventArgs(PrinterStatuses.Unknown, isConnected: false, isHotFolderActive: false, isJsonAccessible: false); // JSON parsing error
                    PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                    _previousRawPrinterStatus = null;
                }
            }
            catch (IOException ioEx)
            {
                if (CurrentPrinterStatus.IsJsonFileAccessible)
                { // Log only if it was previously accessible
                    _logger.Error(ioEx, "DnpStatusService: IOException while reading DNP status file {FilePath}. It might be locked.", _statusFilePath);
                    CurrentPrinterStatus = new PrinterStatusEventArgs(PrinterStatuses.Unknown, isConnected: false, isHotFolderActive: false, isJsonAccessible: false); // File access error
                    PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                    _previousRawPrinterStatus = null;
                }
            }
            catch (Exception ex)
            {
                if (CurrentPrinterStatus.IsJsonFileAccessible)
                {
                    _logger.Error(ex, "DnpStatusService: Unexpected error processing DNP status file {FilePath}", _statusFilePath);
                    CurrentPrinterStatus = new PrinterStatusEventArgs(PrinterStatuses.Unknown, isConnected: false, isHotFolderActive: false, isJsonAccessible: false); // Generic error
                    PrinterStatusUpdated?.Invoke(this, CurrentPrinterStatus);
                    _previousRawPrinterStatus = null;
                }
            }
        }

        public void Dispose()
        {
            _logger.Information("DnpStatusService: Disposing.");
            StopMonitoring();
            // _timer.Tick -= Timer_Tick; // DispatcherTimer Tick is unsubscribed when timer is stopped or disposed by GC
        }
    }
}