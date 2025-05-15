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
using Windows.UI;
using Microsoft.UI.Xaml.Media.Animation; // For handling keyboard shortcuts

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

            // Log application start
            App.Logger?.Information("Application started");
        }


        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App.Logger?.Information("MainPage: Page Loaded.");
            this.Focus(FocusState.Programmatic);

            // Load dynamic texts and background image now that elements are ready
            LoadDynamicUITexts();
            await LoadPageBackgroundAsync();

            // Transition in the content if it was initially hidden for a smooth load
            // Assuming your root content Grid in MainPage.xaml is named "RootContentGrid" and starts with Opacity="0"
            if (this.FindName("RootGrid") is UIElement rootContent)
            {
                // Ensure it's actually starting from 0 if XAML didn't set it or if re-loaded
                // rootContent.Opacity = 0; // Not strictly needed if XAML Opacity="0"

                var fadeInAnimation = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(600), // Adjust as needed
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(fadeInAnimation, rootContent);
                Storyboard.SetTargetProperty(fadeInAnimation, "Opacity");
                var sb = new Storyboard();
                sb.Children.Add(fadeInAnimation);
                sb.Begin();
                App.Logger?.Debug("MainPage: Content fade-in animation started.");
            }
            else
            {
                App.Logger?.Warning("MainPage: RootContentGrid not found for fade-in animation.");
            }

            // Set final state after loading and initial animations
            App.State = App.PhotoBoothState.Idle;

        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            App.State = App.PhotoBoothState.LoadingMainPage;

            // Load UI texts
            LoadDynamicUITexts();


            App.State = App.PhotoBoothState.Idle;
        }

        // If you want to refresh the background when returning from settings page:
        // we attach a handler when we leave the main page, only if we are going to the settings page
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
            // Reload settings if coming back from settings page
            if (e.SourcePageType == this.GetType())
            {
                // Remove the handler to prevent memory leaks
                Frame.Navigated -= Frame_Navigated;

                // Reload the background image in case it changed
                //TODO, update cached image
                await LoadPageBackgroundAsync();
            }
        }

        public void LoadDynamicUITexts()
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

            App.Logger?.Debug("MainPage: Loading dynamic UI texts from settings.");

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

        // New public method to be called by App.xaml.cs
        public async void RefreshBackgroundDisplay()
        {
            App.Logger?.Debug("MainPage: RefreshBackgroundDisplay called.");
            // This reuses your existing LoadPageBackgroundAsync logic,
            // which already correctly uses App.PreloadedBackgroundImage.
            // Ensure LoadPageBackgroundAsync is safe to call multiple times.
            await LoadPageBackgroundAsync();
        }


        // In PhotoBoothPage.xaml.cs (and similarly in MainPage.xaml.cs)
        public async Task LoadPageBackgroundAsync() 
        {
            App.Logger?.Debug("{PageName}: Attempting to apply preloaded page background.", this.GetType().Name);
            var pageBackgroundImageControl = this.FindName("PageBackgroundImage") as Image;
            var pageBackgroundOverlayControl = this.FindName("PageBackgroundOverlay") as Grid;

            if (pageBackgroundImageControl == null)
            {
                App.Logger?.Error("{PageName}: PageBackgroundImage control not found in XAML.", this.GetType().Name);
                if (pageBackgroundOverlayControl != null) pageBackgroundOverlayControl.Visibility = Visibility.Collapsed;
                return;
            }

            if (App.PreloadedBackgroundImage != null)
            {
                pageBackgroundImageControl.Source = App.PreloadedBackgroundImage;
                if (pageBackgroundOverlayControl != null) pageBackgroundOverlayControl.Visibility = Visibility.Visible;
                App.Logger?.Debug("{PageName}: Applied preloaded background image.", this.GetType().Name);
            }
            else
            {
                // Fallback if preloading failed or no image was configured
                pageBackgroundImageControl.Source = null;
                if (pageBackgroundOverlayControl != null) pageBackgroundOverlayControl.Visibility = Visibility.Collapsed;
                App.Logger?.Warning("{PageName}: No preloaded background image available or configured. Background cleared.", this.GetType().Name);

                // Optional: You could attempt to load it directly here as a fallback if App.PreloadedBackgroundImage is null
                // but App.CurrentSettings.BackgroundImagePath has a value (e.g., if preload failed but path is valid).
                // For simplicity, this example assumes if preload failed, we show no background.
                // If you want a fallback load:
                // if (App.CurrentSettings != null && !string.IsNullOrEmpty(App.CurrentSettings.BackgroundImagePath) && File.Exists(App.CurrentSettings.BackgroundImagePath)) { ... load it now ... }
            }
            // This method might no longer need to be async if it's just assigning the Source
            // unless you keep the fallback direct load logic. For now, keep as Task for consistency.
            await Task.CompletedTask;
        }

        private void HandleCornerTouch(string cornerName)
        {
            App.Logger?.Debug($"Corner touched: {cornerName}");

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
            App.Logger?.Debug("Checking secret pattern");

            // Remove old touches outside the time window
            DateTime cutoffTime = DateTime.Now.AddMilliseconds(-SecretPatternTimeWindow);
            
            for (int i = _cornerTouches.Count - 1; i >= 0; i--)
            {
                if (_cornerTouches[i].TouchTime < cutoffTime)
                {

                    App.Logger?.Debug($"Removing previous corner touch of {_cornerTouches[i].CornerName} at {_cornerTouches[i].TouchTime}");
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
                App.Logger?.Information("Secret pattern detected! Opening admin panel.");

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
            App.Logger?.Information("Photo capture initiated");

            // Navigate to photo capture page
            Frame.Navigate(typeof(PhotoBoothPage));
        }

        private void RecordVideoButton_Click(object sender, RoutedEventArgs e)
        {
            App.Logger?.Information("Video recording initiated");
            // Navigate to video recording page
            // Frame.Navigate(typeof(VideoRecordingPage));
        }

        private void OpenSetttingsPage()
        {
            App.Logger?.Information("Opening advanced settings page");
            Frame.Navigate(typeof(SettingsPage));
        }

        // Step 2: Add this method to your MainPage.xaml.cs file:
        private void Page_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Check if the pressed key is 'S' or 's' (VirtualKey.S handles both cases)
            if (e.Key == Windows.System.VirtualKey.S)
            {
                App.Logger?.Warning("Keyboard 'S' key press detected");

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