// SettingsManager.cs
using Serilog;
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
        private const string SETTINGS_BACKUP_FILENAME_SUFFIX = ".bak"; 
        private static readonly string FilePath;

        static SettingsManager()
        {
             // Ensure LocalFolder path is available. This might require the app to be packaged
             // or have appropriate permissions if unpackaged for LocalFolder access.
             // For unpackaged apps, AppContext.BaseDirectory might be more reliable for a config file
             // you intend to ship or allow easy user access to. Let's stick with LocalFolder for app-specific data.
            try
            {
                // Attempt to get the LocalFolder path for storing settings  
                FilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, SETTINGS_FILENAME);
                App.Logger?.Debug($"SettingsManager: LocalFolder path resolved to {FilePath}");
            }
            catch (Exception ex)
            {
                // Log fallback to BaseDirectory in case of an error  
                App.Logger?.Debug($"Error getting LocalFolder path for settings: {ex.Message}. Falling back to BaseDirectory.");
                // Fallback for unpackaged apps or if LocalFolder is problematic:
                FilePath = Path.Combine(AppContext.BaseDirectory, SETTINGS_FILENAME);
                App.Logger?.Debug($"SettingsManager: Fallback path resolved to {FilePath}");
            }
        }

        public static async Task<PhotoBoothSettings> LoadSettingsAsync()
        {
            try
            {
                App.Logger?.Debug($"SettingsManager: Attempting to load settings from {FilePath}");

                if (File.Exists(FilePath))
                {
                    App.Logger?.Debug($"SettingsManager: Settings file found at {FilePath}");
                    string json = await File.ReadAllTextAsync(FilePath);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        App.Logger?.Debug("SettingsManager: Deserializing settings JSON");
                        var settings = JsonSerializer.Deserialize<PhotoBoothSettings>(json);

                        if (settings != null)
                        {
                            // If LastModifiedUtc was missing from an old JSON, it might be DateTime.MinValue
                            // The PhotoBoothSettings constructor now sets it to UtcNow for new instances.
                            // If loaded and it's MinValue, could update it to file's last write time or now.
                            return settings;
                        }
                        else
                        {
                            App.Logger?.Debug("SettingsManager: Deserialized settings object is null. Using default settings.");
                        }
                    }
                    else
                    {
                        App.Logger?.Debug("SettingsManager: Settings file is empty. Using default settings.");
                    }
                }
                else
                {
                    App.Logger?.Debug($"SettingsManager: Settings file not found at {FilePath}. Creating with defaults.");
                }
            }
            catch (JsonException jsonEx)
            {
                App.Logger?.Debug($"SettingsManager: JSON deserialization error: {jsonEx.Message}. Using default settings and overwriting.");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug($"SettingsManager: Error loading settings: {ex.Message}. Using default settings.");
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
                App.Logger?.Debug("SettingsManager: Attempted to save null settings. Operation aborted.");
                return;
            }

            // If changes are from local UI (SettingsPage)
            if (!isFromRemoteUpdate)
            {
                settings.LastModifiedUtc = DateTime.UtcNow;
                App.Logger?.Debug($"SettingsManager: Local save. Updating LastModifiedUtc to: {settings.LastModifiedUtc}");
            }
            else // If from remote, the timestamp in 'settings' object is the one from the server
            {
                App.Logger?.Debug($"SettingsManager: Remote save. Preserving LastModifiedUtc: {settings.LastModifiedUtc}");
            }

            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // --- Backup logic before overwriting with remote settings ---
                if (isFromRemoteUpdate && File.Exists(FilePath))
                {
                    string backupFilePath = FilePath + SETTINGS_BACKUP_FILENAME_SUFFIX;
                    try
                    {
                        File.Copy(FilePath, backupFilePath, true); // true to overwrite existing backup
                        App.Logger?.Debug($"SettingsManager: Backed up current settings to {backupFilePath}");
                        App.Logger?.Information("SettingsManager: Backed up current settings to {BackupFilePath} before applying remote update.", backupFilePath);
                    }
                    catch (Exception backupEx)
                    {
                        App.Logger?.Debug($"SettingsManager: Error creating settings backup: {backupEx.Message}");
                        App.Logger?.Error(backupEx, "SettingsManager: Failed to create settings backup before remote update.");
                        // Decide if you want to proceed with the save even if backup fails. Usually yes.
                    }
                }
                // --- End of Backup logic ---

                var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
                string json = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(FilePath, json);
                App.Logger?.Information("SettingsManager: Settings saved to {FilePath}. Effective Timestamp: {Timestamp}", FilePath, settings.LastModifiedUtc);

                // Trigger event that settings have been written (for reporting current state)
                OnSettingsWrittenToDisk?.Invoke(null, settings);
                App.Logger?.Debug("SettingsManager: OnSettingsWrittenToDisk event triggered.");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug($"SettingsManager: Error saving settings to {FilePath}: {ex.Message}");
            }
        }

        // Event to signal that settings have been successfully written to disk
        public static event EventHandler<PhotoBoothSettings> OnSettingsWrittenToDisk;
    }
}