using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Windows.Foundation;
using WinUI3App1;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;
using Windows.UI; // For handling keyboard shortcuts

namespace WinUI3App
{
    public sealed partial class MainPage : Page
    {
        // Configuration for the secret admin access
        private const int SecretPatternTimeWindow = 5000; // 5 seconds in milliseconds

        // Tracks corner touches for the secret admin pattern
        private ObservableCollection<CornerTouch> _cornerTouches = new ObservableCollection<CornerTouch>();

        // Logging collection
        private ObservableCollection<LogEntry> _logs = new ObservableCollection<LogEntry>();

        // Timer for tracking corner touches within time window
        private DispatcherTimer _cornerTouchTimer;

        // Keyboard shortcuts
        private KeyboardAccelerator settingsKeyboardAccelerator;

        private ImageBrush backgroundBrush;

        public MainPage()
        {
            this.InitializeComponent();

            // Initialize the corner touch timer
            _cornerTouchTimer = new DispatcherTimer();
            _cornerTouchTimer.Interval = TimeSpan.FromMilliseconds(100);
            _cornerTouchTimer.Tick += CornerTouchTimer_Tick;
            _cornerTouchTimer.Start();

            // Load background image
            LoadBackgroundFromSettings();

            // Log application start
            AddLog("Application started");
        }


        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Set focus to the page itself so it can receive keyboard input immediately
            this.Focus(FocusState.Programmatic);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            LoadDynamicUITexts();

            // Load the background image
            await LoadBackgroundFromSettings();
        }

        // If you want to refresh the background when returning from settings page:
        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // If navigating to settings page, register for when we return
            if (e.SourcePageType == typeof(SettingsPage))
            {
                Frame.Navigated += Frame_Navigated;
            }
        }

        private async void Frame_Navigated(object sender, NavigationEventArgs e)
        {

            // If we're returning to this page from the settings page
            if (e.SourcePageType == this.GetType())
            {
                // Remove the handler to prevent memory leaks
                Frame.Navigated -= Frame_Navigated;

                // Reload the background image in case it changed
                await LoadBackgroundFromSettings();

            }
        }

        private void LoadDynamicUITexts()
        {
            if (App.CurrentSettings == null)
            {
                App.Logger?.Warning("MainPage: App.CurrentSettings is null in LoadDynamicUITexts. UI texts will use XAML defaults or hardcoded fallbacks.");
                // Apply hardcoded fallbacks if TextBlocks might be empty and settings aren't loaded
                if (this.FindName("TitleTextBlock") is TextBlock titleDef) titleDef.Text = "Welcome!";
                if (this.FindName("SubtitleTextBlock") is TextBlock subtitleDef) subtitleDef.Text = "Capture your perfect moment.";
                if (this.FindName("TakePhotoButtonLabel") is TextBlock photoDef) photoDef.Text = "Take Photo";
                if (this.FindName("RecordVideoButtonLabel") is TextBlock videoDef) videoDef.Text = "Record Video";
                return;
            }

            App.Logger?.Information("MainPage: Loading dynamic UI texts from settings.");

            // Load Title Text (assuming TextBlock x:Name="TitleTextBlock" in your XAML)
            if (this.FindName("TitleTextBlock") is TextBlock titleTextBlock)
            {
                titleTextBlock.Text = App.CurrentSettings.UiMainPageTitleText ?? "Welcome!"; // Fallback
            }

            // Load Subtitle Text (assuming TextBlock x:Name="SubtitleTextBlock" in your XAML)
            if (this.FindName("SubtitleTextBlock") is TextBlock subtitleTextBlock)
            {
                subtitleTextBlock.Text = App.CurrentSettings.UiMainPageSubtitleText ?? "Capture your perfect moment."; // Fallback
            }

            // Load Button Texts (as implemented in the previous step)
            if (this.FindName("TakePhotoButtonLabel") is TextBlock photoButtonLabel)
            {
                photoButtonLabel.Text = App.CurrentSettings.UiMainPagePhotoButtonText ?? "Take Photo";
            }
            else if (this.FindName("TakePhotoButton") is Button photoButton && photoButton.Content is string)
            {
                photoButton.Content = App.CurrentSettings.UiMainPagePhotoButtonText ?? "Take Photo";
            }

            if (this.FindName("RecordVideoButtonLabel") is TextBlock videoButtonLabel)
            {
                videoButtonLabel.Text = App.CurrentSettings.UiMainPageVideoButtonText ?? "Record Video";
            }
            else if (this.FindName("RecordVideoButton") is Button videoButton && videoButton.Content is string)
            {
                videoButton.Content = App.CurrentSettings.UiMainPageVideoButtonText ?? "Record Video";
            }
        }

        // Updated background loading method with additional fixes
        private async Task LoadBackgroundFromSettings()
        {
            try
            {
                // No need to call SettingsManager.LoadSettingsAsync() again if App.CurrentSettings is already populated.
                // If App.CurrentSettings might not be loaded yet (e.g. if MainPage loads before App.OnLaunched fully completes settings loading,
                // which is unlikely but good to be mindful of timing), then load it:
                // PhotoBoothSettings settings = App.CurrentSettings ?? await SettingsManager.LoadSettingsAsync();
                // For simplicity, assuming App.CurrentSettings is populated by the time MainPage needs it:

                if (App.CurrentSettings == null)
                {
                    App.Logger?.Warning("App.CurrentSettings is null in MainPage.LoadBackgroundFromSettings. Attempting to load settings directly.");
                    // This direct load is a fallback, ideally App.CurrentSettings is reliable
                    var settings = await SettingsManager.LoadSettingsAsync();
                    if (App.CurrentSettings == null && settings != null) { /* App.CurrentSettings = settings; */ } // Avoid overwriting if App.OnLaunched sets it later
                    if (settings == null)
                    {
                        App.Logger?.Error("Failed to load settings directly in MainPage.");
                        BackgroundImage.Source = null; // Assuming BackgroundImage is your x:Name
                        BackgroundOverlay.Visibility = Visibility.Collapsed; // Assuming BackgroundOverlay is your x:Name
                        return;
                    }
                    // Use locally loaded 'settings' for this method if App.CurrentSettings was null
                    string backgroundImagePath = settings.BackgroundImagePath;
                    App.Logger?.Information("MainPage attempting to load background image from: {Path}", backgroundImagePath);
                    // ... rest of your loading logic using backgroundImagePath ...
                }
                else
                {
                    string backgroundImagePath = App.CurrentSettings.BackgroundImagePath;
                    App.Logger?.Information("MainPage attempting to load background image from App.CurrentSettings: {Path}", backgroundImagePath);
                    // ... (your existing logic to load image from backgroundImagePath) ...
                    if (!string.IsNullOrEmpty(backgroundImagePath) && File.Exists(backgroundImagePath))
                    {
                        BitmapImage bitmap = new BitmapImage { CreateOptions = BitmapCreateOptions.None };
                        using (FileStream stream = File.OpenRead(backgroundImagePath))
                        {
                            bitmap.DecodePixelWidth = 1920;
                            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                        }
                        // Assuming your XAML has <Image x:Name="BackgroundImage"/> and <Grid x:Name="BackgroundOverlay"/>
                        BackgroundImage.Source = bitmap;
                        BackgroundOverlay.Visibility = Visibility.Visible;
                        // BackgroundOverlay.Fill = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.3 }; // Or similar
                        Canvas.SetZIndex(BackgroundImage, -1); // If BackgroundImage is in a Canvas
                    }
                    else
                    {
                        BackgroundImage.Source = null;
                        BackgroundOverlay.Visibility = Visibility.Collapsed;
                        if (!string.IsNullOrEmpty(backgroundImagePath)) App.Logger?.Warning("Background image file not found: {Path}", backgroundImagePath);
                        else App.Logger?.Information("No custom background image path set in settings.");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error loading background image in MainPage: {Message}", ex.Message);
                if (this.FindName("BackgroundImage") is Image bgImg) bgImg.Source = null;
                if (this.FindName("BackgroundOverlay") is Grid bgOverlay) bgOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void AddLog(string message)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message
            };

            _logs.Add(logEntry);
            Debug.WriteLine($"{logEntry.Timestamp.ToString("HH:mm:ss.fff")}: {logEntry.Message}");
        }

        private void HandleCornerTouch(string cornerName)
        {
            AddLog($"Corner touched: {cornerName}");

            // Add this touch to the collection
            _cornerTouches.Add(new CornerTouch
            {
                CornerName = cornerName,
                TouchTime = DateTime.Now
            });

            // Check if the secret pattern has been entered
            CheckSecretPattern();
        }

        private void CheckSecretPattern()
        {
            // Remove old touches outside the time window
            DateTime cutoffTime = DateTime.Now.AddMilliseconds(-SecretPatternTimeWindow);

            for (int i = _cornerTouches.Count - 1; i >= 0; i--)
            {
                if (_cornerTouches[i].TouchTime < cutoffTime)
                {
                    _cornerTouches.RemoveAt(i);
                }
            }

            // Check if all four corners have been touched within the time window
            bool topLeftTouched = false;
            bool topRightTouched = false;
            bool bottomLeftTouched = false;
            bool bottomRightTouched = false;

            foreach (var touch in _cornerTouches)
            {
                switch (touch.CornerName)
                {
                    case "TopLeft": topLeftTouched = true; break;
                    case "TopRight": topRightTouched = true; break;
                    case "BottomLeft": bottomLeftTouched = true; break;
                    case "BottomRight": bottomRightTouched = true; break;
                }
            }

            if (topLeftTouched && topRightTouched && bottomLeftTouched && bottomRightTouched)
            {
                AddLog("Secret pattern detected! Opening admin panel.");

                OpenSetttingsPage();
                _cornerTouches.Clear();
            }
        }

        private void CornerTouchTimer_Tick(object sender, object e)
        {
            // Remove touches that are outside the time window
            DateTime cutoffTime = DateTime.Now.AddMilliseconds(-SecretPatternTimeWindow);

            for (int i = _cornerTouches.Count - 1; i >= 0; i--)
            {
                if (_cornerTouches[i].TouchTime < cutoffTime)
                {
                    _cornerTouches.RemoveAt(i);
                }
            }
        }

        private void TopLeftCorner_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HandleCornerTouch("TopLeft");
        }

        private void TopRightCorner_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HandleCornerTouch("TopRight");
        }

        private void BottomLeftCorner_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HandleCornerTouch("BottomLeft");
        }

        private void BottomRightCorner_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HandleCornerTouch("BottomRight");
        }

        private void TakePhotoButton_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Photo capture initiated");

            // Navigate to photo capture page
            Frame.Navigate(typeof(PhotoBoothPage));
        }

        private void RecordVideoButton_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Video recording initiated");
            // Navigate to video recording page
            // Frame.Navigate(typeof(VideoRecordingPage));
        }

        private void OpenSetttingsPage()
        {
            AddLog("Opening advanced settings page");
            Frame.Navigate(typeof(SettingsPage));
        }

        // Step 2: Add this method to your MainPage.xaml.cs file:
        private void Page_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Check if the pressed key is 'S' or 's' (VirtualKey.S handles both cases)
            if (e.Key == Windows.System.VirtualKey.S)
            {
                AddLog("Keyboard 'S' key press detected");

                // Mark the event as handled to prevent further processing
                e.Handled = true;

                // Navigate to the settings page
                OpenSetttingsPage();
            }
        }

    }

    public class CornerTouch
    {
        public string CornerName { get; set; }
        public DateTime TouchTime { get; set; }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }

        public string FormattedMessage => $"{Timestamp.ToString("HH:mm:ss.fff")}: {Message}";
    }
}