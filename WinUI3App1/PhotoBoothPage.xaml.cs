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
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Path = System.IO.Path;
using Windows.Media.DialProtocol;

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
                App.Logger.Verbose($"Dot {i + 1} state: {(i < photosSuccessfullyCompleted ? "Completed" : (i == photosSuccessfullyCompleted && isCapturingNext ? "Active" : "Pending"))}");
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
            App.Logger.Verbose("TakePhotoSimulation: Starting 'show photo' animation for TakenPhotoImage.");
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
            App.Logger.Verbose("TakePhotoSimulation: Photo preview animation started. Waiting for display duration (2500ms).");
            await Task.Delay(2500);
            App.Logger.Verbose("TakePhotoSimulation: Photo display duration complete.");

            // --- Animation to hide the taken photo (Fade out and optionally Zoom out) ---
            App.Logger.Verbose("TakePhotoSimulation: Starting 'hide photo' animation for TakenPhotoImage.");
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
                App.Logger.Verbose("TakePhotoSimulation: More photos to take, adding scale reset to hide animation.");
                Storyboard.SetTarget(scaleXResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleXResetAnim, "ScaleX");
                Storyboard.SetTarget(scaleYResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleYResetAnim, "ScaleY");
                hidePhotoSb.Children.Add(scaleXResetAnim);
                hidePhotoSb.Children.Add(scaleYResetAnim);
            }
            hidePhotoSb.Begin();

            // Wait for the hide animation to complete
            await Task.Delay(300);
            App.Logger.Verbose("TakePhotoSimulation: 'Hide photo' animation complete.");

            // Proceed to the next step in the photo capture sequence (either another countdown or the review screen)
            App.Logger.Debug("TakePhotoSimulation: Proceeding to StartNextPhotoCapture.");
            await StartNextPhotoCapture();
            App.Logger.Debug("TakePhotoSimulation: StartNextPhotoCapture method finished.");
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
            App.Logger.Information($"Accept button clicked. Current state: {App.State}");

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
            App.Logger?.Verbose($"PhotoBoothPage: Started fade-in logic for overlayGrid");

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

            App.Logger?.Verbose($"PhotoBoothPage: Fade-in logic for overlayGrid completed");
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
                // TODO: a severe error has occured
                // show a popup to the user
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
                App.Logger?.Verbose("PhotoBoothPage: Fade out complete.");
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

        private async Task<string> ProcessAndMergePhotosAsync(string templateOverlayPath, List<string> individualPhotoPaths)
        {
            #region Check inputs
            if (string.IsNullOrEmpty(templateOverlayPath) || !File.Exists(templateOverlayPath))
            {
                App.Logger?.Error("ImageProcessing: Template overlay path is invalid or file does not exist: {TemplatePath}", templateOverlayPath);
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
            #endregion

            #region Retrieve composition settings
            // Haal de compositie-instellingen op
            var compositionSettings = App.CurrentSettings?.PhotoStripComposition;
            if (compositionSettings == null)
            {
                App.Logger?.Error("ImageProcessing: PhotoStripComposition settings are missing. Aborting.");
                // Optioneel: Gebruik hier hardcoded defaults als fallback of return null
                // Voor nu gaan we ervan uit dat ze altijd geladen zijn (met defaults uit de constructor)
                // Als je een expliciete fallback wilt:
                // compositionSettings = new PhotoStripImageCompositionSettings(); 
                return null; // Of gooi een exception
            }
            #endregion

            string filenamePrepend = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Get output folder path from settings or use default
            string outputBaseFolderPath = App.CurrentSettings?.PhotoOutputPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBoothOutput");

            // Create the output folder if it doesn't exist
            Directory.CreateDirectory(outputBaseFolderPath);

            App.Logger?.Information("ImageProcessing: Starting merge with overlay. Template: {TemplatePath}. Output base: {OutputDirectory}", templateOverlayPath, outputBaseFolderPath);

            try
            {
                using SixLabors.ImageSharp.Image<Rgba32> overlayTemplateImage = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(templateOverlayPath);
                App.Logger?.Debug("ImageProcessing: PNG Overlay Template '{TemplateName}' loaded ({Width}x{Height}).", Path.GetFileName(templateOverlayPath), overlayTemplateImage.Width, overlayTemplateImage.Height);

                // Verwachte template afmetingen
                const int expectedTemplateWidth = 1200;
                const int expectedTemplateHeight = 3600;

                if (overlayTemplateImage.Width != expectedTemplateWidth || overlayTemplateImage.Height != expectedTemplateHeight)
                {
                    App.Logger?.Warning("ImageProcessing: Template dimensions ({ActualWidth}x{ActualHeight}) do not match expected {ExpectedWidth}x{ExpectedHeight}. Results may vary.",
                        overlayTemplateImage.Width, overlayTemplateImage.Height, expectedTemplateWidth, expectedTemplateHeight);
                }

                using (var photoCanvasImage = new Image<Rgba32>(overlayTemplateImage.Width, overlayTemplateImage.Height))
                {
                    App.Logger?.Debug("ImageProcessing: Created photo canvas ({Width}x{Height}).", photoCanvasImage.Width, photoCanvasImage.Height);

                    var sourceImages = new List<Image<Rgba32>>(TOTAL_PHOTOS_TO_TAKE);
                    try
                    {
                        for (int i = 0; i < TOTAL_PHOTOS_TO_TAKE; i++)
                        {
                            sourceImages.Add(await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(individualPhotoPaths[i]));
                            App.Logger?.Debug("ImageProcessing: Photo {IndexPlusOne} '{PhotoName}' loaded.", i + 1, Path.GetFileName(individualPhotoPaths[i]));
                        }

                        int templateWidth = photoCanvasImage.Width;
                        int templateHeight = photoCanvasImage.Height;

                        // Gebruik waarden uit de settings
                        int horizontalPaddingPerSide = compositionSettings.TemplateHorizontalPaddingPerSide;
                        int photoAreaWidth = templateWidth - (2 * horizontalPaddingPerSide);

                        int desiredTopMargin = compositionSettings.TemplateDesiredTopMargin;
                        int desiredBottomMargin = compositionSettings.TemplateDesiredBottomMargin;
                        int spacingBetweenPhotos = compositionSettings.SpacingBetweenPhotos;

                        int totalVerticalSpaceForPhotosAndSpacing = templateHeight - desiredTopMargin - desiredBottomMargin;
                        int photoSlotHeight = (totalVerticalSpaceForPhotosAndSpacing - ((TOTAL_PHOTOS_TO_TAKE - 1) * spacingBetweenPhotos)) / TOTAL_PHOTOS_TO_TAKE;

                        App.Logger?.Verbose($"ImageProcessing: Horizontal Padding Per Side: {horizontalPaddingPerSide}, Total vertical space for photos and spacing: {totalVerticalSpaceForPhotosAndSpacing}, Desired Top Margin: {desiredTopMargin}, Desired Bottom Margin: {desiredBottomMargin}, " +
                            $"Spacing Between Photos: {spacingBetweenPhotos}, Target Photo Area Width: {photoAreaWidth}, Target Photo Slot Height: {photoSlotHeight}");

                        int currentY = desiredTopMargin; // Start Y-positie voor de eerste foto

                        for (int i = 0; i < sourceImages.Count; i++)
                        {
                            var currentPhoto = sourceImages[i];
                            var tempImage = currentPhoto.Clone();

                            // Resize and crop the source photo to fill the "photo window" (photoAreaWidth x photoSlotHeight)**
                            // We want to scale the photo so that it fills the slot, and then crop any excess.
                            // The aspect ratio of the slot is photoAreaWidth / photoSlotHeight.
                            float targetSlotAspectRatio = (float)photoAreaWidth / photoSlotHeight;
                            float sourceAspectRatio = (float)tempImage.Width / tempImage.Height;

                            int resizeWidth, resizeHeight;
                            ResizeMode resizeMode; // ImageSharp's ResizeMode.Crop vult en snijdt bij.

                            if (sourceAspectRatio > targetSlotAspectRatio)
                            {
                                // Source photo is wider (more landscape) than the target slot.
                                // We scale so the height fits, and the width will then have excess to crop.
                                App.Logger?.Debug($"Source photo is wider than target slot, scaling so it fits in height. Width will be cropped.");

                                resizeHeight = photoSlotHeight;
                                resizeWidth = (int)Math.Round(resizeHeight * sourceAspectRatio);
                                resizeMode = ResizeMode.Crop; // Zal schalen op hoogte en dan breedte croppen
                            }
                            else
                            {
                                // Source photo is narrower/taller (more portrait) than or equal to the target slot.
                                // We scale so the width fits, and the height will then have excess to crop.
                                App.Logger?.Debug($"Source photo is taller than target slot, scaling so it fits in width. Height will be cropped.");

                                resizeWidth = photoAreaWidth;
                                resizeHeight = (int)Math.Round(resizeWidth / sourceAspectRatio);
                                resizeMode = ResizeMode.Crop; // Zal schalen op breedte en dan hoogte croppen
                            }

                            // Use ResizeMode.Crop. ImageSharp first scales the image so that it covers the target area,
                            // then crops the excess from the center.
                            var resizeOptions = new ResizeOptions
                            {
                                Size = new Size(photoAreaWidth, photoSlotHeight), // De uiteindelijke afmetingen van de foto in het slot
                                Mode = ResizeMode.Crop, // Vul het gebied en snijd overschot af
                                Sampler = KnownResamplers.Lanczos3,
                                Position = AnchorPositionMode.Center // Crop vanuit het midden
                            };

                            tempImage.Mutate(x => x.Resize(resizeOptions));
                            App.Logger?.Debug($"ImageProcessing: Photo {i + 1} resized and cropped to {tempImage.Width}x{tempImage.Height} to fit slot.");

                            // Na ResizeMode.Crop zouden tempImage.Width en tempImage.Height exact photoAreaWidth en photoSlotHeight moeten zijn.
                            int finalPhotoWidth = tempImage.Width;
                            int finalPhotoHeight = tempImage.Height;

                            // X positie om horizontaal te centreren
                            int targetX = horizontalPaddingPerSide;
                            // Y positie is currentY (de bovenkant van het huidige slot)
                            int targetY = currentY;

                            photoCanvasImage.Mutate(x => x.DrawImage(tempImage, new Point(targetX, targetY), 1f));
                            App.Logger?.Debug($"ImageProcessing: Photo {i + 1} drawn onto photo canvas at ({targetX},{targetY}) with final size {finalPhotoWidth}x{finalPhotoHeight}.");

                            tempImage.Dispose();

                            currentY += photoSlotHeight + spacingBetweenPhotos; // Update Y voor de bovenkant van het volgende foto slot
                        }
                        // ----- EINDE AANGEPASTE LOGICA VOOR FOTO PLAATSING -----
                    }
                    finally
                    {
                        foreach (var img in sourceImages) { img?.Dispose(); }
                    }

                    var graphicsOptions = new GraphicsOptions() { AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver };
                    photoCanvasImage.Mutate(ctx => ctx.DrawImage(overlayTemplateImage, new Point(0, 0), graphicsOptions));
                    App.Logger?.Debug("ImageProcessing: PNG Overlay Template applied over photo canvas.");

                    string outputFileName = $"{filenamePrepend}_PhotoStrip_Overlay.jpg";
                    string finalOutputPath = Path.Combine(outputBaseFolderPath, outputFileName);

                    var jpegEncoder = new JpegEncoder { Quality = 90 };
                    await photoCanvasImage.SaveAsJpegAsync(finalOutputPath, jpegEncoder);
                    App.Logger?.Information("ImageProcessing: Final composite photo strip saved to: {OutputPath}", finalOutputPath);

                    // If printing is enabled and a hot folder path is set, copy the final output to the hot folder
                    if (App.CurrentSettings.EnablePrinting && !string.IsNullOrEmpty(App.CurrentSettings.HotFolderPath))
                    {
                        string hotFolderPathString = App.CurrentSettings.HotFolderPath;
                        try
                        {
                            if (Directory.Exists(hotFolderPathString))
                            {
                                string destinationHotFilePath = Path.Combine(hotFolderPathString, Path.GetFileName(finalOutputPath));
                                // ... (unieke bestandsnaam logica voor hotfolder) ...
                                int attempt = 0;
                                string tempDestPath = destinationHotFilePath;
                                while (File.Exists(tempDestPath))
                                {
                                    attempt++;
                                    tempDestPath = Path.Combine(hotFolderPathString, $"{Path.GetFileNameWithoutExtension(finalOutputPath)}_{attempt}{Path.GetExtension(finalOutputPath)}");
                                }
                                destinationHotFilePath = tempDestPath;
                                File.Copy(finalOutputPath, destinationHotFilePath);
                                App.Logger?.Information($"ImageProcessing: Merged photo strip copied to HotFolder: {destinationHotFilePath}. From there it should be printed by printer utility.");
                            }
                            else
                            {
                                App.Logger?.Warning($"ImageProcessing: Hot folder path does not exist, cannot copy: {hotFolderPathString}");
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "ImageProcessing: Failed to copy merged photo strip to hot folder {HotFolderPath}", hotFolderPathString);
                        }
                    }
                    else if (!App.CurrentSettings.EnablePrinting)
                    {
                        App.Logger?.Information("ImageProcessing: Printing is disabled, not copying to hot folder.");
                    }
                    else if (string.IsNullOrEmpty(App.CurrentSettings.HotFolderPath))
                    {
                        App.Logger?.Warning("ImageProcessing: Copy to HotFolder is enabled, but path is empty. Cannot copy to HotFolder.");
                    }
                    
                    return finalOutputPath;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "ImageProcessing: A critical error occurred during the image merging process with overlay.");
                return null;
            }
        }
    }
}