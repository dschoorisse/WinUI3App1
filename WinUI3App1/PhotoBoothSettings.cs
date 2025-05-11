// PhotoBoothSettings.cs
using System;
using System.Collections.Generic; // For Environment.MachineName

namespace WinUI3App1 // Ensure this namespace matches your project
{
    public class PhotoBoothSettings
    {
        // General
        public string PhotoboothId { get; set; } = $"PhotoBooth_{Environment.MachineName.Replace(" ", "_").ReplaceNonAlphaNumericChars(string.Empty)}"; // Ensure safe ID

        // Guest-Facing UI Texts for Main Page
        public string UiMainPageTitleText { get; set; } = "Welcome!";
        public string UiMainPageSubtitleText { get; set; } = "Capture your perfect moment.";
        public string UiMainPagePhotoButtonText { get; set; } = "Take Photo";
        public string UiMainPageVideoButtonText { get; set; } = "Record Video"; // Or "Make a Video", etc.

        // Guest-Facing UI Texts for PhotoBoothPage
        public string UiInstructionTextFormat { get; set; } = "We are going to take {0} pictures, get ready!"; // {0} will be replaced by number of photos
        public string UiCountdown3 { get; set; } = "3";
        public string UiCountdown2 { get; set; } = "2";
        public string UiCountdown1 { get; set; } = "1";
        public string UiCountdownSmile { get; set; } = "📸"; // Default to existing smiley, can be text like "Smile!"
        public string UiSavingMessage { get; set; } = "Saving...";
        public string UiDoneMessage { get; set; } = "Done!";

        // Texts for the Accept/Retake buttons on PhotoBoothPage's review screen
        // Note: Your XAML for these buttons currently has hardcoded text.
        // You'll need to either bind these or set them from code.
        // Adding x:Name to the TextBlocks inside the buttons is one way.
        public string UiButtonAcceptText { get; set; } = "OK";
        public string UiButtonRetakeText { get; set; } = "Retake";

        // UI/Look and Feel (from SettingsPage)
        public string BackgroundImagePath { get; set; } = ""; // Example: "Assets/default_background.jpg" or leave empty
        public int PhotoStripLayoutIndex { get; set; } = 0; // 0: Default, 1: Layout A, 2: Layout B etc.
        public string PhotoStripTemplatePath { get; set; } = ""; // Example: "Assets/template_4x6_3up.png"
        public int TimeoutSeconds { get; set; } = 60; // Timeout for user inactivity on welcome screen

        // Functionality (from SettingsPage)
        public bool EnablePhotos { get; set; } = true;
        public bool EnableVideos { get; set; } = false; // Default to false as per current example
        public bool EnablePrinting { get; set; } = true;
        public bool ShowPrinterWarnings { get; set; } = true;
        public string SelectedPrinter { get; set; } = ""; // Will be populated by available system printers

        // Lighting (from SettingsPage)
        public int InternalLedsMinimum { get; set; } = 20;  // Percentage
        public int InternalLedsMaximum { get; set; } = 100; // Percentage
        public int ExternalDmxMinimum { get; set; } = 10;  // Percentage
        public int ExternalDmxMaximum { get; set; } = 80;  // Percentage
        public string SelectedComPort { get; set; } = "";   // Will be populated by available COM ports

        // MQTT Settings (previously in AppSettings.cs, now centralized)
        public string MqttBrokerAddress { get; set; } = "192.168.1.3"; // Default MQTT broker
        public int MqttBrokerPort { get; set; } = 1883;              // Default MQTT port
        public string MqttUsername { get; set; } = ""; // Example username
        public string MqttPassword { get; set; } = "d8232msn2987sd"; // Example password (consider secure storage for production)

        // Hidden/Advanced Settings (Examples of settings not on the UI but configurable via JSON)
        public bool AutoStartPhotoSequence { get; set; } = false; // E.g., auto-start after a delay
        public int CountdownDurationSeconds { get; set; } = 3;   // Duration for "3, 2, 1"
        public string CameraResolution { get; set; } = "1920x1080"; // Desired camera resolution
        public int MaxVideoDurationSeconds { get; set; } = 15;
        public string LogLevel { get; set; } = "Information"; // For Serilog or other loggers
        public bool EnableRemoteAdminViaMqtt { get; set; } = false;
        public List<string> AdminUsernames { get; set; } = new List<string> { "admin" }; // Example for local admin access

        // Constructor can be used to initialize complex defaults if needed
        public PhotoBoothSettings()
        {
            // For PhotoboothId, ensure it's file-system/URL safe if used in paths/topics directly
            // A helper extension method might be useful for complex string cleaning
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