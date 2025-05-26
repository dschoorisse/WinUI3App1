// PhotoBoothSettings.cs
using System;
using System.Collections.Generic; // For Environment.MachineName

namespace WinUI3App1 // Ensure this namespace matches your project
{
    public class PhotoBoothSettings
    {
        public GeneralSettings General { get; set; } = new GeneralSettings();
        public PrinterSettings Printer { get; set; } = new PrinterSettings();
        public BackgroundSettings Background { get; set; } = new BackgroundSettings();
        public UserInterfaceSettings UserInterface { get; set; } = new UserInterfaceSettings();
        public TimeoutSettings Timeouts { get; set; } = new TimeoutSettings();
        public OutputSettings Output { get; set; } = new OutputSettings();
        public FunctionalitySettings Functionality { get; set; } = new FunctionalitySettings();
        public MqttSettings Mqtt { get; set; } = new MqttSettings();
        public AdvancedSettings Advanced { get; set; } = new AdvancedSettings();

        // Photo Strip Settings (from SettingsPage)
        public string PhotoStripFilePath { get; set; } = ""; // NIEUW: Vervangt PhotoStripTemplatePath en PhotoStripLayoutIndex
        public PhotoStripImageCompositionSettings PhotoStripComposition { get; set; } = new PhotoStripImageCompositionSettings();

        // Section for S3 settings
        public MinioSettings Minio { get; set; } = new MinioSettings();

        // Constructor can be used to initialize complex defaults if needed
        public PhotoBoothSettings()
        {
        }
    }

    public class PhotoStripImageCompositionSettings
    {
        // Marges binnen de template (in pixels)
        public int TemplateHorizontalPaddingPerSide { get; set; } = 85; // Marge links en rechts op de template
        public int TemplateDesiredTopMargin { get; set; } = 80;       // Marge boven de eerste foto
        public int TemplateDesiredBottomMargin { get; set; } = 500;    // Marge onder de laatste foto

        // Spacing tussen de foto's (in pixels)
        public int SpacingBetweenPhotos { get; set; } = 25;

        // Doel aspect ratio voor de 'vensters' waar de foto's in komen.
        // Bijvoorbeeld: 1.7777 (16:9), 1.5 (3:2), 1.3333 (4:3).
        // Of laat dit weg en bereken de hoogte van het slot puur op basis van beschikbare ruimte.
        // Voor nu laten we dit weg en gebruiken we de berekening zoals in je laatste code.
        // Je zou hier later een target aspect ratio of vaste hoogte kunnen toevoegen.
        // public double TargetPhotoSlotAspectRatio { get; set; } = 16.0 / 9.0; // Voorbeeld
    }

    public class MinioSettings // NIEUWE subklasse voor MinIO instellingen
    {
        public string ServiceUrl { get; set; } = "https://s3.devideopaal.nl"; // Je endpoint (pas poort aan indien nodig, of laat weg als standaard HTTP/HTTPS)
        public string BucketName { get; set; } = "photobooth-media"; // Voorbeeld bucket naam
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public bool UseHttp { get; set; } = false; // Bepaalt of http of https wordt gebruikt voor de ServiceUrl
        public string PublicBaseUrl { get; set; } = "https://s3.devideopaal.nl"; // De basis URL voor publieke links
    }

    public class GeneralSettings
    {
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow; // Last modified date
        public string PhotoboothId { get; set; } // Ensure safe ID

        public GeneralSettings()
        {
            PhotoboothId = $"PhotoBooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}";
        }
    }

    public class PrinterSettings
    {
        public int DnpStatusFileMaxAgeMinutes { get; set; } = 5; // Default 5 minuten
        public int PrinterIdleLightDelaySeconds { get; set; } = 20; // Default 20 seconden
        public bool ShowPrinterWarnings { get; set; } = true;
        public string HotFolderPath { get; set; } = "";
        public string DnpPrinterStatusFilePath { get; set; }

        public PrinterSettings()
        {
        }
    }

    public class BackgroundSettings
    {
        public string BackgroundImagePath { get; set; } = ""; // Example: "Assets/default_background.jpg" or leave empty
        public string RemoteBackgroundImageUrl { get; set; } = "";    // URL from where to download the background
        public string RemoteBackgroundImageHash { get; set; } = "";   // Optional: SHA256 hash of the remote image for verification/update checks
        public string LastSuccessfullyDownloadedImageUrl { get; set; } = ""; // To track if current local image matches the remote URL
        public string LastSuccessfullyDownloadedImageHash { get; set; } = "";// To track if current local image matches the remote hash

        public BackgroundSettings()
        {
        }
    }

    public class UserInterfaceSettings
    {
        // Guest-Facing UI Texts for Main Page
        public string UiMainPageTitleText { get; set; } = "Welcome!";
        public string UiMainPageSubtitleText { get; set; } = "Capture your perfect moment.";
        public string UiMainPagePhotoButtonText { get; set; } = "Take Photo";
        public string UiMainPageVideoButtonText { get; set; } = "Record Video"; // Or "Make a Video", etc.

        // PhotoBoothPage
        public string UiInstructionTextFormat { get; set; } = "We are going to take {0} pictures, get ready!"; // {0} will be replaced by number of photos
        public string UiCountdown3 { get; set; } = "3";
        public string UiCountdown2 { get; set; } = "2";
        public string UiCountdown1 { get; set; } = "1";
        public string UiCountdown0 { get; set; } = "📸"; // Default to camera smiley, can be text like "Smile!"
        public string UiSavingMessage { get; set; } = "Saving...";
        public string UiUploadingMessage { get; set; } = "Uploading...";
        public string UiDoneMessage { get; set; } = "Done!";
        public string UiQrInstruction { get; set; } = "Scan the code to view you photo strip!";
        public string UiQrCloseButton { get; set; } = "OK";
        public string UiUploadError { get; set; } = "Error while uploading!";
        public string UiQrError { get; set; } = "Cannot create QR code!";
        public bool HorizontalReviewLayout { get; set; } = true;
        public string UiButtonAcceptText { get; set; } = "OK";
        public string UiButtonRetakeText { get; set; } = "Retake";

        public UserInterfaceSettings()
        {
        }
    }

    public class TimeoutSettings
    {
        public int ReviewPageTimeoutSeconds { get; set; } = 30; // Default to 30 seconds
        public int QrCodeTimeoutSeconds { get; set; } = 60; // NIEUW

        public TimeoutSettings()
        {
        }
    }

    public class OutputSettings
    {
        public string PhotoOutputPath { get; set; } = ""; // NIEUW: Voor foto's

        public OutputSettings()
        {
        }
    }

    public class FunctionalitySettings
    {
        public bool EnablePhotos { get; set; } = true;
        public bool EnableVideos { get; set; } = false; // Default to false as per current example
        public bool EnablePrinting { get; set; } = true;
        public bool EnableUploading { get; set; } = true;
        public bool EnableShowQr { get; set; } = true;

        public FunctionalitySettings()
        {
        }
    }

    public class MqttSettings
    {
        public string LightPrinterMqttTopic { get; set; }
        public string InternalLightMqttTopic { get; set; }
        public int InternalLedsMinimum { get; set; } = 20;
        public int InternalLedsMaximum { get; set; } = 100;
        public string DmxLightMqttTopic { get; set; }
        public int ExternalDmxMinimum { get; set; } = 10;
        public int ExternalDmxMaximum { get; set; } = 80;
        public int DmxStartAddress { get; set; } = 1;
        public string MqttBrokerAddress { get; set; } = "192.168.1.3";
        public int MqttBrokerPort { get; set; } = 1883;
        public string MqttUsername { get; set; } = "";
        public string MqttPassword { get; set; } = "d8232msn2987sd"; // Example password (TODO: secure storage for production)
        public bool EnableRemoteAdminViaMqtt { get; set; } = true; // Was al aanwezig

        public MqttSettings()
        {
            string machineNameIdentifier = Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty);
            LightPrinterMqttTopic = $"photobooth/{machineNameIdentifier}/light/printer";
            InternalLightMqttTopic = $"photobooth/{machineNameIdentifier}/light/internal";
            DmxLightMqttTopic = $"photobooth/{machineNameIdentifier}/light/dmx";
        }
    }

    public class AdvancedSettings
    {
        public bool AutoStartPhotoSequence { get; set; } = false; // E.g., auto-start after a delay
        public int CountdownDurationSeconds { get; set; } = 3;   // Duration for "3, 2, 1"
        public int MaxVideoDurationSeconds { get; set; } = 180;
        public string LogLevel { get; set; } = "Information"; // For Serilog or other loggers
        public List<string> KioskComputerNames { get; set; } = new List<string> { "" }; // Example for local admin access

        public AdvancedSettings()
        {
        }
    }

    // Helper extension for cleaning strings (optional, place in a utility class if you have one)
    public static class StringExtensions
    {
        public static string ReplaceNonAlphaNumericChars(this string str, string replacement)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return System.Text.RegularExpressions.Regex.Replace(str, @"[^a-zA-Z0-9_]", replacement);
        }
    }
}