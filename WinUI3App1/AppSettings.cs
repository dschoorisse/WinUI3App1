﻿using System;
using System.Diagnostics;
using Windows.Storage;

namespace WinUI3App1
{
    /// <summary>
    /// Provides application-wide access to settings
    /// </summary>
    public static class AppSettings
    {
        #region UI/Look and Feel

        public static string BackgroundImagePath
        {
            get => GetSetting<string>("BackgroundImagePath", "");
            set => SaveSetting("BackgroundImagePath", value);
        }

        public static int PhotoStripLayoutIndex
        {
            get => GetSetting("PhotoStripLayoutIndex", 0);
            set => SaveSetting("PhotoStripLayoutIndex", value);
        }

        public static string PhotoStripTemplatePath
        {
            get => GetSetting<string>("PhotoStripTemplatePath", "");
            set => SaveSetting("PhotoStripTemplatePath", value);
        }

        public static int TimeoutSeconds
        {
            get => GetSetting("TimeoutSeconds", 60);
            set => SaveSetting("TimeoutSeconds", value);
        }

        #endregion

        #region Functionality

        public static bool EnablePhotos
        {
            get => GetSetting("EnablePhotos", true);
            set => SaveSetting("EnablePhotos", value);
        }

        public static bool EnableVideos
        {
            get => GetSetting("EnableVideos", false);
            set => SaveSetting("EnableVideos", value);
        }

        public static bool EnablePrinting
        {
            get => GetSetting("EnablePrinting", true);
            set => SaveSetting("EnablePrinting", value);
        }

        public static bool ShowPrinterWarnings
        {
            get => GetSetting("ShowPrinterWarnings", true);
            set => SaveSetting("ShowPrinterWarnings", value);
        }

        public static string SelectedPrinter
        {
            get => GetSetting<string>("SelectedPrinter", "");
            set => SaveSetting("SelectedPrinter", value);
        }

        #endregion

        #region Lighting

        public static int InternalLedsMinimum
        {
            get => GetSetting("InternalLedsMinimum", 20);
            set => SaveSetting("InternalLedsMinimum", value);
        }

        public static int InternalLedsMaximum
        {
            get => GetSetting("InternalLedsMaximum", 100);
            set => SaveSetting("InternalLedsMaximum", value);
        }

        public static int ExternalDmxMinimum
        {
            get => GetSetting("ExternalDmxMinimum", 10);
            set => SaveSetting("ExternalDmxMinimum", value);
        }

        public static int ExternalDmxMaximum
        {
            get => GetSetting("ExternalDmxMaximum", 80);
            set => SaveSetting("ExternalDmxMaximum", value);
        }

        public static string SelectedComPort
        {
            get => GetSetting<string>("SelectedComPort", "");
            set => SaveSetting("SelectedComPort", value);
        }

        #endregion

        #region Helper Methods

        private static T GetSetting<T>(string key, T defaultValue)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.TryGetValue(key, out object value) && value is T typedValue)
                {
                    return typedValue;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error retrieving setting '{key}': {ex.Message}");
            }
            return defaultValue;
        }

        private static void SaveSetting<T>(string key, T value)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[key] = value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving setting '{key}': {ex.Message}");
            }
        }

        #endregion
    }
}