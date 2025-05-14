using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using WinUI3App1;

namespace WinUI3App
{
    public sealed partial class PhotoBoothPage : Page
    {
        private int _photosTaken = 0;
        private const int TOTAL_PHOTOS_TO_TAKE = 3;
        private List<string> _photoPaths = new List<string>();
        private const string PLACEHOLDER_IMAGE_PATH = "ms-appx:///Assets/placeholder.jpg";
        private readonly SolidColorBrush _dotPendingBrush = new SolidColorBrush(Colors.DimGray);
        private readonly SolidColorBrush _dotActiveBrush = new SolidColorBrush(Colors.DodgerBlue);
        private readonly SolidColorBrush _dotCompletedBrush = new SolidColorBrush(Colors.LimeGreen);

        public PhotoBoothPage()
        {
            this.InitializeComponent();
            this.Loaded += PhotoBoothPage_Loaded;

        }

        private async void PhotoBoothPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPageBackgroundAsync();

            App.State = App.PhotoBoothState.LoadingPhotoBoothPage;

            LoadConfigurableTexts(); // Load texts after settings are available via App.CurrentSettings
            ResetProcedure();
            await StartPhotoProcedure();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            App.State = App.PhotoBoothState.LoadingMainPage;

            // Load the background image
            await LoadPageBackgroundAsync();

            App.State = App.PhotoBoothState.Idle;
        }


        // Method to load configurable texts
        private void LoadConfigurableTexts()
        {
            if (App.CurrentSettings == null)
            {
                App.Logger?.Warning("PhotoBoothPage: App.CurrentSettings is null in LoadConfigurableTexts. UI texts might use fallbacks.");

                // Set hardcoded fallbacks directly if settings aren't loaded, though this path should ideally not be hit often
                // if App.OnLaunched correctly populates App.CurrentSettings.
                InstructionText.Text = string.Format("We are going to take {0} pictures, get ready!", TOTAL_PHOTOS_TO_TAKE); // Fallback
                if (this.FindName("AcceptButtonLabel") is TextBlock acceptLabel) acceptLabel.Text = "OK"; // Fallback
                if (this.FindName("RetakeButtonLabel") is TextBlock retakeLabel) retakeLabel.Text = "Retake"; // Fallback
                return;
            }

            // For InstructionText - set in ShowInstructions directly using settings
            // For Countdown steps - set in DoCountdown directly using settings
            // For Saving/Done messages - set in AcceptButton_Click directly using settings

            // Set button texts (assuming TextBlocks have x:Name="AcceptButtonLabel" and x:Name="RetakeButtonLabel")
            if (this.FindName("AcceptButtonLabel") is TextBlock accLabel)
            {
                accLabel.Text = App.CurrentSettings.UiButtonAcceptText ?? "OK";
            }
            if (this.FindName("RetakeButtonLabel") is TextBlock retLabel)
            {
                retLabel.Text = App.CurrentSettings.UiButtonRetakeText ?? "Retake";
            }
        }


        private async Task LoadPageBackgroundAsync()
        {
            App.Logger?.Debug("PhotoBoothPage: Attempting to load page background from App.CurrentSettings.");
            try
            {
                string backgroundImagePath = "";
                if (App.CurrentSettings != null)
                {
                    backgroundImagePath = App.CurrentSettings.BackgroundImagePath;
                    App.Logger?.Debug("PhotoBoothPage: BackgroundImagePath from App.CurrentSettings: '{Path}'", string.IsNullOrEmpty(backgroundImagePath) ? "<empty>" : backgroundImagePath);
                }
                else
                {
                    App.Logger?.Warning("PhotoBoothPage: App.CurrentSettings is null. Cannot determine background image path.");
                    // Ensure UI elements are found by x:Name or are direct fields
                    if (this.FindName("PageBackgroundImage") is Image pbiNull) pbiNull.Source = null;
                    if (this.FindName("PageBackgroundOverlay") is Grid pboNull) pboNull.Visibility = Visibility.Collapsed;
                    return;
                }

                // Attempt to find the controls by their x:Name from the XAML.
                // Ensure PageBackgroundImage and PageBackgroundOverlay are correctly named in your PhotoBoothPage.xaml
                var pageBackgroundImageControl = this.FindName("PageBackgroundImage") as Image;
                var pageBackgroundOverlayControl = this.FindName("PageBackgroundOverlay") as Grid;

                if (pageBackgroundImageControl == null)
                {
                    App.Logger?.Error("PhotoBoothPage: Critical - PageBackgroundImage control not found in XAML. Cannot set page background.");
                    // If the overlay exists independently, ensure it's also hidden.
                    if (pageBackgroundOverlayControl != null) pageBackgroundOverlayControl.Visibility = Visibility.Collapsed;
                    return;
                }

                if (!string.IsNullOrEmpty(backgroundImagePath) && File.Exists(backgroundImagePath))
                {
                    App.Logger?.Information("PhotoBoothPage: Path is valid and file exists. Loading image: {Path}", backgroundImagePath);
                    BitmapImage bitmap = new BitmapImage();
                    using (FileStream stream = File.OpenRead(backgroundImagePath))
                    {
                        // Consider making DecodePixelWidth configurable or consistent if issues arise
                        bitmap.DecodePixelWidth = 1920;
                        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                    }
                    pageBackgroundImageControl.Source = bitmap;
                    if (pageBackgroundOverlayControl != null)
                    {
                        pageBackgroundOverlayControl.Visibility = Visibility.Visible;
                        // Ensure overlay fill is set if not done in XAML, e.g., from App.CurrentSettings or a fixed value.
                        // Example: pageBackgroundOverlayControl.Background = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.3 };
                    }
                    App.Logger?.Information("PhotoBoothPage: Background successfully loaded from {Path}", backgroundImagePath);
                }
                else
                {
                    App.Logger?.Information("PhotoBoothPage: BackgroundImagePath is empty, null, or file does not exist. Clearing background.");
                    pageBackgroundImageControl.Source = null;
                    if (pageBackgroundOverlayControl != null) pageBackgroundOverlayControl.Visibility = Visibility.Collapsed;

                    if (!string.IsNullOrEmpty(backgroundImagePath))
                    {
                        App.Logger?.Warning("PhotoBoothPage: Background image file specified in settings was NOT FOUND at: {Path}", backgroundImagePath);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "PhotoBoothPage: Exception occurred in LoadPageBackgroundAsync.");
                // Attempt to find and clear again in case of error during loading
                if (this.FindName("PageBackgroundImage") is Image pbiEx) pbiEx.Source = null;
                if (this.FindName("PageBackgroundOverlay") is Grid pboEx) pboEx.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateProgressIndicator(int photosSuccessfullyCompleted, bool isCapturingNext)
        {
            // Update the progress dots based on the number of photos taken
            // and whether we are capturing the next photo
            // Set the colors of the dots based on the current state
            App.Logger.Debug($"Updating progress indicator: {photosSuccessfullyCompleted} photos taken, capturing next: {isCapturingNext}");
            Ellipse[] dots = { ProgressDot1, ProgressDot2, ProgressDot3 };
            for (int i = 0; i < dots.Length; i++)
            {
                App.Logger.Debug($"Dot {i + 1} state: {(i < photosSuccessfullyCompleted ? "Completed" : (i == photosSuccessfullyCompleted && isCapturingNext ? "Active" : "Pending"))}");
                if (i < photosSuccessfullyCompleted) { dots[i].Fill = _dotCompletedBrush; }
                else if (i == photosSuccessfullyCompleted && isCapturingNext && photosSuccessfullyCompleted < TOTAL_PHOTOS_TO_TAKE) { dots[i].Fill = _dotActiveBrush; }
                else { dots[i].Fill = _dotPendingBrush; }
            }
            // Visibility of the panel itself is now controlled by the calling methods
        }

        private async void ResetProcedure()
        {
            App.Logger.Debug("Resetting photo booth procedure...");
            App.State = App.PhotoBoothState.ResettingPhotoBoothPage;

            // Reset the state and UI elements
            _photosTaken = 0;
            _photoPaths.Clear();

            App.State = App.PhotoBoothState.Idle;

            InstructionTextBackground.Opacity = 0;
            CountdownTextBackground.Opacity = 0; CountdownText.Text = "";
            TakenPhotoImage.Source = null; TakenPhotoImage.Opacity = 0;
            if (TakenPhotoImage.RenderTransform is ScaleTransform st) { st.ScaleX = 0.5; st.ScaleY = 0.5; }
            else { TakenPhotoImage.RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 }; TakenPhotoImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5); }

            CaptureElementsViewbox.Visibility = Visibility.Visible;
            PhotoGallery.Visibility = Visibility.Collapsed; PhotoGallery.Opacity = 0;
            ActionButtonsPanel.Visibility = Visibility.Collapsed; ActionButtonsPanel.Opacity = 0;
            OverlayGrid.Visibility = Visibility.Collapsed;

            UpdateProgressIndicator(0, false);
            ProgressIndicatorPanel.Visibility = Visibility.Visible; // Dots are made visible here (all gray)
            CameraPlaceholderImage.Visibility = Visibility.Visible;
        }

        private async Task StartPhotoProcedure()
        {
            App.Logger.Information("Starting photo procedure...");
            
            // Make ProgressIndicatorPanel visible with pending dots
            ResetProcedure(); 

            App.State = App.PhotoBoothState.ShowingInstructions;
            await ShowInstructions();
        }

        private async Task ShowInstructions()
        {
            App.Logger.Debug("Showing instructions...");

            // Show instructions to the user
            App.State = App.PhotoBoothState.ShowingInstructions;

            string instructionFormat = App.CurrentSettings?.UiInstructionTextFormat ?? "We are going to take {0} pictures, get ready!";
            InstructionText.Text = string.Format(instructionFormat, TOTAL_PHOTOS_TO_TAKE);

            ProgressIndicatorPanel.Visibility = Visibility.Visible; // Keep dots visible as per last request
            UpdateProgressIndicator(0, false); // Show 3 pending dots

            CaptureElementsViewbox.Visibility = Visibility.Visible;
            PhotoGallery.Visibility = Visibility.Collapsed;

            var fadeInAnimation = new DoubleAnimation
            { To = 1.0, Duration = TimeSpan.FromSeconds(1), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fadeInAnimation, InstructionTextBackground);
            Storyboard.SetTargetProperty(fadeInAnimation, "Opacity");
            var sb = new Storyboard(); sb.Children.Add(fadeInAnimation); sb.Begin();

            await Task.Delay(3000);

            var fadeOutAnimation = new DoubleAnimation
            { To = 0.0, Duration = TimeSpan.FromSeconds(1), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(fadeOutAnimation, InstructionTextBackground);
            Storyboard.SetTargetProperty(fadeOutAnimation, "Opacity");
            sb = new Storyboard(); sb.Children.Add(fadeOutAnimation); sb.Begin();

            await Task.Delay(1000);
            await StartNextPhotoCapture();
        }

        private async Task DoCountdown()
        {
            // This method handles the countdown before taking a photo
            App.Logger.Debug("Starting countdown...");

            // If this is the first photo, show the camera placeholder image
            if (_photosTaken == 0) { 
                CameraPlaceholderImage.Visibility = Visibility.Visible; 
            }
            else { 
                CameraPlaceholderImage.Visibility = Visibility.Collapsed; 
            }
            TakenPhotoImage.Opacity = 0;

            // Use texts from settings, with fallbacks
            string step3Text = App.CurrentSettings?.UiCountdown3 ?? "3";
            string step2Text = App.CurrentSettings?.UiCountdown2 ?? "2";
            string step1Text = App.CurrentSettings?.UiCountdown1 ?? "1";
            string step0Text = App.CurrentSettings?.UiCountdown0 ?? "📸";

            string[] countdownSteps = { step3Text, step2Text, step1Text, step0Text };

            foreach (var step in countdownSteps)
            {
                App.Logger.Debug($"Countdown step: {step} of {countdownSteps.Length}");
                CountdownText.Text = step;
                CountdownTextBackground.Opacity = 0;

                var daUkf = new DoubleAnimationUsingKeyFrames();
                Storyboard.SetTarget(daUkf, CountdownTextBackground); Storyboard.SetTargetProperty(daUkf, "Opacity");

                // KeyFrames for the fade-in and fade-out effect
                var kfFadeInStart = new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)), Value = 0 };
                var kfFadeInEnd = new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250)), Value = 1, EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut } };
                var kfHold = new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750)), Value = 1 };
                var kfFadeOutEnd = new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000)), Value = 0, EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn } };

                daUkf.KeyFrames.Add(kfFadeInStart); daUkf.KeyFrames.Add(kfFadeInEnd); daUkf.KeyFrames.Add(kfHold); daUkf.KeyFrames.Add(kfFadeOutEnd);

                var sb = new Storyboard(); sb.Children.Add(daUkf); sb.Begin();
                await Task.Delay(1000);
            }
            CountdownTextBackground.Opacity = 0; CountdownText.Text = "";
            await TakePhotoSimulation();
        }

        private async Task StartNextPhotoCapture()
        {

            App.Logger.Debug($"Starting next photo capture, current state: {App.State}");


            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE)
            {
                // Proceed to take the next photo
                App.Logger.Information($"Taking photo {_photosTaken + 1} of {TOTAL_PHOTOS_TO_TAKE}");

                App.State = App.PhotoBoothState.Countdown;
                CaptureElementsViewbox.Visibility = Visibility.Visible; // Ensure capture elements are visible
                PhotoGallery.Visibility = Visibility.Collapsed;       // Ensure gallery is hidden

                UpdateProgressIndicator(_photosTaken, true);
                ProgressIndicatorPanel.Visibility = Visibility.Visible; // Make sure dots are shown
                await DoCountdown();
            }
            else
            {
                App.Logger.Information("All photos taken, proceeding to review...");

                // All photos taken, proceed to review
                await ShowAllPhotosForReview();
            }
        }

        // This method simulates taking a photo. In a real application, this would involve camera SDK calls.
        // It updates the UI to show the taken photo and animates it.
        // It also handles the progress indicator and prepares for the next photo or review screen.
        // The method is asynchronous and uses await for delays and animations.
        // It also sends status updates over MQTT
        private async Task TakePhotoSimulation()
        {
            // Send status update over MQTT
            App.State = App.PhotoBoothState.TakingPhoto;

            // Log the start of this photo simulation step, indicating which photo number this is (1-based)
            App.Logger.Information("TakePhotoSimulation: Starting photo {PhotoNumber} of {TotalPhotos}.", _photosTaken + 1, TOTAL_PHOTOS_TO_TAKE);

            // Set the application's current state to indicate a photo is being "taken"
            App.State = App.PhotoBoothState.TakingPhoto;
            App.Logger.Debug("TakePhotoSimulation: State set to TakingPhoto.");

            // Simulate a short delay for actual camera capture time
            App.Logger.Debug("TakePhotoSimulation: Simulating camera capture delay (100ms).");
            await Task.Delay(100); // In a real app, this would be your camera SDK's capture call.
            App.Logger.Debug("TakePhotoSimulation: Capture delay complete.");

            // Add the path of the "taken" photo (currently a placeholder) to our list
            _photoPaths.Add(PLACEHOLDER_IMAGE_PATH);
            // Increment the counter for photos taken
            _photosTaken++;
            App.Logger.Information("TakePhotoSimulation: Placeholder photo recorded. Total photos taken: {PhotosTakenCount}.", _photosTaken);

            // Update the progress indicator dots to reflect the photo just taken as completed.
            // The 'false' for isCapturingNext indicates we are done with this capture, not starting the next countdown yet.
            UpdateProgressIndicator(_photosTaken, false);
            ProgressIndicatorPanel.Visibility = Visibility.Visible; // Ensure the dots panel remains visible
            App.Logger.Debug("TakePhotoSimulation: Progress indicator updated.");

            // Prepare the UI for showing the taken photo preview
            CameraPlaceholderImage.Visibility = Visibility.Collapsed; // Hide the live feed/placeholder
            App.Logger.Debug("TakePhotoSimulation: CameraPlaceholderImage hidden.");

            TakenPhotoImage.Source = new BitmapImage(new Uri(PLACEHOLDER_IMAGE_PATH)); // Set the image source for the preview
            App.Logger.Debug("TakePhotoSimulation: TakenPhotoImage source set to placeholder.");

            // Ensure the RenderTransform is set up correctly for animations and reset its initial state (scaled down, transparent)
            if (TakenPhotoImage.RenderTransform is not ScaleTransform)
            {
                TakenPhotoImage.RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 };
                TakenPhotoImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                App.Logger.Debug("TakePhotoSimulation: ScaleTransform for TakenPhotoImage initialized.");
            }
            // Explicitly set initial scale for the "zoom in" effect
            ((ScaleTransform)TakenPhotoImage.RenderTransform).ScaleX = 0.5;
            ((ScaleTransform)TakenPhotoImage.RenderTransform).ScaleY = 0.5;
            TakenPhotoImage.Opacity = 0; // Start transparent for fade-in effect
            App.Logger.Debug("TakePhotoSimulation: TakenPhotoImage initial transform and opacity reset for animation.");

            // --- Animation to show the taken photo (Zoom in and Fade in) ---
            App.Logger.Debug("TakePhotoSimulation: Starting 'show photo' animation for TakenPhotoImage.");
            var scaleXAnim = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            var scaleYAnim = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            var fadeInAnim = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(400) };

            Storyboard showPhotoSb = new Storyboard();
            Storyboard.SetTarget(scaleXAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
            Storyboard.SetTarget(scaleYAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
            Storyboard.SetTarget(fadeInAnim, TakenPhotoImage); Storyboard.SetTargetProperty(fadeInAnim, "Opacity");

            showPhotoSb.Children.Add(scaleXAnim);
            showPhotoSb.Children.Add(scaleYAnim);
            showPhotoSb.Children.Add(fadeInAnim);
            showPhotoSb.Begin();

            // Wait for the photo to be displayed on screen for a set duration
            App.Logger.Debug("TakePhotoSimulation: Photo preview animation started. Waiting for display duration (2500ms).");
            await Task.Delay(2500);
            App.Logger.Debug("TakePhotoSimulation: Photo display duration complete.");

            // --- Animation to hide the taken photo (Fade out and optionally Zoom out) ---
            App.Logger.Debug("TakePhotoSimulation: Starting 'hide photo' animation for TakenPhotoImage.");
            var scaleXResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var scaleYResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var fadeOutAnimPhoto = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(300) };

            Storyboard hidePhotoSb = new Storyboard();
            Storyboard.SetTarget(fadeOutAnimPhoto, TakenPhotoImage); Storyboard.SetTargetProperty(fadeOutAnimPhoto, "Opacity");
            hidePhotoSb.Children.Add(fadeOutAnimPhoto);

            // Only apply the zoom-out part of the hide animation if there are more photos to take.
            // This prepares the ScaleTransform for the next photo's "zoom-in" or resets it if going to gallery.
            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE)
            {
                App.Logger.Debug("TakePhotoSimulation: More photos to take, adding scale reset to hide animation.");
                Storyboard.SetTarget(scaleXResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleXResetAnim, "ScaleX");
                Storyboard.SetTarget(scaleYResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleYResetAnim, "ScaleY");
                hidePhotoSb.Children.Add(scaleXResetAnim);
                hidePhotoSb.Children.Add(scaleYResetAnim);
            }
            hidePhotoSb.Begin();

            // Wait for the hide animation to complete
            await Task.Delay(300);
            App.Logger.Debug("TakePhotoSimulation: 'Hide photo' animation complete.");

            // Proceed to the next step in the photo capture sequence (either another countdown or the review screen)
            App.Logger.Information("TakePhotoSimulation: Proceeding to StartNextPhotoCapture.");
            await StartNextPhotoCapture();
            App.Logger.Debug("TakePhotoSimulation: Method finished.");
        }

        private async Task ShowAllPhotosForReview()
        {
            App.State = App.PhotoBoothState.ReviewingPhotos;

            // Hide elements related to individual capture/preview
            CaptureElementsViewbox.Visibility = Visibility.Collapsed;
            TakenPhotoImage.Opacity = 0;

            // Update the dots to reflect all are completed (optional, as panel will be hidden)
            // UpdateProgressIndicator(TOTAL_PHOTOS_TO_TAKE, false); 

            // Hide the progress dots panel for the final gallery review
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed; // <<-- KEY CHANGE HERE

            // Populate and show the final photo gallery
            if (_photoPaths.Count > 0) Photo1Image.Source = new BitmapImage(new Uri(_photoPaths[0])); else Photo1Image.Source = null;
            if (_photoPaths.Count > 1) Photo2Image.Source = new BitmapImage(new Uri(_photoPaths[1])); else Photo2Image.Source = null;
            if (_photoPaths.Count > 2) Photo3Image.Source = new BitmapImage(new Uri(_photoPaths[2])); else Photo3Image.Source = null;

            PhotoGallery.Opacity = 0;
            ActionButtonsPanel.Opacity = 0;
            PhotoGallery.Visibility = Visibility.Visible;
            ActionButtonsPanel.Visibility = Visibility.Visible;

            // Animation for gallery and buttons
            var galleryFadeIn = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500) };
            Storyboard.SetTarget(galleryFadeIn, PhotoGallery); Storyboard.SetTargetProperty(galleryFadeIn, "Opacity");
            var buttonsFadeIn = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500) };
            Storyboard.SetTarget(buttonsFadeIn, ActionButtonsPanel); Storyboard.SetTargetProperty(buttonsFadeIn, "Opacity");
            Storyboard reviewSb = new Storyboard();
            reviewSb.Children.Add(galleryFadeIn); reviewSb.Children.Add(buttonsFadeIn);
            reviewSb.Begin();

            await Task.CompletedTask;
        }

        private async void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            // Handle the accept button click event
            App.Logger.Information("Accept button clicked. Current state: {App.State}");

            if (App.State != App.PhotoBoothState.ReviewingPhotos)
            {
                return;
            }

            // Proceed to save the photos
            App.State = App.PhotoBoothState.Saving;
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed;
            PhotoGallery.Visibility = Visibility.Collapsed; ActionButtonsPanel.Visibility = Visibility.Collapsed;

            OverlayGrid.Visibility = Visibility.Visible;
            OverlayText.Text = App.CurrentSettings?.UiSavingMessage ?? "Saving..."; // Use from settings

            // Simulate saving process 
            await Task.Delay(1000);

            OverlayText.Text = App.CurrentSettings?.UiDoneMessage ?? "Done!"; // Use from settings
            await Task.Delay(1500);

            OverlayGrid.Visibility = Visibility.Collapsed;
            App.State = App.PhotoBoothState.Finished;
            if (this.Frame != null && this.Frame.CanGoBack) { this.Frame.GoBack(); }
        }

        private async void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.State != App.PhotoBoothState.ReviewingPhotos && App.State != App.PhotoBoothState.Finished)
            {
                return;
            }
            await StartPhotoProcedure();
        }
    }
}