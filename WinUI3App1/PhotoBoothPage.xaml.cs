using Microsoft.UI; // For Colors
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media; // For SolidColorBrush
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
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
        // For smiley: 😊 (Segoe UI Emoji), 😄, ✨, 📸
        private const string SMILEY_REPLACEMENT = "📸";


        public PhotoBoothPage()
        {
            this.InitializeComponent();
            this.Loaded += PhotoBoothPage_Loaded;
        }

        private async void PhotoBoothPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPageBackgroundAsync(); // Load background first
            ResetProcedure();
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
                    // Ensure overlay has the desired color if not set in XAML, e.g.
                    // PageBackgroundOverlay.Background = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)); 
                    // XAML already sets it to #4C000000 (approx 30% black)
                }
                else
                {
                    PageBackgroundImage.Source = null;
                    PageBackgroundOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                // Log error (App.Logger?.Error(ex, "Failed to load page background");)
                PageBackgroundImage.Source = null;
                PageBackgroundOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ResetProcedure()
        {
            _photosTaken = 0;
            _photoPaths.Clear();
            _currentState = PhotoBoothState.Idle;

            InstructionTextBackground.Opacity = 0; // Target the wrapper
            CountdownTextBackground.Opacity = 0;   // Target the wrapper
            CountdownText.Text = "";
            TakenPhotoImage.Source = null;
            TakenPhotoImage.Opacity = 0;
            if (TakenPhotoImage.RenderTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = 0.5;
                scaleTransform.ScaleY = 0.5;
            }
            else
            {
                TakenPhotoImage.RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 };
                TakenPhotoImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }

            PhotoGallery.Visibility = Visibility.Collapsed;
            PhotoGallery.Opacity = 0;
            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            ActionButtonsPanel.Opacity = 0;
            OverlayGrid.Visibility = Visibility.Collapsed;

            CameraPlaceholderImage.Visibility = Visibility.Visible;
        }

        private async Task StartPhotoProcedure()
        {
            // Allow restart from any state if explicitly called (e.g., by Retake or initial load)
            ResetProcedure();
            _currentState = PhotoBoothState.ShowingInstructions;
            await ShowInstructions();
        }

        private async Task ShowInstructions()
        {
            _currentState = PhotoBoothState.ShowingInstructions;
            InstructionText.Text = $"We are going to take {TOTAL_PHOTOS_TO_TAKE} pictures, get ready!";

            var fadeInAnimation = new DoubleAnimation
            { To = 1.0, Duration = TimeSpan.FromSeconds(1), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fadeInAnimation, InstructionTextBackground); // Target wrapper
            Storyboard.SetTargetProperty(fadeInAnimation, "Opacity");
            var instructionStoryboard = new Storyboard();
            instructionStoryboard.Children.Add(fadeInAnimation);
            instructionStoryboard.Begin();

            await Task.Delay(3000);

            var fadeOutAnimation = new DoubleAnimation
            { To = 0.0, Duration = TimeSpan.FromSeconds(1), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(fadeOutAnimation, InstructionTextBackground); // Target wrapper
            Storyboard.SetTargetProperty(fadeOutAnimation, "Opacity");
            instructionStoryboard = new Storyboard();
            instructionStoryboard.Children.Add(fadeOutAnimation);
            instructionStoryboard.Begin();

            await Task.Delay(1000);
            await StartNextPhotoCapture();
        }

        private async Task StartNextPhotoCapture()
        {
            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE)
            {
                _currentState = PhotoBoothState.Countdown;
                await DoCountdown();
            }
            else
            {
                await ShowAllPhotosForReview();
            }
        }

        private async Task DoCountdown()
        {
            if (_photosTaken == 0)
            {
                CameraPlaceholderImage.Visibility = Visibility.Visible;
            }
            else
            {
                CameraPlaceholderImage.Visibility = Visibility.Collapsed;
            }
            TakenPhotoImage.Opacity = 0;

            string[] countdownSteps = { "3", "2", "1", SMILEY_REPLACEMENT }; // Using smiley
            foreach (var step in countdownSteps)
            {
                CountdownText.Text = step;
                CountdownTextBackground.Opacity = 0; // Target wrapper for animation

                var daUkf = new DoubleAnimationUsingKeyFrames();
                Storyboard.SetTarget(daUkf, CountdownTextBackground); // Target wrapper
                Storyboard.SetTargetProperty(daUkf, "Opacity");

                var kfFadeInStart = new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)), Value = 0 };
                var kfFadeInEnd = new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250)), Value = 1, EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut } };
                var kfHold = new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750)), Value = 1 };
                var kfFadeOutEnd = new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000)), Value = 0, EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseIn } };

                daUkf.KeyFrames.Add(kfFadeInStart);
                daUkf.KeyFrames.Add(kfFadeInEnd);
                daUkf.KeyFrames.Add(kfHold);
                daUkf.KeyFrames.Add(kfFadeOutEnd);

                var sb = new Storyboard();
                sb.Children.Add(daUkf);
                sb.Begin();

                await Task.Delay(1000);
            }
            // Ensure countdown background is hidden after the loop
            CountdownTextBackground.Opacity = 0;
            CountdownText.Text = "";

            await TakePhotoSimulation();
        }

        private async Task TakePhotoSimulation()
        {
            _currentState = PhotoBoothState.TakingPhoto;
            _photoPaths.Add(PLACEHOLDER_IMAGE_PATH);
            _photosTaken++;
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
            Storyboard.SetTarget(scaleXAnim, (ScaleTransform)TakenPhotoImage.RenderTransform);
            Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
            Storyboard.SetTarget(scaleYAnim, (ScaleTransform)TakenPhotoImage.RenderTransform);
            Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
            Storyboard.SetTarget(fadeInAnim, TakenPhotoImage);
            Storyboard.SetTargetProperty(fadeInAnim, "Opacity");
            showPhotoSb.Children.Add(scaleXAnim);
            showPhotoSb.Children.Add(scaleYAnim);
            showPhotoSb.Children.Add(fadeInAnim);
            showPhotoSb.Begin();

            await Task.Delay(2500);

            var scaleXResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var scaleYResetAnim = new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var fadeOutAnimPhoto = new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(300) };

            Storyboard hidePhotoSb = new Storyboard();
            Storyboard.SetTarget(fadeOutAnimPhoto, TakenPhotoImage);
            Storyboard.SetTargetProperty(fadeOutAnimPhoto, "Opacity");
            hidePhotoSb.Children.Add(fadeOutAnimPhoto);
            if (_photosTaken < TOTAL_PHOTOS_TO_TAKE)
            {
                Storyboard.SetTarget(scaleXResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform);
                Storyboard.SetTargetProperty(scaleXResetAnim, "ScaleX");
                Storyboard.SetTarget(scaleYResetAnim, (ScaleTransform)TakenPhotoImage.RenderTransform);
                Storyboard.SetTargetProperty(scaleYResetAnim, "ScaleY");
                hidePhotoSb.Children.Add(scaleXResetAnim);
                hidePhotoSb.Children.Add(scaleYResetAnim);
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

            if (_photoPaths.Count > 0) Photo1Image.Source = new BitmapImage(new Uri(_photoPaths[0])); else Photo1Image.Source = null;
            if (_photoPaths.Count > 1) Photo2Image.Source = new BitmapImage(new Uri(_photoPaths[1])); else Photo2Image.Source = null;
            if (_photoPaths.Count > 2) Photo3Image.Source = new BitmapImage(new Uri(_photoPaths[2])); else Photo3Image.Source = null;

            PhotoGallery.Opacity = 0; // Already set in XAML, but good for clarity
            ActionButtonsPanel.Opacity = 0; // Already set in XAML

            PhotoGallery.Visibility = Visibility.Visible;
            ActionButtonsPanel.Visibility = Visibility.Visible;

            var galleryFadeIn = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500) };
            Storyboard.SetTarget(galleryFadeIn, PhotoGallery);
            Storyboard.SetTargetProperty(galleryFadeIn, "Opacity");

            var buttonsFadeIn = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500) };
            Storyboard.SetTarget(buttonsFadeIn, ActionButtonsPanel);
            Storyboard.SetTargetProperty(buttonsFadeIn, "Opacity");

            Storyboard reviewSb = new Storyboard();
            reviewSb.Children.Add(galleryFadeIn);
            reviewSb.Children.Add(buttonsFadeIn);
            reviewSb.Begin();
            await Task.CompletedTask;
        }

        private async void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState != PhotoBoothState.ReviewingPhotos) return;
            _currentState = PhotoBoothState.Saving;
            PhotoGallery.Visibility = Visibility.Collapsed;
            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            OverlayGrid.Visibility = Visibility.Visible;
            OverlayText.Text = "Saving...";
            await Task.Delay(2000);
            OverlayText.Text = "Done!";
            await Task.Delay(1500);
            OverlayGrid.Visibility = Visibility.Collapsed;
            _currentState = PhotoBoothState.Finished;
            if (this.Frame != null && this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }

        private async void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState != PhotoBoothState.ReviewingPhotos && _currentState != PhotoBoothState.Finished) return;

            // Resetting state explicitly before calling StartPhotoProcedure
            // _currentState = PhotoBoothState.Idle; 
            // StartPhotoProcedure now calls ResetProcedure() unconditionally at the start, so the above line is not strictly needed.
            await StartPhotoProcedure();
        }
    }
}