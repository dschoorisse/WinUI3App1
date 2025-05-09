// SettingsPage.xaml.cs
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
using Windows.Storage.Pickers;
using WinRT.Interop;

// Ensure this namespace matches your project, e.g., WinUI3App1
namespace WinUI3App1
{
    public sealed partial class SettingsPage : Page
    {
        // These properties will now be populated from and save to the PhotoBoothSettings model
        // They remain public for data binding to your XAML controls if you use {x:Bind}.

        // UI/Look and Feel
        public string BackgroundImagePath { get; set; }
        public int PhotoStripLayoutIndex { get; set; }
        public string PhotoStripTemplatePath { get; set; }
        public int TimeoutSeconds { get; set; }

        // Functionality
        public bool EnablePhotos { get; set; }
        public bool EnableVideos { get; set; }
        public bool EnablePrinting { get; set; }
        public bool ShowPrinterWarnings { get; set; }
        public string SelectedPrinter { get; set; }

        // Lighting
        public int InternalLedsMinimum { get; set; }
        public int InternalLedsMaximum { get; set; }
        public int ExternalDmxMinimum { get; set; }
        public int ExternalDmxMaximum { get; set; }
        public string SelectedComPort { get; set; }

        // This will hold the settings loaded from/to be saved to JSON
        private PhotoBoothSettings _loadedSettingsModel;

        // Original values to track changes against UI properties
        private Dictionary<string, object> _originalValues = new Dictionary<string, object>();

        // Lists for UI controls (ComboBoxes)
        private ObservableCollection<string> _availableComPorts = new ObservableCollection<string>();
        private ObservableCollection<string> _availablePrinters = new ObservableCollection<string>();

        public SettingsPage()
        {
            this.InitializeComponent();
            // Assuming IntToPercentConverter is defined elsewhere or in XAML resources
            // If it's defined in C# within this file, ensure it's correctly placed.
            // If not already in XAML resources: Resources.Add("IntToPercentConverter", new IntToPercentConverter());
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadSettingsAsync();    // Load from JSON into _loadedSettingsModel and then to page properties
            StoreOriginalValues();        // Store these loaded values for change tracking
            await InitializeControlsAsync(); // Initialize UI with loaded values (made async for safety)
        }

        private async Task LoadSettingsAsync()
        {
            Debug.WriteLine("SettingsPage: Loading settings from JSON...");
            _loadedSettingsModel = await SettingsManager.LoadSettingsAsync();

            // Populate page properties from the loaded settings model
            // These properties are what your XAML might be binding to.
            BackgroundImagePath = _loadedSettingsModel.BackgroundImagePath;
            PhotoStripLayoutIndex = _loadedSettingsModel.PhotoStripLayoutIndex;
            PhotoStripTemplatePath = _loadedSettingsModel.PhotoStripTemplatePath;
            TimeoutSeconds = _loadedSettingsModel.TimeoutSeconds;

            EnablePhotos = _loadedSettingsModel.EnablePhotos;
            EnableVideos = _loadedSettingsModel.EnableVideos;
            EnablePrinting = _loadedSettingsModel.EnablePrinting;
            ShowPrinterWarnings = _loadedSettingsModel.ShowPrinterWarnings;
            SelectedPrinter = _loadedSettingsModel.SelectedPrinter;

            InternalLedsMinimum = _loadedSettingsModel.InternalLedsMinimum;
            InternalLedsMaximum = _loadedSettingsModel.InternalLedsMaximum;
            ExternalDmxMinimum = _loadedSettingsModel.ExternalDmxMinimum;
            ExternalDmxMaximum = _loadedSettingsModel.ExternalDmxMaximum;
            SelectedComPort = _loadedSettingsModel.SelectedComPort;

            Debug.WriteLine("SettingsPage: Properties populated from JSON model.");
        }

        private void StoreOriginalValues()
        {
            _originalValues.Clear();
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
            Debug.WriteLine("SettingsPage: Original values stored for change detection.");
        }

        private async Task InitializeControlsAsync()
        {
            // This method ensures UI controls reflect the loaded settings.
            // If using {x:Bind Path=MyProperty, Mode=TwoWay} in XAML, many explicit updates aren't needed
            // as setting the public properties in LoadSettingsAsync would trigger UI updates.
            // However, for ComboBox selections and Image previews, explicit setup is good.

            // Update XAML elements if they aren't automatically updated by {x:Bind Mode=TwoWay}
            // Example: BackgroundImagePathTextBox.Text = BackgroundImagePath; (if you have this textbox)
            // PhotoStripTemplateTextBox.Text = PhotoStripTemplatePath;
            // EnablePhotosToggle.IsOn = EnablePhotos; 
            // TimeoutValueTextBlock.Text = TimeoutSeconds.ToString(); // Or bind slider value

            if (!string.IsNullOrEmpty(BackgroundImagePath) && File.Exists(BackgroundImagePath))
            {
                await LoadBackgroundPreview(BackgroundImagePath);
            }
            else
            {
                // Assuming BackgroundPreviewImage is the name of your Image control in XAML
                if (this.FindName("BackgroundPreviewImage") is Image img) img.Source = null;
            }

            await RefreshComPortsAsync();
            RefreshPrinters();

            ComPortComboBox.ItemsSource = _availableComPorts; // Set ItemsSource
            if (!string.IsNullOrEmpty(SelectedComPort) && _availableComPorts.Contains(SelectedComPort))
                ComPortComboBox.SelectedItem = SelectedComPort;
            else if (_availableComPorts.Any() && _availableComPorts.FirstOrDefault(p => !p.ToLower().Contains("error") && !p.ToLower().Contains("no com")) != null)
                ComPortComboBox.SelectedItem = _availableComPorts.FirstOrDefault(p => !p.ToLower().Contains("error") && !p.ToLower().Contains("no com"));


            PrinterSelectionComboBox.ItemsSource = _availablePrinters; // Set ItemsSource
            if (!string.IsNullOrEmpty(SelectedPrinter) && _availablePrinters.Contains(SelectedPrinter))
                PrinterSelectionComboBox.SelectedItem = SelectedPrinter;
            else if (_availablePrinters.Any() && _availablePrinters.FirstOrDefault(p => !p.ToLower().Contains("error")) != null)
                PrinterSelectionComboBox.SelectedItem = _availablePrinters.FirstOrDefault(p => !p.ToLower().Contains("error"));

            // For sliders and toggle switches, if you are not using TwoWay x:Bind, you'd set them here:
            // Example: InternalLedsMinSlider.Value = InternalLedsMinimum;
            // EnablePhotosToggleSwitch.IsOn = EnablePhotos; // Assuming 'EnablePhotosToggleSwitch' is the x:Name
        }

        private async Task SaveSettingsAsync()
        {
            if (_loadedSettingsModel == null)
            {
                Debug.WriteLine("SettingsPage: Cannot save, settings model was not loaded. Creating a new one.");
                _loadedSettingsModel = new PhotoBoothSettings();
            }

            // Update the _loadedSettingsModel with current values from page properties (which are bound to UI)
            _loadedSettingsModel.BackgroundImagePath = BackgroundImagePath;
            _loadedSettingsModel.PhotoStripLayoutIndex = PhotoStripLayoutIndex;
            _loadedSettingsModel.PhotoStripTemplatePath = PhotoStripTemplatePath;
            _loadedSettingsModel.TimeoutSeconds = TimeoutSeconds;
            _loadedSettingsModel.EnablePhotos = EnablePhotos;
            _loadedSettingsModel.EnableVideos = EnableVideos;
            _loadedSettingsModel.EnablePrinting = EnablePrinting;
            _loadedSettingsModel.ShowPrinterWarnings = ShowPrinterWarnings;
            _loadedSettingsModel.SelectedPrinter = SelectedPrinter; // Ensure this comes from ComboBox selected value if not directly bound
            _loadedSettingsModel.InternalLedsMinimum = InternalLedsMinimum;
            _loadedSettingsModel.InternalLedsMaximum = InternalLedsMaximum;
            _loadedSettingsModel.ExternalDmxMinimum = ExternalDmxMinimum;
            _loadedSettingsModel.ExternalDmxMaximum = ExternalDmxMaximum;
            _loadedSettingsModel.SelectedComPort = SelectedComPort; // Ensure this comes from ComboBox selected value

            // Settings not on UI (e.g., MQTT, PhotoboothId from the model) will retain their previously loaded values
            // unless the model was just newed up (in which case they are defaults).
            // If you want PhotoboothId to be editable, you'd add a UI element and property for it.

            await SettingsManager.SaveSettingsAsync(_loadedSettingsModel);
            Debug.WriteLine("SettingsPage: Settings saved to JSON via SettingsManager.");
            StoreOriginalValues(); // Update original values to reflect the newly saved state
        }

        private bool HasSettingsChanged()
        {
            // This compares current UI-bound properties against the state when the page was loaded/last saved
            if (_originalValues.Count == 0) return false; // Nothing to compare against yet

            return BackgroundImagePath != (_originalValues["BackgroundImagePath"] as string ?? "") ||
                   PhotoStripLayoutIndex != (int)_originalValues["PhotoStripLayoutIndex"] ||
                   (PhotoStripTemplatePath ?? "") != (_originalValues["PhotoStripTemplatePath"] as string ?? "") ||
                   TimeoutSeconds != (int)_originalValues["TimeoutSeconds"] ||
                   EnablePhotos != (bool)_originalValues["EnablePhotos"] ||
                   EnableVideos != (bool)_originalValues["EnableVideos"] ||
                   EnablePrinting != (bool)_originalValues["EnablePrinting"] ||
                   ShowPrinterWarnings != (bool)_originalValues["ShowPrinterWarnings"] ||
                   (SelectedPrinter ?? "") != (_originalValues["SelectedPrinter"] as string ?? "") ||
                   InternalLedsMinimum != (int)_originalValues["InternalLedsMinimum"] ||
                   InternalLedsMaximum != (int)_originalValues["InternalLedsMaximum"] ||
                   ExternalDmxMinimum != (int)_originalValues["ExternalDmxMinimum"] ||
                   ExternalDmxMaximum != (int)_originalValues["ExternalDmxMaximum"] ||
                   (SelectedComPort ?? "") != (_originalValues["SelectedComPort"] as string ?? "");
        }

        private bool ValidateSettings()
        {
            bool isValid = true;
            var mediaWarningText = this.FindName("MediaWarningText") as TextBlock; // Assuming x:Name in XAML
            var internalLedsMinSlider = this.FindName("InternalLedsMinSlider") as Slider; // Assuming x:Name
            var externalDmxMinSlider = this.FindName("ExternalDmxMinSlider") as Slider; // Assuming x:Name

            if (!EnablePhotos && !EnableVideos)
            {
                isValid = false;
                if (mediaWarningText != null) mediaWarningText.Visibility = Visibility.Visible;
            }
            else
            {
                if (mediaWarningText != null) mediaWarningText.Visibility = Visibility.Collapsed;
            }

            if (InternalLedsMinimum > InternalLedsMaximum)
            {
                isValid = false;
                if (internalLedsMinSlider != null) internalLedsMinSlider.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            else
            {
                if (internalLedsMinSlider != null) internalLedsMinSlider.Background = null;
            }

            if (ExternalDmxMinimum > ExternalDmxMaximum)
            {
                isValid = false;
                if (externalDmxMinSlider != null) externalDmxMinSlider.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            else
            {
                if (externalDmxMinSlider != null) externalDmxMinSlider.Background = null;
            }
            return isValid;
        }

        // --- Event Handlers for UI elements ---
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
                    if (ValidateSettings()) { await SaveSettingsAsync(); Frame.GoBack(); }
                    break;
                case ContentDialogResult.Secondary: Frame.GoBack(); break;
                case ContentDialogResult.None: break; // Stay on page
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateSettings())
            {
                await SaveSettingsAsync();
                // Optionally provide feedback like "Settings Saved!"
                // For now, just go back or indicate saved state.
                if (Frame.CanGoBack) Frame.GoBack();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasSettingsChanged()) { ShowUnsavedChangesDialog(); }
            else { if (Frame.CanGoBack) Frame.GoBack(); }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasSettingsChanged()) { ShowUnsavedChangesDialog(); }
            else { if (Frame.CanGoBack) Frame.GoBack(); }
        }

        private async void BrowseBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(filePicker, hwnd);
            var file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                BackgroundImagePath = file.Path;
                // If you have a TextBox named BackgroundImagePathTextBox in your XAML:
                if (this.FindName("BackgroundImagePathTextBox") is TextBox bipTb) bipTb.Text = file.Path;
                await LoadBackgroundPreview(file.Path);
            }
        }

        private async void BrowseTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            filePicker.FileTypeFilter.Add(".png"); filePicker.FileTypeFilter.Add(".jpg"); filePicker.FileTypeFilter.Add(".jpeg");
            filePicker.FileTypeFilter.Add(".psd"); filePicker.FileTypeFilter.Add(".svg");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(filePicker, hwnd);
            var file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                PhotoStripTemplatePath = file.Path;
                // If you have a TextBox named PhotoStripTemplateTextBox in your XAML:
                if (this.FindName("PhotoStripTemplateTextBox") is TextBox pstTb) pstTb.Text = file.Path;
            }
        }

        private void ResetBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            BackgroundImagePath = "";
            if (this.FindName("BackgroundImagePathTextBox") is TextBox bipTb) bipTb.Text = "";
            if (this.FindName("BackgroundPreviewImage") is Image img) img.Source = null;
        }

        // This assumes your XAML has ToggleSwitches or CheckBoxes bound to EnablePhotos/EnableVideos
        // If not, this event handler might be tied to specific controls.
        private void MediaOption_Changed(object sender, RoutedEventArgs e)
        {
            // This logic is now part of ValidateSettings, but can be called on change too
            var mediaWarningText = this.FindName("MediaWarningText") as TextBlock;
            if (mediaWarningText != null)
            {
                mediaWarningText.Visibility = (!EnablePhotos && !EnableVideos) ? Visibility.Visible : Visibility.Collapsed;
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
            if (sender is Button btn) btn.IsEnabled = false;
            await RefreshComPortsAsync();
            if (sender is Button btn2) btn2.IsEnabled = true;
        }

        private void TestLightsButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"Testing lights on {SelectedComPort}");
            Debug.WriteLine($"Internal LEDs: {InternalLedsMinimum}% - {InternalLedsMaximum}%");
            Debug.WriteLine($"External DMX: {ExternalDmxMinimum}% - {ExternalDmxMaximum}%");
            var notification = new ContentDialog()
            {
                Title = "Test Lights",
                Content = "Light test signal sent. Check lights.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = notification.ShowAsync();
        }

        private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
        { /* ... (as before) ... */
            try
            {
                string logsDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
                if (!Directory.Exists(logsDirectory)) { Directory.CreateDirectory(logsDirectory); }
                // App.Logger?.Information("Opening logs directory: {LogsDirectory}", logsDirectory);
                Debug.WriteLine($"Opening logs directory: {logsDirectory}"); // Using Debug.WriteLine if App.Logger isn't set up yet
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = logsDirectory, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // App.Logger?.Error(ex, "Error opening logs directory");
                Debug.WriteLine($"Error opening logs directory: {ex.Message}");
                var dialog = new ContentDialog { Title = "Error", Content = "Could not open logs directory. " + ex.Message, CloseButtonText = "OK", XamlRoot = this.XamlRoot };
                _ = dialog.ShowAsync();
            }
        }

        // --- Helper methods for UI ---
        private async Task LoadBackgroundPreview(string imagePath)
        {
            var backgroundPreviewImage = this.FindName("BackgroundPreviewImage") as Image;
            if (backgroundPreviewImage == null) return;
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(imagePath))
                {
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                }
                backgroundPreviewImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading background preview: {ex.Message}");
                backgroundPreviewImage.Source = null;
            }
        }

        private async Task RefreshComPortsAsync()
        { // Renamed to Async
            _availableComPorts.Clear();
            try
            {
                var ports = await Task.Run(() => SerialPort.GetPortNames()); // Run on background thread
                foreach (var port in ports.OrderBy(p => p)) { _availableComPorts.Add(port); }
                if (_availableComPorts.Count == 0) { _availableComPorts.Add("No COM Ports Available"); }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing COM ports: {ex.Message}");
                _availableComPorts.Add("Error retrieving COM ports");
            }
            // ComPortComboBox.ItemsSource = _availableComPorts; // Set in InitializeControls
        }

        private void RefreshPrinters()
        {
            _availablePrinters.Clear();
            try
            {
                // Placeholder - replace with actual printer discovery logic if needed
                _availablePrinters.Add("Default Printer");
                _availablePrinters.Add("Microsoft Print to PDF");
                _availablePrinters.Add("Microsoft XPS Document Writer");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing printers: {ex.Message}");
                _availablePrinters.Add("Error retrieving printers");
            }
            // PrinterSelectionComboBox.ItemsSource = _availablePrinters; // Set in InitializeControls
        }

    } // End of SettingsPage class

    // Ensure IntToPercentConverter is correctly defined, possibly in its own file or here if simple enough
    // public class IntToPercentConverter : Microsoft.UI.Xaml.Data.IValueConverter { /* ... (as defined in user's original file) ... */ }
}