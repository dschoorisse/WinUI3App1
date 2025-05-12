// SettingsManager.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage; // For ApplicationData.Current.LocalFolder

namespace WinUI3App1 // Ensure this namespace matches your project
{
    public static class SettingsManager
    {
        private const string SETTINGS_FILENAME = "photobooth_settings.json";
        private static readonly string FilePath;

        static SettingsManager()
        {
            // Ensure LocalFolder path is available. This might require the app to be packaged
            // or have appropriate permissions if unpackaged for LocalFolder access.
            // For unpackaged apps, AppContext.BaseDirectory might be more reliable for a config file
            // you intend to ship or allow easy user access to. Let's stick with LocalFolder for app-specific data.
            try
            {
                FilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, SETTINGS_FILENAME);
            }
            catch (Exception ex) // IOException can occur if LocalFolder is not accessible (e.g. very early in unpackaged app init)
            {
                Debug.WriteLine($"Error getting LocalFolder path for settings: {ex.Message}. Falling back to BaseDirectory.");
                // Fallback for unpackaged apps or if LocalFolder is problematic:
                FilePath = Path.Combine(AppContext.BaseDirectory, SETTINGS_FILENAME);
            }
        }

        public static async Task<PhotoBoothSettings> LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    
                    string json = await File.ReadAllTextAsync(FilePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var settings = JsonSerializer.Deserialize<PhotoBoothSettings>(json);
                        if (settings != null)
                        {
                            // If LastModifiedUtc was missing from an old JSON, it might be DateTime.MinValue
                            // The PhotoBoothSettings constructor now sets it to UtcNow for new instances.
                            // If loaded and it's MinValue, could update it to file's last write time or now.
                            // For simplicity, constructor handles new instances. Loaded instances will have their value.
                            Debug.WriteLine($"SettingsManager: Settings loaded successfully from {FilePath}. Timestamp: {settings.LastModifiedUtc}");
                            return settings;
                        }
                    }
                }
                Debug.WriteLine($"Settings file not found or empty at {FilePath}. Creating with defaults.");
            }
            catch (JsonException jsonEx)
            {
                Debug.WriteLine($"Error deserializing settings from JSON at {FilePath}: {jsonEx.Message}. Using default settings and overwriting.");
                // Optionally, back up the corrupted file before overwriting
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings from {FilePath}: {ex.Message}. Using default settings.");
            }

            // If file doesn't exist, is empty, or deserialization fails, return new defaults
            // and save them for next time.
            var defaultSettings = new PhotoBoothSettings();
            await SaveSettingsAsync(defaultSettings);
            return defaultSettings;
        }

        public static async Task SaveSettingsAsync(PhotoBoothSettings settings, bool isFromRemoteUpdate = false)
        {
            if (settings == null)
            {
                Debug.WriteLine("SettingsManager: Attempted to save null settings. Operation aborted.");
                return;
            }

            if (!isFromRemoteUpdate) // If changes are from local UI (SettingsPage)
            {
                settings.LastModifiedUtc = DateTime.UtcNow; // Update timestamp to now
                Debug.WriteLine($"SettingsManager: Local save. Updating LastModifiedUtc to: {settings.LastModifiedUtc}");
            }
            else // If from remote, the timestamp in 'settings' object is the one from the server
            {
                Debug.WriteLine($"SettingsManager: Remote save. Preserving LastModifiedUtc: {settings.LastModifiedUtc}");
            }

            try
            {
                // ... (directory creation, serialization, File.WriteAllTextAsync as before) ...
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(FilePath, json);
                Debug.WriteLine($"SettingsManager: Settings saved to {FilePath}");

            }
            catch (Exception ex) { /* ... log error ... */ }
        }
    }
}