// PhotoBoothSettings.cs
using System;
using System.Collections.Generic; // For Environment.MachineName

namespace WinUI3App1 // Ensure this namespace matches your project
{
    public class PhotoBoothSettings
    {
        // General
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow; // Last modified date
        public string PhotoboothId { get; set; } = $"PhotoBooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}"; // Ensure safe ID

        // Background image
        public string BackgroundImagePath { get; set; } = ""; // Example: "Assets/default_background.jpg" or leave empty
        public string RemoteBackgroundImageUrl { get; set; } = "";    // URL from where to download the background
        public string RemoteBackgroundImageHash { get; set; } = "";   // Optional: SHA256 hash of the remote image for verification/update checks
        public string LastSuccessfullyDownloadedImageUrl { get; set; } = ""; // To track if current local image matches the remote URL
        public string LastSuccessfullyDownloadedImageHash { get; set; } = "";// To track if current local image matches the remote hash

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
        public string UiUploadError { get; set; } = "Error while uploading!";
        public string UiQrError { get; set; } = "Cannot create QR code!";
        public bool HorizontalReviewLayout { get; set; } = true;

        // Texts for the Accept/Retake buttons on PhotoBoothPage's review screen
        // Note: Your XAML for these buttons currently has hardcoded text.
        // You'll need to either bind these or set them from code.
        // Adding x:Name to the TextBlocks inside the buttons is one way.
        public string UiButtonAcceptText { get; set; } = "OK";
        public string UiButtonRetakeText { get; set; } = "Retake";

        // Timeouts and durations
        public int ReviewPageTimeoutSeconds { get; set; } = 30; // Default to 30 seconds
        public int QrCodeTimeoutSeconds { get; set; } = 60; // NIEUW

        // Printer
        public bool ShowPrinterWarnings { get; set; } = true;
        public string HotFolderPath { get; set; } = ""; // NIEUW: Voor DNP Hot Folder
        public string DnpPrinterStatusFilePath { get; set; } // NIEUW

        // Output file paths
        public string PhotoOutputPath { get; set; } = ""; // NIEUW: Voor foto's

        // Photo Strip Settings (from SettingsPage)
        public string PhotoStripFilePath { get; set; } = ""; // NIEUW: Vervangt PhotoStripTemplatePath en PhotoStripLayoutIndex
        public PhotoStripImageCompositionSettings PhotoStripComposition { get; set; } = new PhotoStripImageCompositionSettings();

        // Functionality (from SettingsPage)
        public bool EnablePhotos { get; set; } = true;
        public bool EnableVideos { get; set; } = false; // Default to false as per current example
        public bool EnablePrinting { get; set; } = true;
        public bool EnableUploading { get; set; } = true;
        public bool EnableShowQr { get; set; } = true;
        
        // External Equipment Settings
        public string LightPrinterMqttTopic { get; set; } = $"photobooth/{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}/light/printer"; // Voorbeeld topic
        public string InternalLightMqttTopic { get; set; } = $"photobooth/{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}/light/internal";
        public int InternalLedsMinimum { get; set; } = 20;
        public int InternalLedsMaximum { get; set; } = 100;
        public string DmxLightMqttTopic { get; set; } = $"photobooth/{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}/light/dmx";
        public int ExternalDmxMinimum { get; set; } = 10;
        public int ExternalDmxMaximum { get; set; } = 80;
        public int DmxStartAddress { get; set; } = 1;

        // External Control Settings (MQTT & Upload) 
        public string MqttBrokerAddress { get; set; } = "192.168.1.3";
        public int MqttBrokerPort { get; set; } = 1883;
        public string MqttUsername { get; set; } = "";
        public string MqttPassword { get; set; } = "d8232msn2987sd"; // Example password (TODO: secure storage for production)
        //public string ImageUploadUrl { get; set; } = "";
        public bool EnableRemoteAdminViaMqtt { get; set; } = true; // Was al aanwezig

        // Section for S3 settings
        public MinioSettings Minio { get; set; } = new MinioSettings();

        // Hidden/Advanced Settings (Examples of settings not on the UI but configurable via JSON)
        public bool AutoStartPhotoSequence { get; set; } = false; // E.g., auto-start after a delay
        public int CountdownDurationSeconds { get; set; } = 3;   // Duration for "3, 2, 1"
        //public string CameraResolution { get; set; } = "1920x1080"; // Desired camera resolution
        public int MaxVideoDurationSeconds { get; set; } = 180;
        public string LogLevel { get; set; } = "Information"; // For Serilog or other loggers
        public List<string> KioskComputerNames { get; set; } = new List<string> { "" }; // Example for local admin access

        // Constructor can be used to initialize complex defaults if needed
        public PhotoBoothSettings()
        {
            // Zorg ervoor dat default topics uniek zijn per machine indien nodig
            string machineNameIdentifier = Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty);
            LightPrinterMqttTopic = $"light/printer";
            InternalLightMqttTopic = $"light/internal";
            DmxLightMqttTopic = $"light/dmx";
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