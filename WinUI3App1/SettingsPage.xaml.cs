using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WinUI3App1
{
    /// <summary>
    /// Complete settings page for the Photo Booth application
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        // Settings Properties with default values

        // UI/Look and Feel
        public string BackgroundImagePath { get; set; } = "";
        public int PhotoStripLayoutIndex { get; set; } = 0;
        public string PhotoStripTemplatePath { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 60;

        // Functionality
        public bool EnablePhotos { get; set; } = true;
        public bool EnableVideos { get; set; } = false;
        public bool EnablePrinting { get; set; } = true;
        public bool ShowPrinterWarnings { get; set; } = true;
        public string SelectedPrinter { get; set; } = "";

        // Lighting
        public int InternalLedsMinimum { get; set; } = 20;
        public int InternalLedsMaximum { get; set; } = 100;
        public int ExternalDmxMinimum { get; set; } = 10;
        public int ExternalDmxMaximum { get; set; } = 80;
        public string SelectedComPort { get; set; } = "";

        // Original values to track changes
        private Dictionary<string, object> _originalValues = new Dictionary<string, object>();

        // List of available COM ports and printers
        private ObservableCollection<string> _availableComPorts = new ObservableCollection<string>();
        private ObservableCollection<string> _availablePrinters = new ObservableCollection<string>();

        public SettingsPage()
        {
            this.InitializeComponent();

            // Register value converter for percentages
            Resources.Add("IntToPercentConverter", new IntToPercentConverter());
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Load settings when page is navigated to
            LoadSettings();

            // Store original values to detect changes
            StoreOriginalValues();

            // Initialize controls with current settings
            InitializeControls();
        }

        private void LoadSettings()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // UI/Look and Feel
                BackgroundImagePath = localSettings.Values["BackgroundImagePath"] as string ?? "";
                PhotoStripLayoutIndex = GetSetting(localSettings, "PhotoStripLayoutIndex", 0);
                PhotoStripTemplatePath = localSettings.Values["PhotoStripTemplatePath"] as string ?? "";
                TimeoutSeconds = GetSetting(localSettings, "TimeoutSeconds", 60);

                // Functionality
                EnablePhotos = GetSetting(localSettings, "EnablePhotos", true);
                EnableVideos = GetSetting(localSettings, "EnableVideos", false);
                EnablePrinting = GetSetting(localSettings, "EnablePrinting", true);
                ShowPrinterWarnings = GetSetting(localSettings, "ShowPrinterWarnings", true);
                SelectedPrinter = localSettings.Values["SelectedPrinter"] as string ?? "";

                // Lighting
                InternalLedsMinimum = GetSetting(localSettings, "InternalLedsMinimum", 20);
                InternalLedsMaximum = GetSetting(localSettings, "InternalLedsMaximum", 100);
                ExternalDmxMinimum = GetSetting(localSettings, "ExternalDmxMinimum", 10);
                ExternalDmxMaximum = GetSetting(localSettings, "ExternalDmxMaximum", 80);
                SelectedComPort = localSettings.Values["SelectedComPort"] as string ?? "";

                Debug.WriteLine("Settings loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
                // Use defaults if loading fails
            }
        }

        private T GetSetting<T>(ApplicationDataContainer settings, string key, T defaultValue)
        {
            if (settings.Values.TryGetValue(key, out object value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        private void StoreOriginalValues()
        {
            _originalValues["BackgroundImagePath"] = BackgroundImagePath;
            _originalValues["PhotoStripLayoutIndex"] = PhotoStripLayoutIndex;
            _originalValues["PhotoStripTemplatePath"] = PhotoStripTemplatePath;
            _originalValues["TimeoutSeconds"] = TimeoutSeconds;
            _originalValues["EnablePhotos"] = EnablePhotos;
            _originalValues["EnableVideos"] = EnableVideos;
            _originalValues["EnablePrinting"] = EnablePrinting;
            _originalValues["ShowPrinterWarnings"] = ShowPrinterWarnings;
            _originalValues["SelectedPrinter"] = SelectedPrinter;
            _originalValues["InternalLedsMinimum"] = InternalLedsMinimum;
            _originalValues["InternalLedsMaximum"] = InternalLedsMaximum;
            _originalValues["ExternalDmxMinimum"] = ExternalDmxMinimum;
            _originalValues["ExternalDmxMaximum"] = ExternalDmxMaximum;
            _originalValues["SelectedComPort"] = SelectedComPort;
        }

        private async void InitializeControls()
        {
            // Load background image preview if available
            if (!string.IsNullOrEmpty(BackgroundImagePath) && File.Exists(BackgroundImagePath))
            {
                await LoadBackgroundPreview(BackgroundImagePath);
            }

            // Load available COM ports
            await RefreshComPorts();

            // Load available printers
            RefreshPrinters();

            // Select the currently configured COM port
            if (!string.IsNullOrEmpty(SelectedComPort) && _availableComPorts.Contains(SelectedComPort))
            {
                ComPortComboBox.SelectedItem = SelectedComPort;
            }
            else if (_availableComPorts.Count > 0)
            {
                ComPortComboBox.SelectedIndex = 0;
                SelectedComPort = ComPortComboBox.SelectedItem as string;
            }

            // Select the currently configured printer
            if (!string.IsNullOrEmpty(SelectedPrinter) && _availablePrinters.Contains(SelectedPrinter))
            {
                PrinterSelectionComboBox.SelectedItem = SelectedPrinter;
            }
            else if (_availablePrinters.Count > 0)
            {
                PrinterSelectionComboBox.SelectedIndex = 0;
                SelectedPrinter = PrinterSelectionComboBox.SelectedItem as string;
            }
        }

        private async Task LoadBackgroundPreview(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(imagePath))
                {
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                }
                BackgroundPreviewImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading background preview: {ex.Message}");
                BackgroundPreviewImage.Source = null;
            }
        }

        private async Task RefreshComPorts()
        {
            _availableComPorts.Clear();

            try
            {
                var ports = SerialPort.GetPortNames();
                foreach (var port in ports.OrderBy(p => p))
                {
                    _availableComPorts.Add(port);
                }

                if (_availableComPorts.Count == 0)
                {
                    _availableComPorts.Add("No COM Ports Available");
                }

                ComPortComboBox.ItemsSource = _availableComPorts;

                Debug.WriteLine($"Found {_availableComPorts.Count} COM ports");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing COM ports: {ex.Message}");
                _availableComPorts.Add("Error retrieving COM ports");
                ComPortComboBox.ItemsSource = _availableComPorts;
            }
        }

        private void RefreshPrinters()
        {
            _availablePrinters.Clear();

            try
            {
                // This is a placeholder - in a real app, you'd use the Windows printing APIs
                // to get the list of printers
                _availablePrinters.Add("Default Printer");
                _availablePrinters.Add("Microsoft Print to PDF");
                _availablePrinters.Add("Microsoft XPS Document Writer");

                PrinterSelectionComboBox.ItemsSource = _availablePrinters;

                Debug.WriteLine($"Found {_availablePrinters.Count} printers");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing printers: {ex.Message}");
                _availablePrinters.Add("Error retrieving printers");
                PrinterSelectionComboBox.ItemsSource = _availablePrinters;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // UI/Look and Feel
                localSettings.Values["BackgroundImagePath"] = BackgroundImagePath;
                localSettings.Values["PhotoStripLayoutIndex"] = PhotoStripLayoutIndex;
                localSettings.Values["PhotoStripTemplatePath"] = PhotoStripTemplatePath;
                localSettings.Values["TimeoutSeconds"] = TimeoutSeconds;

                // Functionality
                localSettings.Values["EnablePhotos"] = EnablePhotos;
                localSettings.Values["EnableVideos"] = EnableVideos;
                localSettings.Values["EnablePrinting"] = EnablePrinting;
                localSettings.Values["ShowPrinterWarnings"] = ShowPrinterWarnings;
                localSettings.Values["SelectedPrinter"] = SelectedPrinter;

                // Lighting
                localSettings.Values["InternalLedsMinimum"] = InternalLedsMinimum;
                localSettings.Values["InternalLedsMaximum"] = InternalLedsMaximum;
                localSettings.Values["ExternalDmxMinimum"] = ExternalDmxMinimum;
                localSettings.Values["ExternalDmxMaximum"] = ExternalDmxMaximum;
                localSettings.Values["SelectedComPort"] = SelectedComPort;

                Debug.WriteLine("Settings saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
                // Handle save error
            }
        }

        private bool HasSettingsChanged()
        {
            return
                BackgroundImagePath != (string)_originalValues["BackgroundImagePath"] ||
                PhotoStripLayoutIndex != (int)_originalValues["PhotoStripLayoutIndex"] ||
                PhotoStripTemplatePath != (string)_originalValues["PhotoStripTemplatePath"] ||
                TimeoutSeconds != (int)_originalValues["TimeoutSeconds"] ||
                EnablePhotos != (bool)_originalValues["EnablePhotos"] ||
                EnableVideos != (bool)_originalValues["EnableVideos"] ||
                EnablePrinting != (bool)_originalValues["EnablePrinting"] ||
                ShowPrinterWarnings != (bool)_originalValues["ShowPrinterWarnings"] ||
                SelectedPrinter != (string)_originalValues["SelectedPrinter"] ||
                InternalLedsMinimum != (int)_originalValues["InternalLedsMinimum"] ||
                InternalLedsMaximum != (int)_originalValues["InternalLedsMaximum"] ||
                ExternalDmxMinimum != (int)_originalValues["ExternalDmxMinimum"] ||
                ExternalDmxMaximum != (int)_originalValues["ExternalDmxMaximum"] ||
                SelectedComPort != (string)_originalValues["SelectedComPort"];
        }

        private bool ValidateSettings()
        {
            bool isValid = true;

            // Ensure at least one media type is enabled
            if (!EnablePhotos && !EnableVideos)
            {
                isValid = false;
                MediaWarningText.Visibility = Visibility.Visible;
            }
            else
            {
                MediaWarningText.Visibility = Visibility.Collapsed;
            }

            // Ensure internal LED range is valid
            if (InternalLedsMinimum > InternalLedsMaximum)
            {
                isValid = false;
                InternalLedsMinSlider.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            else
            {
                InternalLedsMinSlider.Background = null;
            }

            // Ensure external DMX range is valid
            if (ExternalDmxMinimum > ExternalDmxMaximum)
            {
                isValid = false;
                ExternalDmxMinSlider.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            else
            {
                ExternalDmxMinSlider.Background = null;
            }

            return isValid;
        }

        //
        // Event Handlers
        //

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasSettingsChanged())
            {
                ShowUnsavedChangesDialog();
            }
            else
            {
                Frame.GoBack();
            }
        }

        private async void BrowseBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            filePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

            // Fix: Use the current window's handle instead of Application.Current.MainWindow  
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(filePicker, hwnd);

            var file = await filePicker.PickSingleFileAsync();

            if (file != null)
            {
                BackgroundImagePath = file.Path;
                BackgroundImagePathTextBox.Text = file.Path;

                await LoadBackgroundPreview(file.Path);
            }
        }

        private async void BrowseTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            filePicker.FileTypeFilter.Add(".psd");
            filePicker.FileTypeFilter.Add(".svg");
            filePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(filePicker, hwnd);

            var file = await filePicker.PickSingleFileAsync();

            if (file != null)
            {
                PhotoStripTemplatePath = file.Path;
                PhotoStripTemplateTextBox.Text = file.Path;
            }
        }

        private void ResetBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            BackgroundImagePath = "";
            BackgroundImagePathTextBox.Text = "";
            BackgroundPreviewImage.Source = null;
        }

        private void MediaOption_Changed(object sender, RoutedEventArgs e)
        {
            // Update warning visibility
            if (!EnablePhotos && !EnableVideos)
            {
                MediaWarningText.Visibility = Visibility.Visible;
            }
            else
            {
                MediaWarningText.Visibility = Visibility.Collapsed;
            }
        }

        private void PrinterSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PrinterSelectionComboBox.SelectedItem != null)
            {
                SelectedPrinter = PrinterSelectionComboBox.SelectedItem.ToString();
            }
        }

        private void ComPortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComPortComboBox.SelectedItem != null)
            {
                SelectedComPort = ComPortComboBox.SelectedItem.ToString();
            }
        }

        private async void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPortsButton.IsEnabled = false;
            await RefreshComPorts();
            RefreshPortsButton.IsEnabled = true;
        }

        private void TestLightsButton_Click(object sender, RoutedEventArgs e)
        {
            // This would integrate with your actual lighting control code
            Debug.WriteLine($"Testing lights on {SelectedComPort}");
            Debug.WriteLine($"Internal LEDs: {InternalLedsMinimum}% - {InternalLedsMaximum}%");
            Debug.WriteLine($"External DMX: {ExternalDmxMinimum}% - {ExternalDmxMaximum}%");

            // Show a success message
            var notification = new ContentDialog()
            {
                Title = "Test Lights",
                Content = "Light test signal sent. Check if the lights are responding.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            _ = notification.ShowAsync();
        }

        private async void ShowUnsavedChangesDialog()
        {
            var dialog = new ContentDialog()
            {
                Title = "Unsaved Changes",
                Content = "You have unsaved changes. Do you want to save them before leaving?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Discard",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            switch (result)
            {
                case ContentDialogResult.Primary:
                    // Save changes
                    if (ValidateSettings())
                    {
                        SaveSettings();
                        Frame.GoBack();
                    }
                    break;
                case ContentDialogResult.Secondary:
                    // Discard changes
                    Frame.GoBack();
                    break;
                case ContentDialogResult.None:
                    // Cancel navigation
                    break;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateSettings())
            {
                SaveSettings();
                Frame.GoBack();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasSettingsChanged())
            {
                ShowUnsavedChangesDialog();
            }
            else
            {
                Frame.GoBack();
            }
        }

        // Add this method to your SettingsPage.xaml.cs file
        // Place it with your other event handlers

        private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the path to the logs directory
                string logsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Logs");

                // Ensure the directory exists
                if (!System.IO.Directory.Exists(logsDirectory))
                {
                    System.IO.Directory.CreateDirectory(logsDirectory);
                }

                // Log that the logs directory is being opened
                App.Logger?.Information("Opening logs directory: {LogsDirectory}", logsDirectory);

                // Open the directory in File Explorer
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = logsDirectory,
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                // Log the error
                App.Logger?.Error(ex, "Error opening logs directory");

                // Show error message to user
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = "Could not open logs directory. " + ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };

                _ = dialog.ShowAsync();
            }
        }

    }

    /// <summary>
    /// Converter to display integer values as percentages
    /// </summary>
    public class IntToPercentConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int intValue)
            {
                return $"{intValue}%";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string stringValue && stringValue.EndsWith("%"))
            {
                if (int.TryParse(stringValue.TrimEnd('%'), out int result))
                {
                    return result;
                }
            }
            return value;
        }
    }

}