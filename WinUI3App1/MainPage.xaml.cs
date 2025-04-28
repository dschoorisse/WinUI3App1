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

        // Add this to your OnNavigatedTo method
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

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

        // Updated background loading method with additional fixes
        private async Task LoadBackgroundFromSettings()
        {
            try
            {
                // Get the background image path from settings
                var localSettings = ApplicationData.Current.LocalSettings;
                string backgroundImagePath = localSettings.Values["BackgroundImagePath"] as string ?? "";

                App.Logger?.Information("Attempting to load background image from: {Path}", backgroundImagePath);

                // Check if the background image path is valid
                if (!string.IsNullOrEmpty(backgroundImagePath) && File.Exists(backgroundImagePath))
                {
                    // Create a new bitmap image
                    BitmapImage bitmap = new BitmapImage();

                    // Set bitmap properties before loading
                    bitmap.CreateOptions = BitmapCreateOptions.None;

                    // Load using a different approach
                    using (FileStream stream = File.OpenRead(backgroundImagePath))
                    {
                        bitmap.DecodePixelWidth = 1920; // Optimize for performance
                        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                    }

                    // Explicitly ensure the background image is at the back of the z-order
                    Canvas.SetZIndex(BackgroundImage, -1);

                    // Set the image source
                    BackgroundImage.Source = bitmap;

                    // Show/hide the overlay depending on if you want it
                    BackgroundOverlay.Visibility = Visibility.Visible; // or Collapsed if you don't want it

                    // Set to black with 30% opacity to make text more readable
                    BackgroundOverlay.Fill = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0));

                    App.Logger?.Information("Background image loaded successfully");
                }
                else
                {
                    // If no background is set or file doesn't exist, hide the overlay and clear the image
                    BackgroundImage.Source = null;
                    BackgroundOverlay.Visibility = Visibility.Collapsed;

                    App.Logger?.Information("No custom background image found");
                }
            }
            catch (Exception ex)
            {
                // Log any errors
                App.Logger?.Error(ex, "Error loading background image: {Message}", ex.Message);

                // Clear the background image and hide overlay on error
                BackgroundImage.Source = null;
                BackgroundOverlay.Visibility = Visibility.Collapsed;
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