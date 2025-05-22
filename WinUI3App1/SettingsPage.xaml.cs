// SettingsPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop; // Voor InitializeWithWindow

namespace WinUI3App1
{
    public sealed partial class SettingsPage : Page, INotifyPropertyChanged
    {
        // Event voor INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;


        // Dit model bevat alle instellingen en wordt gebruikt voor bindingen waar mogelijk
        // Properties die direct op de Page staan zijn voor complexere gevallen of waar TwoWay binding op het model lastig is.
        public PhotoBoothSettings LoadedSettingsModel { get; set; }

        // UI/Look and Feel (direct gebonden properties indien nodig)
        private string _backgroundImagePath;
        public string BackgroundImagePath
        {
            get => _backgroundImagePath;
            set
            {
                if (_backgroundImagePath != value)
                {
                    _backgroundImagePath = value;
                    OnPropertyChanged(); // Roep event aan
                                         // Update ook het TextBox veld als dat niet via TwoWay binding gebeurt
                                         // of als je zeker wilt zijn. Meestal is TwoWay binding voldoende.
                                         // if (BackgroundImagePathTextBox != null) BackgroundImagePathTextBox.Text = value;
                }
            }
        }

        public string PhotoStripFilePath { get; set; } // NIEUW
        public bool HorizontalReviewLayout { get; set; } // NIEUW
        public int ReviewPageTimeoutSeconds { get; set; }
        public int QrCodeTimeoutSeconds { get; set; } // NIEUW

        // Functionality
        public bool EnablePhotos { get; set; }
        public bool EnableVideos { get; set; }
        public bool EnablePrinting { get; set; }
        public bool EnableUploading { get; set; }
        public bool EnableShowQr { get; set; }
        public string HotFolderPath { get; set; } // NIEUW
        public string PhotoOutputPath { get; set; } // NIEUW
        public string DnpPrinterStatusFilePath { get; set; } // NIEUW

        // Lighting (nu onderdeel van External Equipment, maar kan hier blijven voor directe binding sliders)
        public int InternalLedsMinimum { get; set; }
        public int InternalLedsMaximum { get; set; }
        public int ExternalDmxMinimum { get; set; }
        public int ExternalDmxMaximum { get; set; }
        public int DmxStartAddress { get; set; } // NIEUW


        // Dictionary om originele waarden bij te houden voor "HasChanged" logica
        private Dictionary<string, object> _originalValues = new Dictionary<string, object>();


        // Helper methode om het event aan te roepen
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadedSettingsModel = new PhotoBoothSettings(); // Initialiseer om null reference te voorkomen vóór OnNavigatedTo
            this.DataContext = this; // Voor {x:Bind} zonder expliciet LoadedSettingsModel ervoor.

            // Register converter
            //Resources.Add("IntToPercentConverter", new IntToPercentConverter());
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadSettingsAsync();    // Laadt vanuit JSON naar LoadedSettingsModel en dan naar page properties
            StoreOriginalValues();        // Sla deze geladen waarden op voor wijzigingsdetectie
            InitializeControlsAsync(); // Initialiseer UI met geladen waarden
        }

        private async Task LoadSettingsAsync()
        {
            Debug.WriteLine("SettingsPage: Loading settings from JSON...");
            LoadedSettingsModel = await SettingsManager.LoadSettingsAsync();

            // Update de direct gebonden properties op de Page vanuit het LoadedSettingsModel
            // Dit is nodig als je in XAML direct bindt aan Page properties (bv. {x:Bind BackgroundImagePath})
            // en niet aan {x:Bind LoadedSettingsModel.BackgroundImagePath}
            BackgroundImagePath = LoadedSettingsModel.BackgroundImagePath;
            PhotoStripFilePath = LoadedSettingsModel.PhotoStripFilePath;
            ReviewPageTimeoutSeconds = LoadedSettingsModel.ReviewPageTimeoutSeconds;
            HorizontalReviewLayout = LoadedSettingsModel.HorizontalReviewLayout;
            QrCodeTimeoutSeconds = LoadedSettingsModel.QrCodeTimeoutSeconds;

            EnablePhotos = LoadedSettingsModel.EnablePhotos;
            EnableVideos = LoadedSettingsModel.EnableVideos;
            EnablePrinting = LoadedSettingsModel.EnablePrinting;
            EnableUploading = LoadedSettingsModel.EnableUploading;
            EnableShowQr = LoadedSettingsModel.EnableShowQr;
            HotFolderPath = LoadedSettingsModel.HotFolderPath;
            PhotoOutputPath = LoadedSettingsModel.PhotoOutputPath;
            DnpPrinterStatusFilePath = LoadedSettingsModel.DnpPrinterStatusFilePath;

            InternalLedsMinimum = LoadedSettingsModel.InternalLedsMinimum;
            InternalLedsMaximum = LoadedSettingsModel.InternalLedsMaximum;
            ExternalDmxMinimum = LoadedSettingsModel.ExternalDmxMinimum;
            ExternalDmxMaximum = LoadedSettingsModel.ExternalDmxMaximum;
            DmxStartAddress = LoadedSettingsModel.DmxStartAddress;

            Debug.WriteLine("SettingsPage: Properties populated from JSON model.");
        }

        private void StoreOriginalValues()
        {
            _originalValues.Clear();
            if (LoadedSettingsModel == null) return;

            _originalValues["BackgroundImagePath"] = LoadedSettingsModel.BackgroundImagePath;
            _originalValues["PhotoStripFilePath"] = LoadedSettingsModel.PhotoStripFilePath;
            _originalValues["HorizontalReviewLayout"] = LoadedSettingsModel.HorizontalReviewLayout;
            _originalValues["ReviewPageTimeoutSeconds"] = LoadedSettingsModel.ReviewPageTimeoutSeconds;
            _originalValues["QrCodeTimeoutSeconds"] = LoadedSettingsModel.QrCodeTimeoutSeconds;

            _originalValues["EnableUploading"] = LoadedSettingsModel.EnableUploading;
            _originalValues["EnableShowQr"] = LoadedSettingsModel.EnableShowQr;
            _originalValues["EnablePhotos"] = LoadedSettingsModel.EnablePhotos;
            _originalValues["EnableVideos"] = LoadedSettingsModel.EnableVideos;
            _originalValues["EnablePrinting"] = LoadedSettingsModel.EnablePrinting;
            _originalValues["HotFolderPath"] = LoadedSettingsModel.HotFolderPath;
            _originalValues["DnpPrinterStatusFilePath"] = LoadedSettingsModel.DnpPrinterStatusFilePath;

            _originalValues["InternalLedsMinimum"] = LoadedSettingsModel.InternalLedsMinimum;
            _originalValues["InternalLedsMaximum"] = LoadedSettingsModel.InternalLedsMaximum;
            _originalValues["ExternalDmxMinimum"] = LoadedSettingsModel.ExternalDmxMinimum;
            _originalValues["ExternalDmxMaximum"] = LoadedSettingsModel.ExternalDmxMaximum;
            _originalValues["DmxStartAddress"] = LoadedSettingsModel.DmxStartAddress;

            // Sla ook waarden op voor de direct aan LoadedSettingsModel gebonden velden
            _originalValues["UiMainPageTitleText"] = LoadedSettingsModel.UiMainPageTitleText;
            _originalValues["UiMainPageSubtitleText"] = LoadedSettingsModel.UiMainPageSubtitleText;
            _originalValues["LightPrinterMqttTopic"] = LoadedSettingsModel.LightPrinterMqttTopic;
            _originalValues["InternalLightMqttTopic"] = LoadedSettingsModel.InternalLightMqttTopic;
            _originalValues["DmxLightMqttTopic"] = LoadedSettingsModel.DmxLightMqttTopic;
            _originalValues["MqttBrokerAddress"] = LoadedSettingsModel.MqttBrokerAddress;
            _originalValues["MqttBrokerPort"] = LoadedSettingsModel.MqttBrokerPort;
            _originalValues["MqttUsername"] = LoadedSettingsModel.MqttUsername;
            _originalValues["MqttPassword"] = LoadedSettingsModel.MqttPassword;
            //_originalValues["ImageUploadUrl"] = LoadedSettingsModel.ImageUploadUrl;
            _originalValues["EnableRemoteAdminViaMqtt"] = LoadedSettingsModel.EnableRemoteAdminViaMqtt;
            _originalValues["LogLevel"] = LoadedSettingsModel.LogLevel;

            Debug.WriteLine("SettingsPage: Original values stored for change detection.");
        }

        private async void InitializeControlsAsync() // Hernoemd van InitializeControls
        {
            if (LoadedSettingsModel == null) return;

            // Toon LastModifiedUtc
            LastModifiedTextBlock.Text = $"Laatst gewijzigd (UTC): {LoadedSettingsModel.LastModifiedUtc:yyyy-MM-dd HH:mm:ss}";

            // Update achtergrond preview
            if (!string.IsNullOrEmpty(LoadedSettingsModel.BackgroundImagePath) && File.Exists(LoadedSettingsModel.BackgroundImagePath))
            {
                await LoadBackgroundPreview(LoadedSettingsModel.BackgroundImagePath);
            }
            else
            {
                BackgroundPreviewImage.Source = null;
            }

            // Update photo strip preview
            if (!string.IsNullOrEmpty(LoadedSettingsModel.PhotoStripFilePath) && File.Exists(LoadedSettingsModel.PhotoStripFilePath))
            {
                await LoadPhotoStripPreview(LoadedSettingsModel.PhotoStripFilePath);
            }
            else
            {
                BackgroundPreviewImage.Source = null;
            }

            // De meeste andere controls worden nu via x:Bind bijgewerkt.
            // Zorg ervoor dat de DataContext correct is ingesteld (gedaan in constructor)
            // en dat LoadedSettingsModel de nieuwe data bevat (gedaan in LoadSettingsAsync).
            // expliciet Bindings.Update() in LoadSettingsAsync kan helpen.
        }

        private async Task SaveSettingsAsync()
        {
            if (LoadedSettingsModel == null)
            {
                Debug.WriteLine("SettingsPage: Cannot save, settings model is null.");
                return; // Of maak een nieuw model aan, maar dit zou niet mogen gebeuren na LoadSettingsAsync
            }

            // De meeste waarden worden via {x:Bind Mode=TwoWay} direct in LoadedSettingsModel bijgewerkt.
            // Voor de properties die direct op de Page class staan (en niet op LoadedSettingsModel),
            // moeten we ze expliciet terugschrijven naar het LoadedSettingsModel voordat we opslaan.
            LoadedSettingsModel.BackgroundImagePath = BackgroundImagePath; // Van page property naar model
            LoadedSettingsModel.PhotoStripFilePath = PhotoStripFilePath;
            LoadedSettingsModel.HorizontalReviewLayout = HorizontalReviewLayout;
            LoadedSettingsModel.ReviewPageTimeoutSeconds = ReviewPageTimeoutSeconds;
            LoadedSettingsModel.QrCodeTimeoutSeconds = QrCodeTimeoutSeconds;

            LoadedSettingsModel.EnablePhotos = EnablePhotos;
            LoadedSettingsModel.EnableVideos = EnableVideos;
            LoadedSettingsModel.EnablePrinting = EnablePrinting;
            LoadedSettingsModel.EnableUploading = EnableUploading;
            LoadedSettingsModel.EnableShowQr = EnableShowQr;
            LoadedSettingsModel.HotFolderPath = HotFolderPath;
            LoadedSettingsModel.PhotoOutputPath = PhotoOutputPath;
            LoadedSettingsModel.DnpPrinterStatusFilePath = DnpPrinterStatusFilePath;

            LoadedSettingsModel.InternalLedsMinimum = InternalLedsMinimum;
            LoadedSettingsModel.InternalLedsMaximum = InternalLedsMaximum;
            LoadedSettingsModel.ExternalDmxMinimum = ExternalDmxMinimum;
            LoadedSettingsModel.ExternalDmxMaximum = ExternalDmxMaximum;
            LoadedSettingsModel.DmxStartAddress = DmxStartAddress;

            // De LastModifiedUtc wordt in SettingsManager.SaveSettingsAsync() bijgewerkt als het een lokale save is.
            await SettingsManager.SaveSettingsAsync(LoadedSettingsModel); // isFromRemoteUpdate is false (default)
            Debug.WriteLine("SettingsPage: Settings saved to JSON via SettingsManager.");

            App.UpdateAppSettings(LoadedSettingsModel); // Update de globale instellingen in App
            App.Logger?.Information("SettingsPage: App.CurrentSettings updated with latest saved values.");

            StoreOriginalValues(); // Update originele waarden om de nieuw opgeslagen staat te reflecteren
            LastModifiedTextBlock.Text = $"Laatst gewijzigd (UTC): {LoadedSettingsModel.LastModifiedUtc:yyyy-MM-dd HH:mm:ss}"; // Update UI
        }


        private bool HasSettingsChanged()
        {
            if (_originalValues.Count == 0 || LoadedSettingsModel == null) return false;

            // Vergelijk direct gebonden page properties
            if (BackgroundImagePath != (_originalValues["BackgroundImagePath"] as string ?? "") ||
                PhotoStripFilePath != (_originalValues["PhotoStripFilePath"] as string ?? "") ||
                HorizontalReviewLayout != (bool)_originalValues["HorizontalReviewLayout"] ||
                ReviewPageTimeoutSeconds != (int)_originalValues["ReviewPageTimeoutSeconds"] ||
                QrCodeTimeoutSeconds != (int)_originalValues["QrCodeTimeoutSeconds"] ||
                EnablePhotos != (bool)_originalValues["EnablePhotos"] ||
                EnableVideos != (bool)_originalValues["EnableVideos"] ||
                EnablePrinting != (bool)_originalValues["EnablePrinting"] ||
                (PhotoOutputPath ?? "") != (_originalValues["PhotoOutputPath"] as string ?? "") ||
                (HotFolderPath ?? "") != (_originalValues["HotFolderPath"] as string ?? "") ||
                (DnpPrinterStatusFilePath ?? "") != (_originalValues["DnpPrinterStatusFilePath"] as string ?? "") ||
                InternalLedsMinimum != (int)_originalValues["InternalLedsMinimum"] ||
                InternalLedsMaximum != (int)_originalValues["InternalLedsMaximum"] ||
                ExternalDmxMinimum != (int)_originalValues["ExternalDmxMinimum"] ||
                ExternalDmxMaximum != (int)_originalValues["ExternalDmxMaximum"] ||
                DmxStartAddress != (int)_originalValues["DmxStartAddress"])
            {
                return true;
            }

            // Vergelijk properties gebonden aan LoadedSettingsModel
            if ((LoadedSettingsModel.UiMainPageTitleText ?? "") != (_originalValues["UiMainPageTitleText"] as string ?? "") ||
                (LoadedSettingsModel.UiMainPageSubtitleText ?? "") != (_originalValues["UiMainPageSubtitleText"] as string ?? "") ||
                (LoadedSettingsModel.LightPrinterMqttTopic ?? "") != (_originalValues["LightPrinterMqttTopic"] as string ?? "") ||
                (LoadedSettingsModel.InternalLightMqttTopic ?? "") != (_originalValues["InternalLightMqttTopic"] as string ?? "") ||
                (LoadedSettingsModel.DmxLightMqttTopic ?? "") != (_originalValues["DmxLightMqttTopic"] as string ?? "") ||
                (LoadedSettingsModel.MqttBrokerAddress ?? "") != (_originalValues["MqttBrokerAddress"] as string ?? "") ||
                LoadedSettingsModel.MqttBrokerPort != (int)_originalValues["MqttBrokerPort"] ||
                (LoadedSettingsModel.MqttUsername ?? "") != (_originalValues["MqttUsername"] as string ?? "") ||
                (LoadedSettingsModel.MqttPassword ?? "") != (_originalValues["MqttPassword"] as string ?? "") ||
                //(LoadedSettingsModel.ImageUploadUrl ?? "") != (_originalValues["ImageUploadUrl"] as string ?? "") ||
                LoadedSettingsModel.EnableRemoteAdminViaMqtt != (bool)_originalValues["EnableRemoteAdminViaMqtt"] ||
                (LoadedSettingsModel.LogLevel ?? "") != (_originalValues["LogLevel"] as string ?? ""))
            {
                return true;
            }

            return false;
        }

        private bool ValidateSettings()
        {
            bool isValid = true;
            // Validatie voor foto's/video's
            MediaWarningText.Visibility = (!EnablePhotos && !EnableVideos) ? Visibility.Visible : Visibility.Collapsed;
            if (!EnablePhotos && !EnableVideos) isValid = false;

            // Validatie voor sliders
            if (InternalLedsMinimum > InternalLedsMaximum)
            {
                isValid = false;
                // TODO: Visuele feedback voor InternalLedsMinSlider
                Debug.WriteLine("Validation Error: Internal LED Min > Max");
            }
            if (ExternalDmxMinimum > ExternalDmxMaximum)
            {
                isValid = false;
                // TODO: Visuele feedback voor ExternalDmxMinSlider
                Debug.WriteLine("Validation Error: External DMX Min > Max");
            }
            // Voeg hier meer validaties toe indien nodig
            return isValid;
        }

        // --- Event Handlers ---
        private async void ShowUnsavedChangesDialog()
        {
            var dialog = new ContentDialog()
            {
                Title = "Niet-opgeslagen Wijzigingen",
                Content = "Er zijn niet-opgeslagen wijzigingen. Wilt u ze opslaan voordat u vertrekt?",
                PrimaryButtonText = "Opslaan",
                SecondaryButtonText = "Verwerpen",
                CloseButtonText = "Annuleren",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await dialog.ShowAsync();
            switch (result)
            {
                case ContentDialogResult.Primary: // Opslaan
                    if (ValidateSettings())
                    {
                        await SaveSettingsAsync();
                        if (Frame.CanGoBack) Frame.GoBack();
                    }
                    // Blijf op pagina als validatie mislukt
                    break;
                case ContentDialogResult.Secondary: // Verwerpen
                    if (Frame.CanGoBack) Frame.GoBack();
                    break;
                case ContentDialogResult.None: // Annuleren
                    // Blijf op de pagina
                    break;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateSettings())
            {
                await SaveSettingsAsync();
                // Optioneel: Toon een "Opgeslagen!" melding
                // Voor nu, ga terug als dat kan
                if (Frame.CanGoBack) Frame.GoBack();
            }
            else
            {
                // Toon een algemene validatiefoutmelding als specifieke feedback niet voldoende is
                var dialog = new ContentDialog()
                {
                    Title = "Validatiefout",
                    Content = "Controleer de instellingen. Sommige waarden zijn incorrect (bv. min > max).",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) // Gekoppeld aan Annuleren knop
        {
            if (HasSettingsChanged())
            {
                ShowUnsavedChangesDialog();
            }
            else
            {
                if (Frame.CanGoBack) Frame.GoBack();
            }
        }

        private async void BrowseBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            InitializeWithWindow.Initialize(filePicker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                BackgroundImagePath = file.Path; // Update page property
                BackgroundImagePathTextBox.Text = file.Path; // Update UI
                await LoadBackgroundPreview(file.Path);
            }
        }

        private async void BrowsePhotoStripFileButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            // Voeg andere relevante bestandstypen toe indien nodig
            InitializeWithWindow.Initialize(filePicker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                PhotoStripFilePath = file.Path; // Update page property
                PhotoStripFilePathTextBox.Text = file.Path; // Update UI
                await LoadPhotoStripPreview(file.Path);
            }
        }

        private async void SelectPhotoOutputPathButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            folderPicker.FileTypeFilter.Add("*"); // Toon alle bestanden/mappen, gebruiker kiest map
            InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(App.MainWindow));
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                PhotoOutputPath = folder.Path; // Update page property
                PhotoOutputPathTextBox.Text = folder.Path; // Update UI
            }
        }

        private async void SelectHotFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            folderPicker.FileTypeFilter.Add("*"); // Toon alle bestanden/mappen, gebruiker kiest map
            InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(App.MainWindow));
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                HotFolderPath = folder.Path; // Update page property
                HotFolderPathTextBox.Text = folder.Path; // Update UI
            }
        }

        private async void SelectDnpStatusFileButton_Click(object sender, RoutedEventArgs e)
        {
            var filePicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            filePicker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(filePicker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                DnpPrinterStatusFilePath = file.Path; // Update page property
                DnpPrinterStatusFilePathTextBox.Text = file.Path; // Update UI
            }
        }

        private void ResetBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            BackgroundImagePath = "";
            BackgroundImagePathTextBox.Text = "";
            BackgroundPreviewImage.Source = null;
        }
        private void ResetPhotoStripButton_Click(object sender, RoutedEventArgs e)
        {
            PhotoStripFilePath = "";
            PhotoStripFilePathTextBox.Text = "";
            PhotoStripPreviewImage.Source = null;
        }

        private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // TODO: save in settings and use this path, also in App.xaml.cs
                string localAppDataPath;
                try
                {
                    // This is the standard and preferred way
                    localAppDataPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                }
                catch (Exception ex) // InvalidOperationException can occur if LocalFolder is not accessible (e.g., certain contexts for unpackaged apps very early)
                {
                    App.Logger?.Error(ex, "ConfigureLogging: Could not access ApplicationData.Current.LocalFolder.Path. Falling back to AppContext.BaseDirectory for logs path.");
                    // Fallback path, be mindful of write permissions if app is installed in Program Files
                    localAppDataPath = AppContext.BaseDirectory;
                }

                string logsDirectory = System.IO.Path.Combine(localAppDataPath, "Logs"); // New base path

                if (!Directory.Exists(logsDirectory)) { Directory.CreateDirectory(logsDirectory); }
                // App.Logger?.Information("Opening logs directory: {LogsDirectory}", logsDirectory);
                Debug.WriteLine($"Opening logs directory: {logsDirectory}"); // Using Debug.WriteLine if App.Logger isn't set up yet
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = logsDirectory, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening logs directory: {ex.Message}");
                var dialog = new ContentDialog { Title = "Error", Content = "Could not open logs directory. " + ex.Message, CloseButtonText = "OK", XamlRoot = this.XamlRoot };
                _ = dialog.ShowAsync();
            }
        }

        private async Task LoadBackgroundPreview(string imagePath)
        {
            App.Logger?.Verbose($"Loading new background image from path {imagePath}");

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                BackgroundPreviewImage.Source = null;
                return;
            }
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
        private async Task LoadPhotoStripPreview(string imagePath)
        {
            App.Logger?.Verbose($"Loading new photo strip preview image from path {imagePath}");

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                PhotoStripPreviewImage.Source = null;
                return;
            }
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(imagePath))
                {
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                }
                PhotoStripPreviewImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Error($"Error loading photo strip preview: {ex.Message}");
                PhotoStripPreviewImage.Source = null;
            }
        }

        private void LayoutStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
        {
            if (e.NewState != null)
            {
                string newStateName = e.NewState.Name;
                App.Logger?.Debug("SettingsPage: VisualState changed to {VisualStateName}", newStateName);

                // Optioneel: Log de huidige vensterbreedte (vereist wat meer setup om aan de AppWindow te komen)
                // if (App.MainWindow != null)
                // {
                //     try
                //     {
                //         IntPtr hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                //         WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                //         AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                //         if (appWindow != null)
                //         {
                //             Debug.WriteLine($"AdaptiveTrigger: Window width is approx {appWindow.Size.Width}");
                //             App.Logger?.Information("SettingsPage: Current window width {WindowWidth}", appWindow.Size.Width);
                //         }
                //     }
                //     catch (Exception ex)
                //     {
                //         Debug.WriteLine($"Error getting window width: {ex.Message}");
                //     }
                // }
            }
            if (e.OldState != null)
            {
                App.Logger?.Debug($"AdaptiveTrigger: VisualState changed from <- {e.OldState.Name}");
            }
        }
    }
}