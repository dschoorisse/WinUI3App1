using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
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
        private enum PhotoBoothState { /* ... (as defined before) ... */ Idle, ShowingInstructions, Countdown, TakingPhoto, ShowingSinglePhoto, ReviewingPhotos, Saving, Finished }

        private PhotoBoothState _currentState = PhotoBoothState.Idle;
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
            LoadConfigurableTexts(); // Load texts after settings are available via App.CurrentSettings
            ResetProcedure();
            await StartPhotoProcedure();
        }
        // New method to load configurable texts
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
            App.Logger.Debug("Loading page background image from settings...");
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                string backgroundImagePath = localSettings.Values["BackgroundImagePath"] as string ?? "";

                // Check if the path is valid and the file exists
                if (!string.IsNullOrEmpty(backgroundImagePath) && File.Exists(backgroundImagePath))
                {
                    // Load the image from the specified path
                    App.Logger.Debug($"Loading background image from path: {backgroundImagePath}");
                    BitmapImage bitmap = new BitmapImage();
                    using (FileStream stream = File.OpenRead(backgroundImagePath))
                    {
                        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                    }
                    PageBackgroundImage.Source = bitmap;
                    PageBackgroundOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    // If the path is empty or the file doesn't exist, set a default image or hide the overlay
                    App.Logger.Debug("Background image path is empty or file does not exist. Hiding overlay.");
                    PageBackgroundImage.Source = null;
                    PageBackgroundOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                PageBackgroundImage.Source = null;
                PageBackgroundOverlay.Visibility = Visibility.Collapsed;
                // Log
                App.Logger?.Error($"Error loading background image: {ex.Message}");
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

        private void ResetProcedure()
        {
            App.Logger.Debug("Resetting photo booth procedure...");

            // Reset the state and UI elements
            _photosTaken = 0;
            _photoPaths.Clear();
            _currentState = PhotoBoothState.Idle;

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
            ResetProcedure(); // This makes ProgressIndicatorPanel visible with pending dots
            _currentState = PhotoBoothState.ShowingInstructions;
            await ShowInstructions();
        }

        private async Task ShowInstructions()
        {
            App.Logger.Debug("Showing instructions...");

            // Show instructions to the user
            _currentState = PhotoBoothState.ShowingInstructions;

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

            if (_photosTaken == 0) { CameraPlaceholderImage.Visibility = Visibility.Visible; }
            else { CameraPlaceholderImage.Visibility = Visibility.Collapsed; }
            TakenPhotoImage.Opacity = 0;

            // Use texts from settings, with fallbacks
            string step3Text = App.CurrentSettings?.UiCountdown3 ?? "3";
            string step2Text = App.CurrentSettings?.UiCountdown2 ?? "2";
            string step1Text = App.CurrentSettings?.UiCountdown1 ?? "1";
            string smileText = App.CurrentSettings?.UiCountdownSmile ?? "📸";

            string[] countdownSteps = { step3Text, step2Text, step1Text, smileText };

            foreach (var step in countdownSteps)
            {
                App.Logger.Debug($"Countdown step: {step} of {countdownSteps}");
                CountdownText.Text = step;
                CountdownTextBackground.Opacity = 0;

                var daUkf = new DoubleAnimationUsingKeyFrames();
                Storyboard.SetTarget(daUkf, CountdownTextBackground); Storyboard.SetTargetProperty(daUkf, "Opacity");
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
            App.Logger.Debug($"Starting next photo capture, current state: {_currentState}");

            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE)
            {
                // Proceed to take the next photo
                App.Logger.Debug($"Taking photo {_photosTaken + 1} of {TOTAL_PHOTOS_TO_TAKE}");
                _currentState = PhotoBoothState.Countdown;
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

        private async Task TakePhotoSimulation()
        {
            // Simulate taking a photo (replace with actual camera capture logic)
            App.Logger.Debug("Simulating photo capture...");

            _currentState = PhotoBoothState.TakingPhoto;
            await Task.Delay(100);

            _photoPaths.Add(PLACEHOLDER_IMAGE_PATH);
            _photosTaken++;
            UpdateProgressIndicator(_photosTaken, false);
            ProgressIndicatorPanel.Visibility = Visibility.Visible; // Ensure dots remain visible

            CameraPlaceholderImage.Visibility = Visibility.Collapsed; // This is inside CaptureElementsViewbox
            TakenPhotoImage.Source = new BitmapImage(new Uri(PLACEHOLDER_IMAGE_PATH));
            // ... (rest of TakenPhotoImage setup and animation)
            if (TakenPhotoImage.RenderTransform is not ScaleTransform) { TakenPhotoImage.RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 }; TakenPhotoImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5); }
            ((ScaleTransform)TakenPhotoImage.RenderTransform).ScaleX = 0.5; ((ScaleTransform)TakenPhotoImage.RenderTransform).ScaleY = 0.5;
            TakenPhotoImage.Opacity = 0;
            var scaleXAnim = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            var scaleYAnim = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };
            var fadeInAnim = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(400) };
            Storyboard showPhotoSb = new Storyboard();
            Storyboard.SetTarget(scaleXAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
            Storyboard.SetTarget(scaleYAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
            Storyboard.SetTarget(fadeInAnim, TakenPhotoImage); Storyboard.SetTargetProperty(fadeInAnim, "Opacity");
            showPhotoSb.Children.Add(scaleXAnim); showPhotoSb.Children.Add(scaleYAnim); showPhotoSb.Children.Add(fadeInAnim);
            showPhotoSb.Begin();
            await Task.Delay(2500);
            var scaleXResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var scaleYResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var fadeOutAnimPhoto = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(300) };
            Storyboard hidePhotoSb = new Storyboard();
            Storyboard.SetTarget(fadeOutAnimPhoto, TakenPhotoImage); Storyboard.SetTargetProperty(fadeOutAnimPhoto, "Opacity");
            hidePhotoSb.Children.Add(fadeOutAnimPhoto);
            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE) { Storyboard.SetTarget(scaleXResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleXResetAnim, "ScaleX"); Storyboard.SetTarget(scaleYResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleYResetAnim, "ScaleY"); hidePhotoSb.Children.Add(scaleXResetAnim); hidePhotoSb.Children.Add(scaleYResetAnim); }
            hidePhotoSb.Begin();
            await Task.Delay(300);

            await StartNextPhotoCapture();
        }

        private async Task ShowAllPhotosForReview()
        {
            _currentState = PhotoBoothState.ReviewingPhotos;

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
            if (_currentState != PhotoBoothState.ReviewingPhotos) return;
            _currentState = PhotoBoothState.Saving;
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed;
            PhotoGallery.Visibility = Visibility.Collapsed; ActionButtonsPanel.Visibility = Visibility.Collapsed;

            OverlayGrid.Visibility = Visibility.Visible;
            OverlayText.Text = App.CurrentSettings?.UiSavingMessage ?? "Saving..."; // Use from settings

            await Task.Delay(2000);

            OverlayText.Text = App.CurrentSettings?.UiDoneMessage ?? "Done!"; // Use from settings
            await Task.Delay(1500);

            OverlayGrid.Visibility = Visibility.Collapsed;
            _currentState = PhotoBoothState.Finished;
            if (this.Frame != null && this.Frame.CanGoBack) { this.Frame.GoBack(); }
        }

        private async void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState != PhotoBoothState.ReviewingPhotos && _currentState != PhotoBoothState.Finished) return;
            await StartPhotoProcedure();
        }
    }
}