using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Serilog;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WinUI3App1;
using System.Linq;

namespace WinUI3App
{
    public sealed partial class PhotoBoothPage : Page
    {
        private int _photosTaken = 0;
        private const int TOTAL_PHOTOS_TO_TAKE = 3;
        private List<string> _photoPaths = new List<string>(); // contains the paths of the taken individual photos

        private static readonly string PlaceholderImageFileName = "placeholder.jpg";
        private static readonly string AssetsFolderName = "Assets";
        private static readonly string PLACEHOLDER_IMAGE_PATH = System.IO.Path.Combine(AppContext.BaseDirectory, AssetsFolderName, PlaceholderImageFileName);


        // Color of the dots
        private readonly SolidColorBrush _dotPendingBrush = new SolidColorBrush(Colors.DimGray);
        private readonly SolidColorBrush _dotActiveBrush = new SolidColorBrush(Colors.DodgerBlue);
        private readonly SolidColorBrush _dotCompletedBrush = new SolidColorBrush(Colors.LimeGreen);

        private DispatcherTimer _reviewPageTimeoutTimer;


        public PhotoBoothPage()
        {
            this.InitializeComponent();
            this.Loaded += PhotoBoothPage_Loaded;

            // Log placeholder image path and check if it exists
            App.Logger?.Debug("PhotoBoothPage: PLACEHOLDER_IMAGE_PATH resolved to: {PlaceholderPath}", PLACEHOLDER_IMAGE_PATH);
            if (!File.Exists(PLACEHOLDER_IMAGE_PATH))
            {
                App.Logger?.Error("PhotoBoothPage: CRITICAL - Placeholder image not found at resolved path: {PlaceholderPath}", PLACEHOLDER_IMAGE_PATH);
                // Overweeg hier een fallback of duidelijke foutmelding als de placeholder essentieel is.
            }

            // Initialize the timer but don't start it yet
            _reviewPageTimeoutTimer = new DispatcherTimer();
            _reviewPageTimeoutTimer.Tick += ReviewPageTimeoutTimer_Tick;
        }

        private async void PhotoBoothPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPageBackgroundAsync();

            LoadConfigurableTexts(); // Load texts after settings are available via App.CurrentSettings

            #region Review timeout timer
            // Set timer interval based on loaded settings
            if (App.CurrentSettings != null)
            {
                _reviewPageTimeoutTimer.Interval = TimeSpan.FromSeconds(App.CurrentSettings.ReviewPageTimeoutSeconds > 0 ? App.CurrentSettings.ReviewPageTimeoutSeconds : 30); // Use default if setting is invalid
                App.Logger?.Debug("PhotoBoothPage: Review page timeout set to {Timeout} seconds.", _reviewPageTimeoutTimer.Interval.TotalSeconds);
            }
            else
            {
                _reviewPageTimeoutTimer.Interval = TimeSpan.FromSeconds(30); // Fallback default
                App.Logger?.Warning("PhotoBoothPage: App.CurrentSettings is null, review page timeout defaulted to 30 seconds.");
            }
            #endregion

            await StartPhotoProcedure();
        }

        // This happens before the _Loaded event, but the UI tree is not yet fully ready
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            App.State = App.PhotoBoothState.LoadingPhotoBoothPage;
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


        // In PhotoBoothPage.xaml.cs (and similarly in MainPage.xaml.cs)
        private async Task LoadPageBackgroundAsync()
        {
            App.Logger?.Debug("{PageName}: Attempting to apply preloaded page background.", this.GetType().Name);
            var pageBackgroundImageControl = this.FindName("PageBackgroundImage") as Microsoft.UI.Xaml.Controls.Image;
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
                App.Logger?.Information("{PageName}: No preloaded background image available or configured. Background cleared.", this.GetType().Name);

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

            InstructionTextBackground.Opacity = 0;
            CountdownTextBackground.Opacity = 0;
            CountdownText.Text = "";
            TakenPhotoImage.Source = null;
            TakenPhotoImage.Opacity = 0;
            if (TakenPhotoImage.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = 0.5; st.ScaleY = 0.5;
            }
            else
            {
                TakenPhotoImage.RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 };
                TakenPhotoImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }

            CaptureElementsViewbox.Visibility = Visibility.Visible;

            HorizontalPhotoGalleryContainer.Visibility = Visibility.Collapsed;
            HorizontalPhotoGalleryContainer.Opacity = 0;

            VerticalPhotoGalleryContainer.Visibility = Visibility.Collapsed;
            VerticalPhotoGalleryContainer.Opacity = 0;

            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            ActionButtonsPanel.Opacity = 0;
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
            HorizontalPhotoGalleryContainer.Visibility = Visibility.Collapsed;
            VerticalPhotoGalleryContainer.Visibility = Visibility.Collapsed;

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
            if (_photosTaken == 0)
            {
                CameraPlaceholderImage.Visibility = Visibility.Visible;
            }
            else
            {
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

                HorizontalPhotoGalleryContainer.Visibility = Visibility.Collapsed;       // Ensure gallery is hidden
                VerticalPhotoGalleryContainer.Visibility = Visibility.Collapsed;       // Ensure gallery is hidden

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
            App.Logger.Debug("TakePhotoSimulation: Placeholder photo recorded. Total photos taken: {PhotosTakenCount}.", _photosTaken);

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
            App.Logger.Debug("TakePhotoSimulation: Proceeding to StartNextPhotoCapture.");
            await StartNextPhotoCapture();
            App.Logger.Debug("TakePhotoSimulation: Method finished.");
        }

        private async Task ShowAllPhotosForReview()
        {
            App.State = App.PhotoBoothState.ReviewingPhotos;
            App.Logger?.Debug("PhotoBoothPage: Showing photos for review.");

            CaptureElementsViewbox.Visibility = Visibility.Collapsed;
            TakenPhotoImage.Opacity = 0;
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed;

            bool useHorizontalLayout = App.CurrentSettings?.HorizontalReviewLayout ?? true; // Default naar horizontaal als setting niet bestaat
            App.Logger?.Debug("PhotoBoothPage: Review layout will be {Layout}. HorizontalReviewLayout setting is {SettingValue}",
                useHorizontalLayout ? "Horizontal" : "Vertical", App.CurrentSettings?.HorizontalReviewLayout);

            // Bepaal welke galerij en welke image controls te gebruiken
            Viewbox activeGalleryContainer;
            Microsoft.UI.Xaml.Controls.Image reviewPhoto1, reviewPhoto2, reviewPhoto3;

            if (useHorizontalLayout)
            {
                activeGalleryContainer = HorizontalPhotoGalleryContainer;
                VerticalPhotoGalleryContainer.Visibility = Visibility.Collapsed;
                reviewPhoto1 = H_Photo1Image;
                reviewPhoto2 = H_Photo2Image;
                reviewPhoto3 = H_Photo3Image;
                App.Logger?.Debug("PhotoBoothPage: Using HorizontalPhotoGallery.");
            }
            else
            {
                activeGalleryContainer = VerticalPhotoGalleryContainer;
                HorizontalPhotoGalleryContainer.Visibility = Visibility.Collapsed;
                reviewPhoto1 = V_Photo1Image;
                reviewPhoto2 = V_Photo2Image;
                reviewPhoto3 = V_Photo3Image;
                App.Logger?.Debug("PhotoBoothPage: Using VerticalPhotoGallery.");
            }

            // Reset de bronnen voor het geval er minder dan 3 foto's zijn
            reviewPhoto1.Source = null;
            reviewPhoto2.Source = null;
            reviewPhoto3.Source = null;

            // Populate de actieve galerij
            if (_photoPaths.Count > 0 && !string.IsNullOrEmpty(_photoPaths[0]))
                reviewPhoto1.Source = new BitmapImage(new Uri(_photoPaths[0]));
            else
                App.Logger?.Warning("PhotoBoothPage: Path voor foto 1 is leeg of null.");

            if (_photoPaths.Count > 1 && !string.IsNullOrEmpty(_photoPaths[1]))
                reviewPhoto2.Source = new BitmapImage(new Uri(_photoPaths[1]));
            else if (_photoPaths.Count > 1)
                App.Logger?.Warning("PhotoBoothPage: Path voor foto 2 is leeg of null.");


            if (_photoPaths.Count > 2 && !string.IsNullOrEmpty(_photoPaths[2]))
                reviewPhoto3.Source = new BitmapImage(new Uri(_photoPaths[2]));
            else if (_photoPaths.Count > 2)
                App.Logger?.Warning("PhotoBoothPage: Path voor foto 3 is leeg of null.");


            activeGalleryContainer.Opacity = 0;
            ActionButtonsPanel.Opacity = 0; // Knoppen blijven hetzelfde
            activeGalleryContainer.Visibility = Visibility.Visible;
            ActionButtonsPanel.Visibility = Visibility.Visible;

            // Animatie voor galerij en knoppen
            var galleryFadeIn = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500) };
            Storyboard.SetTarget(galleryFadeIn, activeGalleryContainer); Storyboard.SetTargetProperty(galleryFadeIn, "Opacity");

            var buttonsFadeIn = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500) };
            Storyboard.SetTarget(buttonsFadeIn, ActionButtonsPanel); Storyboard.SetTargetProperty(buttonsFadeIn, "Opacity");

            Storyboard reviewSb = new Storyboard();
            reviewSb.Children.Add(galleryFadeIn);
            reviewSb.Children.Add(buttonsFadeIn);
            reviewSb.Begin();

            _reviewPageTimeoutTimer.Start(); // Start inactiviteitstimer
            // await Task.CompletedTask; // Niet meer nodig als de methode async void is, maar kan geen kwaad
        }

        private async void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            // Review timer is stopped when the user interacts with the accept button
            _reviewPageTimeoutTimer.Stop();
            App.Logger?.Debug("PhotoBoothPage: Accept button clicked, review timeout timer stopped.");

            // Handle the accept button click event
            App.Logger.Information("Accept button clicked. Current state: {App.State}");

            if (App.State != App.PhotoBoothState.ReviewingPhotos)
            {
                return;
            }

            // Proceed to save the photos
            App.State = App.PhotoBoothState.Saving;
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed;
            HorizontalPhotoGalleryContainer.Visibility = Visibility.Collapsed;
            VerticalPhotoGalleryContainer.Visibility = Visibility.Collapsed;
            ActionButtonsPanel.Visibility = Visibility.Collapsed;

            #region Fade-in of overlay
            // --- Start of Fade-in Logic for OverlayGrid ---
            OverlayGrid.Opacity = 0; // Start fully transparent
            OverlayGrid.Visibility = Visibility.Visible; // Make it visible but transparent
            OverlayText.Text = App.CurrentSettings?.UiSavingMessage ?? "Saving...";

            // Create and start the fade-in animation
            var fadeInAnimation = new DoubleAnimation
            {
                To = 1.0, // Fade to fully opaque
                Duration = TimeSpan.FromMilliseconds(300), // Adjust duration as needed (e.g., 300-500ms)
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } // Smooth easing
            };
            Storyboard.SetTarget(fadeInAnimation, OverlayGrid);
            Storyboard.SetTargetProperty(fadeInAnimation, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(fadeInAnimation);
            storyboard.Begin();
            // --- End of Fade-in Logic for OverlayGrid ---
            #endregion

            // Simulate saving process 
            #region Merge and save
            App.Logger?.Information("PhotoBoothPage: Starting photo processing and merging after user accept.");
            string finalImagePath = null; // Om het pad naar de samengevoegde afbeelding op te vangen

            // Zorg ervoor dat _photoPaths correct gevuld is met de paden naar de 3 genomen foto's
            // en dat de template pad in settings beschikbaar is.
            if (App.CurrentSettings != null &&
                !string.IsNullOrEmpty(App.CurrentSettings.PhotoStripFilePath) &&
                _photoPaths != null &&
                _photoPaths.Count == TOTAL_PHOTOS_TO_TAKE &&
                _photoPaths.All(p => !string.IsNullOrEmpty(p) && File.Exists(p))) // Controleer of alle paden valide zijn en bestaan
            {
                // Roep de nieuwe methode aan
                finalImagePath = await ProcessAndMergePhotosAsync(App.CurrentSettings.PhotoStripFilePath, _photoPaths);
            }
            else
            {
                App.Logger?.Error("PhotoBoothPage: Cannot start merge process. Conditions not met (Settings, TemplatePath, or PhotoPaths invalid/incomplete).");
                if (App.CurrentSettings == null) App.Logger?.Error(" - App.CurrentSettings is null.");
                if (App.CurrentSettings != null && string.IsNullOrEmpty(App.CurrentSettings.PhotoStripFilePath)) App.Logger?.Error(" - PhotoStripFilePath is empty.");
                if (_photoPaths == null) App.Logger?.Error(" - _photoPaths is null.");
                if (_photoPaths != null && _photoPaths.Count != TOTAL_PHOTOS_TO_TAKE) App.Logger?.Error(" - _photoPaths count is not {TOTAL_PHOTOS_TO_TAKE}. Actual: {_photoPaths.Count}");
                if (_photoPaths != null)
                {
                    for (int i = 0; i < _photoPaths.Count; i++)
                    {
                        if (string.IsNullOrEmpty(_photoPaths[i]) || !File.Exists(_photoPaths[i]))
                        {
                            App.Logger?.Error($" - _photoPaths[{i}] is invalid or file does not exist: '{_photoPaths[i]}'");
                        }
                    }
                }
            }
            #endregion

            OverlayText.Text = App.CurrentSettings?.UiDoneMessage ?? "Done!";
            await Task.Delay(1500); // Original delay

            // If no error occurred, proceed to the next step
            // we do not need to remove the overlay, the whole page is faded out and it will look nice

            //#region Fade-out overlay
            //// --- Optional: Fade out OverlayGrid ---
            //// If you want it to fade out instead of abruptly disappearing:
            //var fadeOutAnimation = new DoubleAnimation
            //{
            //    To = 0.0,
            //    Duration = TimeSpan.FromMilliseconds(300),
            //    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            //};
            //Storyboard.SetTarget(fadeOutAnimation, OverlayGrid);
            //Storyboard.SetTargetProperty(fadeOutAnimation, "Opacity");

            //var storyboardOut = new Storyboard();
            //storyboardOut.Children.Add(fadeOutAnimation);
            //storyboardOut.Completed += (s, ev) => {
            //    OverlayGrid.Visibility = Visibility.Collapsed; // Hide it after fade out completes
            //};
            //storyboardOut.Begin();
            //await Task.Delay(300); // Only if you need to wait for fade-out before navigating
            //// --- End of Optional Fade out ---
            //#endregion

            // If not fading out, just hide it:
            //// OverlayGrid.Visibility = Visibility.Collapsed; // Original way to hide

            App.State = App.PhotoBoothState.Finished; // Use App.State

            NavigateBackToMainPage("Accepted"); // Call your refactored navigation method
        }

        private async void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            // Review timer is stopped when the user interacts with the retake button
            _reviewPageTimeoutTimer.Stop();
            App.Logger?.Debug("PhotoBoothPage: Retake button clicked, review timeout timer stopped.");

            if (App.State != App.PhotoBoothState.ReviewingPhotos && App.State != App.PhotoBoothState.Finished)
            {
                return;
            }
            await StartPhotoProcedure();
        }

        // Timer tick event handler
        private void ReviewPageTimeoutTimer_Tick(object sender, object e)
        {
            _reviewPageTimeoutTimer.Stop(); // Stop the timer
            App.Logger?.Debug("PhotoBoothPage: Review page inactivity timeout reached. Navigating back to MainPage.");

            // Navigate back to MainPage
            // Ensure App.State is reset or indicates returning to idle from timeout
            App.State = App.PhotoBoothState.ReviewingPhotosTimedOut; // Or a specific "TimedOut" state if you want to track it

            // Call the method to navigate back to MainPage
            NavigateBackToMainPage("Timeout"); // Call the extracted method
        }

        private async void NavigateBackToMainPage(string reason)
        {
            App.Logger?.Information("PhotoBoothPage: Navigating back to MainPage. Reason: {Reason}", reason);

            // Ensure App.State reflects that the photobooth process is no longer active or is finishing.
            // If finishing normally (Accept), it's already set to Finished.
            // If timing out, we might set it to Idle or a specific TimedOut state.
            if (reason.Contains("Timeout")) // Check if reason indicates a timeout
            {
                App.State = App.PhotoBoothState.Idle; // Or App.PhotoBoothState.ReviewingPhotosTimedOut if you add that state
            }
            // If called after "Accept", App.State would have been set to Finished already.

            #region Fade out page
            // --- Fade out the current page (PhotoBoothPage) ---
            var pageRoot = this.Content as UIElement; // Assuming 'this.Content' is your page's root container like RootGrid
            if (pageRoot != null)
            {
                var fadeOutAnimation = new DoubleAnimation
                {
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(250), // Adjust duration as needed
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(fadeOutAnimation, pageRoot);
                Storyboard.SetTargetProperty(fadeOutAnimation, "Opacity");

                var storyboard = new Storyboard();
                storyboard.Children.Add(fadeOutAnimation);

                // Create a TaskCompletionSource to await the animation's completion
                var tcs = new TaskCompletionSource<bool>();
                storyboard.Completed += (s, e_sb) => tcs.SetResult(true);

                storyboard.Begin();
                await tcs.Task; // Wait for the fade-out to complete
                App.Logger?.Debug("PhotoBoothPage: Fade out complete.");
            }
            else
            {
                App.Logger?.Warning("PhotoBoothPage: Could not find page root for fade-out animation.");
            }
            // --- End of Fade out ---
            #endregion

            #region Perform actual navigation
            // Attempt to find the root frame and navigate
            Frame rootFrame = null;
            if (App.MainWindow.Content is Frame appRootFrame)
            {
                rootFrame = appRootFrame;
            }
            else if (this.Frame != null) // Fallback to this page's frame
            {
                rootFrame = this.Frame;
            }

            if (rootFrame != null)
            {
                if (rootFrame.CanGoBack)
                {
                    App.Logger?.Debug("PhotoBoothPage: Navigating back using rootFrame.GoBack().");
                    rootFrame.GoBack();
                }
                else
                {
                    App.Logger?.Debug("PhotoBoothPage: rootFrame cannot GoBack(), navigating directly to MainPage type.");
                    rootFrame.Navigate(typeof(MainPage)); // Navigate to MainPage type, clears backstack for this frame
                }
            }
            else
            {
                App.Logger?.Error("PhotoBoothPage: Could not find a suitable Frame to navigate back to MainPage.");
            }
            #endregion
        }

        private async Task<string> ProcessAndMergePhotosAsync(string templateImagePath, List<string> individualPhotoPaths)
        {
            if (string.IsNullOrEmpty(templateImagePath) || !File.Exists(templateImagePath))
            {
                App.Logger?.Error("ImageProcessing: Template image path is invalid or file does not exist: {TemplatePath}", templateImagePath);
                return null;
            }

            if (individualPhotoPaths == null || individualPhotoPaths.Count != TOTAL_PHOTOS_TO_TAKE)
            {
                App.Logger?.Error("ImageProcessing: Incorrect number of photo paths provided. Expected {ExpectedCount}, got {ActualCount}.", TOTAL_PHOTOS_TO_TAKE, individualPhotoPaths?.Count ?? 0);
                return null;
            }

            foreach (var photoPath in individualPhotoPaths)
            {
                if (string.IsNullOrEmpty(photoPath) || !File.Exists(photoPath))
                {
                    App.Logger?.Error("ImageProcessing: A provided photo path is invalid or file does not exist: {PhotoPath}", photoPath);
                    return null;
                }
            }

            string filenamePrepend = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Output folder: Gebruik Environment.GetFolderPath voor bekende mappen
            string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string outputBaseFolderPath = System.IO.Path.Combine(picturesPath, "PhotoBoothAppOutput");

            // Zorg ervoor dat de output map bestaat
            try
            {
                Directory.CreateDirectory(outputBaseFolderPath); // Maakt de map aan als deze niet bestaat, doet niets als hij wel bestaat.
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "ImageProcessing: Failed to create output directory: {OutputDirectory}", outputBaseFolderPath);
                return null;
            }

            App.Logger?.Information("ImageProcessing: Starting merge. Template: {TemplatePath}. Output will be in: {OutputDirectory}", templateImagePath, outputBaseFolderPath);

            try
            {
                // Laad de template afbeelding
                using SixLabors.ImageSharp.Image<Rgba32> templateImage = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(templateImagePath);
                App.Logger?.Debug("ImageProcessing: Template image '{TemplateName}' loaded ({Width}x{Height}).", System.IO.Path.GetFileName(templateImagePath), templateImage.Width, templateImage.Height);

                // Laad de drie genomen foto's
                // We maken een lijst van de Image objecten zodat we ze kunnen disposen in een finally block of na gebruik
                var sourceImagesToProcess = new List<Image<Rgba32>>(TOTAL_PHOTOS_TO_TAKE);
                try
                {
                    sourceImagesToProcess.Add(await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(individualPhotoPaths[0]));
                    App.Logger?.Debug("ImageProcessing: Photo 1 '{PhotoName}' loaded.", System.IO.Path.GetFileName(individualPhotoPaths[0]));
                    sourceImagesToProcess.Add(await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(individualPhotoPaths[1]));
                    App.Logger?.Debug("ImageProcessing: Photo 2 '{PhotoName}' loaded.", System.IO.Path.GetFileName(individualPhotoPaths[1]));
                    sourceImagesToProcess.Add(await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(individualPhotoPaths[2]));
                    App.Logger?.Debug("ImageProcessing: Photo 3 '{PhotoName}' loaded.", System.IO.Path.GetFileName(individualPhotoPaths[2]));

                    // ----- BEGIN VAN JOUW SPECIFIEKE MERGE LOGICA -----
                    // Dit deel moet je overnemen en aanpassen uit je oude PhotoMain.xaml.cs
                    // Het volgende is een ZEER generiek voorbeeld en moet vervangen worden.
                    // Gebruik de 'sourceImagesToProcess' lijst.

                    // Voorbeeld: Afmetingen en posities (VERVANG DIT MET JOUW LOGICA)
                    var photoPositions = new[]
                    {
                        new { X = 50, Y = 100, Width = 300, Height = 200 },
                        new { X = 50, Y = 350, Width = 300, Height = 200 },
                        new { X = 50, Y = 600, Width = 300, Height = 200 }
                    };

                    for (int i = 0; i < sourceImagesToProcess.Count; i++)
                    {
                        var currentPhoto = sourceImagesToProcess[i];
                        var position = photoPositions[i];

                        // Optioneel: Croppen
                        // currentPhoto.Mutate(x => x.Crop(new Rectangle(cropX, cropY, targetCropWidth, targetCropHeight)));

                        // Resizen
                        currentPhoto.Mutate(x => x.Resize(position.Width, position.Height, KnownResamplers.Lanczos3));
                        App.Logger?.Debug($"ImageProcessing: Photo {i + 1} resized to {position.Width}x{position.Height}.");

                        // Teken op template
                        templateImage.Mutate(x => x.DrawImage(currentPhoto, new SixLabors.ImageSharp.Point(position.X, position.Y), 1f));
                        App.Logger?.Debug($"ImageProcessing: Photo {i + 1} drawn onto template at ({position.X},{position.Y}).");
                    }
                    // ----- EINDE VAN JOUW SPECIFIEKE MERGE LOGICA -----
                }
                finally
                {
                    // Zorg ervoor dat de geladen bronafbeeldingen worden gedisposed
                    foreach (var img in sourceImagesToProcess)
                    {
                        img?.Dispose();
                    }
                }


                // Opslaan van de samengevoegde afbeelding
                string outputFileName = $"{filenamePrepend}_PhotoStrip.jpg";
                string finalOutputPath = System.IO.Path.Combine(outputBaseFolderPath, outputFileName);

                var jpegEncoder = new JpegEncoder { Quality = 90 }; // Waarde tussen 1 en 100
                await templateImage.SaveAsJpegAsync(finalOutputPath, jpegEncoder); // Sla direct op naar pad

                App.Logger?.Information("ImageProcessing: Final merged photo strip saved to: {OutputPath}", finalOutputPath);

                // Kopiëren naar Hot Folder indien ingeschakeld en pad geconfigureerd
                if (App.CurrentSettings.EnablePrinting && !string.IsNullOrEmpty(App.CurrentSettings.HotFolderPath))
                {
                    string hotFolderPathString = App.CurrentSettings.HotFolderPath;
                    try
                    {
                        if (Directory.Exists(hotFolderPathString)) // Controleer of de doelmap bestaat
                        {
                            string destinationHotFilePath = System.IO.Path.Combine(hotFolderPathString, System.IO.Path.GetFileName(finalOutputPath));
                            // Genereer een unieke naam in de hot folder om overschrijven te voorkomen,
                            // of overschrijf als dat de bedoeling is.
                            int attempt = 0;
                            string tempDestPath = destinationHotFilePath;
                            while (File.Exists(tempDestPath))
                            {
                                attempt++;
                                tempDestPath = System.IO.Path.Combine(hotFolderPathString, $"{System.IO.Path.GetFileNameWithoutExtension(finalOutputPath)}_{attempt}{System.IO.Path.GetExtension(finalOutputPath)}");
                            }
                            destinationHotFilePath = tempDestPath;

                            File.Copy(finalOutputPath, destinationHotFilePath);
                            App.Logger?.Information("ImageProcessing: Merged photo strip copied to hot folder: {CopiedPath}", destinationHotFilePath);
                        }
                        else
                        {
                            App.Logger?.Warning("ImageProcessing: Hot folder path does not exist, cannot copy: {HotFolderPath}", hotFolderPathString);
                        }
                    }
                    catch (Exception ex) // Vangt bredere exceptions op voor IO
                    {
                        App.Logger?.Error(ex, "ImageProcessing: Failed to copy merged photo strip to hot folder {HotFolderPath}", hotFolderPathString);
                    }
                }

                return finalOutputPath; // Geef het pad naar de opgeslagen strip terug
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "ImageProcessing: A critical error occurred during the image merging process.");
                return null;
            }
            // Dispose van templateImage gebeurt door de 'using' statement aan het begin van de try-block.

        }
    }
}