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
using Microsoft.UI.Xaml.Media.Animation;
using Canon.Sdk.Core;
using Canon.Sdk.Exceptions;
using System.Threading;
using EDSDKLib; // For handling keyboard shortcuts

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

        // Settings managment

        private ImageBrush backgroundBrush;
        private bool running;

        public MainPage()
        {
            this.InitializeComponent();

            // Initialize the corner touch timer
            _cornerTouchTimer = new DispatcherTimer();
            _cornerTouchTimer.Interval = TimeSpan.FromMilliseconds(100);
            _cornerTouchTimer.Tick += CornerTouchTimer_Tick;
            _cornerTouchTimer.Start();

            App.Logger?.Verbose("MainPage constructor called");
        }


        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App.Logger?.Debug("MainPage: Page Loaded.");

            // Check if lastest settings are loadeds
            if ((App.lastPreloadBackgroundUtc == DateTime.MinValue ) || 
                (App.CurrentSettings.LastModifiedUtc > App.lastPreloadBackgroundUtc))
            {
                App.Logger?.Debug("MainPage: Newer settings detected than loaded before. Will reload some settings!");

                // Load dynamic texts and background image now that elements are ready
                await App.PreloadBackgroundImageAsync();
            }


            LoadDynamicUITexts();

            // Configure photo/video buttons based on settings
            ConfigureButtons();

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
                App.Logger?.Verbose("MainPage: Content fade-in animation started.");
            }
            else
            {
                App.Logger?.Warning("MainPage: RootContentGrid not found for fade-in animation.");
            }

            App.lastPreloadBackgroundUtc = App.CurrentSettings.LastModifiedUtc;

            // Set final state after loading and initial animations
            App.State = App.PhotoBoothState.Idle;

            // Set focus for listening to keyboard 'S' press
            this.Focus(FocusState.Programmatic);

        }

        private void ConfigureButtons()
        {
            App.Logger?.Verbose("MainPage: Configuring buttons based on settings.");

            if (App.CurrentSettings != null)
            {
                TakePhotoButton.IsEnabled = App.CurrentSettings.EnablePhotos;
                TakePhotoButton.Visibility = App.CurrentSettings.EnablePhotos ? Visibility.Visible : Visibility.Collapsed;

                RecordVideoButton.IsEnabled = App.CurrentSettings.EnableVideos;
                RecordVideoButton.Visibility = App.CurrentSettings.EnableVideos ? Visibility.Visible : Visibility.Collapsed;

                App.Logger?.Debug("MainPage: Buttons configured based on settings. Photos: {0}, Videos: {1}",
                    App.CurrentSettings.EnablePhotos, App.CurrentSettings.EnableVideos);
            }
            else
            {
                App.Logger?.Warning("MainPage: App.CurrentSettings is null. Buttons will be enabled by default.");
            }
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

        // Applies the already preloaded background image
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
            App.Logger?.Debug("MainPage: Checking secret pattern");

            // Remove old touches outside the time window
            DateTime cutoffTime = DateTime.Now.AddMilliseconds(-SecretPatternTimeWindow);
            
            for (int i = _cornerTouches.Count - 1; i >= 0; i--)
            {
                if (_cornerTouches[i].TouchTime < cutoffTime)
                {

                    App.Logger?.Debug($"MainPage: Removing previous corner touch of {_cornerTouches[i].CornerName} at {_cornerTouches[i].TouchTime}");
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
                App.Logger?.Warning("MainPage: Secret pattern detected! Opening admin panel.");

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
            App.Logger?.Debug("MainPage: Photo capture initiated");

            // Navigate to photo capture page
            Frame.Navigate(typeof(PhotoBoothPage));
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            App.Logger?.Debug("MainPage: Test button clicked");


            App.Logger?.Debug("Initializing Canon SDK...");
            App.canonApi = new CanonAPI();
            App.canonApi.Initialize();
            App.Logger?.Information("Initialized Canon SDK!");


            // Do the tests
            App.Logger?.Warning("Looking for cameras...");
            App.cameraList = App.canonApi.GetCameraList();
            App.Logger?.Warning($"Found {App.cameraList.Count} camera(s)");

            // Write log to the debugTextBox
            DebugTextBox.Text += $"Found {App.cameraList.Count} camera(s)\n";
            DebugTextBox.Visibility = Visibility.Visible;

            // Do the tests
            if (App.cameraList.Count > 0)
            {
                App.Logger?.Debug("MainPage: Camera found, attempting to take a picture.");
                DebugTextBox.Text += "Camera found, attempting to take a picture.\n";
                TestTakePictureTemp();
            }
            else
            {
                App.Logger?.Error("MainPage: No cameras found. Please connect a camera and try again.");
                DebugTextBox.Text += "No cameras found. Please connect a camera and try again.\n";
            }

        }

        private void RecordVideoButton_Click(object sender, RoutedEventArgs e)
        {
            App.Logger?.Debug("MainPage: Video recording initiated");
            // Navigate to video recording page
            // Frame.Navigate(typeof(VideoRecordingPage));
        }

        private void OpenSetttingsPage()
        {
            App.Logger?.Debug("MainPage: Opening advanced settings page");
            Frame.Navigate(typeof(SettingsPage));
        }

        // Step 2: Add this method to your MainPage.xaml.cs file:
        private void Page_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Check if the pressed key is 'S' or 's' (VirtualKey.S handles both cases)
            if (e.Key == Windows.System.VirtualKey.S)
            {
                App.Logger?.Warning("MainPage: Keyboard 'S' key press detected");

                // Mark the event as handled to prevent further processing
                e.Handled = true;

                // Navigate to the settings page
                OpenSetttingsPage();
            }
        }

        private void TestTakePictureTemp()
        {
            try
            {
                lock (App.cameraLock)
                {
                    if (App.cameraList.Count > 0 && !App.isCameraConnectedAndInitialized)
                    {
                        App.currentCamera = App.cameraList[0];
                        // Subscribe first
                        //Console.WriteLine("Program.cs: Subscribing to C# events from Camera object...");
                        //currentCamera.PropertyChanged += OnPropertyChanged;
                        //currentCamera.ObjectChanged += OnObjectChanged;
                        //currentCamera.StateChanged += OnStateChanged;

                        ConnectCamera(App.currentCamera); // Connects and sets isCameraConnectedAndInitialized
                        while (App.currentCamera.BatteryLevel == -1)
                        {
                            Thread.Sleep(10); // Small delay
                        }
                    }
                    else if (App.cameraList.Count > 0)
                    {
                        Console.WriteLine("Camera already found and likely initialized.");
                    }
                    else
                    {
                        Console.WriteLine("No cameras found initially. Please connect a camera and wait...");
                    }
                }
                                
                // Process SDK events
                uint err = EDSDK.EdsGetEvent();
                if (err != EDSDK.EDS_ERR_OK && err != EDSDK.EDS_ERR_OBJECT_NOTREADY)
                {
                    Console.WriteLine($"WARNING: EdsGetEvent returned error: 0x{err:X}");
                }

                lock (App.cameraLock) // Ensure camera object doesn't change during operation
                {
                    var currentCamera = App.currentCamera;
                    if (currentCamera != null && App.isCameraConnectedAndInitialized)
                    {
                        try
                        {
                            Console.WriteLine("\n--- Checking state before TakePicture ---");
                            Console.WriteLine($"Model: {App.currentCamera.ProductName}"); //
                            Console.WriteLine($"Firmware: {App.currentCamera.FirmwareVersion}"); 
                            Console.WriteLine($"Battery: {App.currentCamera.BatteryLevel}%"); 
                            Console.WriteLine($"AE Mode: {App.currentCamera.AeMode}"); 
                            Console.WriteLine($"ISO: {App.currentCamera.IsoSpeed}"); 
                            Console.WriteLine($"Save Destination: {App.currentCamera.ImageSaveDestination}");

                            Console.WriteLine("-----------------------------------------");

                            Console.WriteLine("Taking picture (via Camera.TakePicture)...");
                            currentCamera.TakePicture(); // Uses PressShutterButton sequence
                            Console.WriteLine("TakePicture method call returned.");

                            // Check state immediately after (Optional - for debugging)
                            try
                            {
                                Thread.Sleep(100); // Small delay
                            }
                            catch (Exception statusEx) { Console.WriteLine($"Error getting status post-picture: {statusEx.Message}"); }

                            Console.WriteLine("Waiting for events via EdsGetEvent loop...");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error during TakePicture sequence: {ex.Message}");
                            if (ex is CanonSdkException sdkEx) { Console.WriteLine($"SDK Error Code: 0x{sdkEx.ErrorCode:X}"); }
                            if (ex.InnerException != null) { Console.WriteLine($"Inner Exception: {ex.InnerException.Message}"); }
                        }
                    }
                    else if (currentCamera == null)
                    {
                        Console.WriteLine("Cannot take picture: Camera not detected.");
                    }
                    else
                    {
                        Console.WriteLine("Cannot take picture: Camera not fully initialized.");
                    }

                    // Short sleep to prevent high CPU usage, adjust as needed
                    Thread.Sleep(50); // 50ms might be a reasonable balance
                } // end while(cameraRunning)
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                Console.WriteLine(ex.StackTrace); // Print stack trace for fatal errors
            }
            finally
            {
                // Clean up resources
                Console.WriteLine("Cleaning up...");
                lock (App.cameraLock)
                {
                    if (App.currentCamera != null)
                    {
                        Console.WriteLine("Disposing camera object...");
                        App.currentCamera.Dispose(); // Should close session if open, release camera ref
                        App.currentCamera = null;
                    }
                }

                if (App.canonApi != null)
                {
                    Console.WriteLine("Terminating Canon SDK...");
                    App.canonApi.Dispose(); // Should terminate SDK
                }
                Console.WriteLine("Cleanup finished. Press Enter to exit completely.");
                Console.ReadLine(); // Keep window open
            }
        }

        static void ConnectCamera(Camera camera)
        {
            // Basic check to prevent re-entry, protected by lock in calling methods
            if (App.isCameraConnectedAndInitialized && camera == App.currentCamera)
            {
                Console.WriteLine("ConnectCamera called but already initialized for this camera.");
                return;
            }

            try
            {
                DeviceInfo deviceInfo = camera.DeviceInfo;
                Console.WriteLine($"Connected to camera: {deviceInfo.DeviceDescription}");
                Console.WriteLine($"Port: {deviceInfo.PortName}");

                Console.WriteLine("Opening session...");
                camera.OpenSession(); // Includes SetSaveToHost internally

                // Event subscriptions moved to Main/OnCameraAdded

                // Print some initial properties
                try
                {
                    Console.WriteLine($"Product Name: {App.currentCamera.ProductName}");
                    Console.WriteLine($"Battery Level: {App.currentCamera.BatteryLevel}");
                }
                catch (Exception ex) { Console.WriteLine($"Error getting initial properties: {ex.Message}"); }

                // --- Command Thread Removed ---

                // Set flag after successful setup
                App.isCameraConnectedAndInitialized = true;
                Console.WriteLine("Camera initialization complete. isCameraConnectedAndInitialized = true.");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to camera: {ex.Message}");
                App.isCameraConnectedAndInitialized = false; // Reset on error
                if (ex is CanonSdkException sdkEx) { Console.WriteLine($"SDK Error Code: 0x{sdkEx.ErrorCode:X}"); }
                if (ex.InnerException != null) { Console.WriteLine($"Inner Exception: {ex.InnerException.Message}"); }
                // Optional: Rethrow or handle more gracefully
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