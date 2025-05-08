using Microsoft.UI; // For Colors
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media; // For SolidColorBrush
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes; // For Ellipse
using System;
using System.Collections.Generic;
using System.IO; // For File.Exists
using System.Threading.Tasks;
using Windows.Storage; // For ApplicationData

// Ensure this namespace matches your project's namespace
namespace WinUI3App
{
    public sealed partial class PhotoBoothPage : Page
    {
        private enum PhotoBoothState
        {
            Idle,
            ShowingInstructions,
            Countdown,
            TakingPhoto,
            ShowingSinglePhoto,
            ReviewingPhotos,
            Saving,
            Finished
        }

        private PhotoBoothState _currentState = PhotoBoothState.Idle;
        private int _photosTaken = 0;
        private const int TOTAL_PHOTOS_TO_TAKE = 3;
        private List<string> _photoPaths = new List<string>();

        private const string PLACEHOLDER_IMAGE_PATH = "ms-appx:///Assets/placeholder.jpg";
        private const string SMILEY_REPLACEMENT = "😊"; // Or 😄, ✨, 📸

        // Brushes for Progress Indicator Dots
        private readonly SolidColorBrush _dotPendingBrush = new SolidColorBrush(Colors.DimGray);
        private readonly SolidColorBrush _dotActiveBrush = new SolidColorBrush(Colors.DodgerBlue); // Or use ThemeResource SystemAccentColor
        private readonly SolidColorBrush _dotCompletedBrush = new SolidColorBrush(Colors.LimeGreen);


        public PhotoBoothPage()
        {
            this.InitializeComponent();
            // It's good practice to set initial state of progress dots here if not done in XAML
            // but ResetProcedure will handle it.
            this.Loaded += PhotoBoothPage_Loaded;
        }

        private async void PhotoBoothPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPageBackgroundAsync();
            ResetProcedure(); // This will also reset progress dots
            await StartPhotoProcedure();
        }

        private async Task LoadPageBackgroundAsync()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                string backgroundImagePath = localSettings.Values["BackgroundImagePath"] as string ?? "";

                if (!string.IsNullOrEmpty(backgroundImagePath) && File.Exists(backgroundImagePath))
                {
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
                    PageBackgroundImage.Source = null;
                    PageBackgroundOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                // App.Logger?.Error(ex, "Failed to load page background");
                PageBackgroundImage.Source = null;
                PageBackgroundOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateProgressIndicator(int currentPhotoIndex, PhotoBoothState stateHint)
        {
            Ellipse[] dots = { ProgressDot1, ProgressDot2, ProgressDot3 };

            for (int i = 0; i < dots.Length; i++)
            {
                if (i < currentPhotoIndex) // Photos already taken
                {
                    dots[i].Fill = _dotCompletedBrush;
                }
                // currentPhotoIndex is 0-based for photos taken.
                // If we are about to take photo `N` (1-based), then `currentPhotoIndex` will be `N-1` *after* it's taken.
                // When state is Countdown, currentPhotoIndex refers to the photo about to be taken (0 for 1st, 1 for 2nd, etc.)
                else if (i == currentPhotoIndex && (stateHint == PhotoBoothState.Countdown || stateHint == PhotoBoothState.TakingPhoto))
                {
                    dots[i].Fill = _dotActiveBrush; // Current photo being processed
                }
                else
                {
                    dots[i].Fill = _dotPendingBrush; // Pending photos
                }
            }
            ProgressIndicatorPanel.Visibility = Visibility.Visible; // Make sure it's visible
        }


        private void ResetProcedure()
        {
            _photosTaken = 0;
            _photoPaths.Clear();
            _currentState = PhotoBoothState.Idle;

            InstructionTextBackground.Opacity = 0;
            CountdownTextBackground.Opacity = 0;
            CountdownText.Text = "";
            TakenPhotoImage.Source = null;
            TakenPhotoImage.Opacity = 0;
            if (TakenPhotoImage.RenderTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = 0.5; scaleTransform.ScaleY = 0.5;
            }
            else
            {
                TakenPhotoImage.RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 };
                TakenPhotoImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }

            PhotoGallery.Visibility = Visibility.Collapsed; PhotoGallery.Opacity = 0;
            ActionButtonsPanel.Visibility = Visibility.Collapsed; ActionButtonsPanel.Opacity = 0;
            OverlayGrid.Visibility = Visibility.Collapsed;

            UpdateProgressIndicator(-1, PhotoBoothState.Idle); // Reset dots, -1 indicates none active/completed yet
            CameraPlaceholderImage.Visibility = Visibility.Visible;
        }

        private async Task StartPhotoProcedure()
        {
            ResetProcedure();
            _currentState = PhotoBoothState.ShowingInstructions;
            await ShowInstructions();
        }

        private async Task ShowInstructions()
        {
            _currentState = PhotoBoothState.ShowingInstructions;
            InstructionText.Text = $"We are going to take {TOTAL_PHOTOS_TO_TAKE} pictures, get ready!";
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed; // Hide dots during instruction

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

        private async Task StartNextPhotoCapture()
        {
            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE)
            {
                _currentState = PhotoBoothState.Countdown;
                // Before starting countdown, update progress for the photo ABOUT to be taken
                UpdateProgressIndicator(_photosTaken, PhotoBoothState.Countdown);
                await DoCountdown();
            }
            else
            {
                ProgressIndicatorPanel.Visibility = Visibility.Collapsed; // Hide dots before showing gallery
                await ShowAllPhotosForReview();
            }
        }

        private async Task DoCountdown()
        {
            if (_photosTaken == 0) { CameraPlaceholderImage.Visibility = Visibility.Visible; }
            else { CameraPlaceholderImage.Visibility = Visibility.Collapsed; }
            TakenPhotoImage.Opacity = 0;

            // UpdateProgressIndicator(_photosTaken, PhotoBoothState.Countdown); // Moved to StartNextPhotoCapture

            string[] countdownSteps = { "3", "2", "1", SMILEY_REPLACEMENT };
            foreach (var step in countdownSteps)
            {
                CountdownText.Text = step;
                CountdownTextBackground.Opacity = 0;

                var daUkf = new DoubleAnimationUsingKeyFrames();
                Storyboard.SetTarget(daUkf, CountdownTextBackground);
                Storyboard.SetTargetProperty(daUkf, "Opacity");
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

        private async Task TakePhotoSimulation()
        {
            _currentState = PhotoBoothState.TakingPhoto;
            // _photosTaken is incremented AFTER this photo is conceptually "taken"
            UpdateProgressIndicator(_photosTaken, PhotoBoothState.TakingPhoto); // Update dot for current active photo

            // --- Simulate short delay for "taking photo" ---
            await Task.Delay(100); // Small delay to ensure active dot is seen
            // ---------------------------------------------

            _photoPaths.Add(PLACEHOLDER_IMAGE_PATH);
            _photosTaken++; // Increment after adding path, so _photosTaken now reflects completed count
            CameraPlaceholderImage.Visibility = Visibility.Collapsed;

            TakenPhotoImage.Source = new BitmapImage(new Uri(PLACEHOLDER_IMAGE_PATH));
            if (TakenPhotoImage.RenderTransform is not ScaleTransform)
            {
                TakenPhotoImage.RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 };
                TakenPhotoImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }
            ((ScaleTransform)TakenPhotoImage.RenderTransform).ScaleX = 0.5;
            ((ScaleTransform)TakenPhotoImage.RenderTransform).ScaleY = 0.5;
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

            // After photo is shown, update its dot to "completed"
            // _photosTaken is now 1 for the 1st photo, 2 for 2nd, etc.
            // So, the (photosTaken - 1) index is the one just completed.
            UpdateProgressIndicator(_photosTaken - 1, PhotoBoothState.ShowingSinglePhoto);


            var scaleXResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var scaleYResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var fadeOutAnimPhoto = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(300) };
            Storyboard hidePhotoSb = new Storyboard();
            Storyboard.SetTarget(fadeOutAnimPhoto, TakenPhotoImage); Storyboard.SetTargetProperty(fadeOutAnimPhoto, "Opacity");
            hidePhotoSb.Children.Add(fadeOutAnimPhoto);
            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE)
            {
                Storyboard.SetTarget(scaleXResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleXResetAnim, "ScaleX");
                Storyboard.SetTarget(scaleYResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform); Storyboard.SetTargetProperty(scaleYResetAnim, "ScaleY");
                hidePhotoSb.Children.Add(scaleXResetAnim); hidePhotoSb.Children.Add(scaleYResetAnim);
            }
            hidePhotoSb.Begin();
            await Task.Delay(300);
            await StartNextPhotoCapture();
        }

        private async Task ShowAllPhotosForReview()
        {
            _currentState = PhotoBoothState.ReviewingPhotos;
            CameraPlaceholderImage.Visibility = Visibility.Collapsed;
            TakenPhotoImage.Opacity = 0;
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed; // Ensure dots are hidden

            if (_photoPaths.Count > 0) Photo1Image.Source = new BitmapImage(new Uri(_photoPaths[0])); else Photo1Image.Source = null;
            if (_photoPaths.Count > 1) Photo2Image.Source = new BitmapImage(new Uri(_photoPaths[1])); else Photo2Image.Source = null;
            if (_photoPaths.Count > 2) Photo3Image.Source = new BitmapImage(new Uri(_photoPaths[2])); else Photo3Image.Source = null;

            PhotoGallery.Opacity = 0; ActionButtonsPanel.Opacity = 0;
            PhotoGallery.Visibility = Visibility.Visible; ActionButtonsPanel.Visibility = Visibility.Visible;

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
            PhotoGallery.Visibility = Visibility.Collapsed; ActionButtonsPanel.Visibility = Visibility.Collapsed;
            OverlayGrid.Visibility = Visibility.Visible; OverlayText.Text = "Saving...";
            await Task.Delay(2000);
            OverlayText.Text = "Done!"; await Task.Delay(1500);
            OverlayGrid.Visibility = Visibility.Collapsed;
            _currentState = PhotoBoothState.Finished;
            if (this.Frame != null && this.Frame.CanGoBack) { this.Frame.GoBack(); }
        }

        private async void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            // Allow retake from Reviewing or if somehow stuck in Finished on this page
            if (_currentState != PhotoBoothState.ReviewingPhotos && _currentState != PhotoBoothState.Finished) return;
            await StartPhotoProcedure(); // This now calls ResetProcedure() reliably
        }
    }
}