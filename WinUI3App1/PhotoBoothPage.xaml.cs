using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Threading;

namespace WinUI3App1
{
    // Separate Photo Booth page that can be navigated to from MainWindow
    public sealed partial class PhotoBoothPage : Page
    {
        private CanonCameraController _cameraController;
        private TextBlock _statusText;
        private Image _previewImage;
        private bool _isCountdownActive = false;
        private int _countdownValue = 3;
        private string _lastPhotoPath;

        // Setting controls
        private ComboBox _isoComboBox;
        private ComboBox _apertureComboBox;
        private ComboBox _shutterSpeedComboBox;
        private ComboBox _imageQualityComboBox;

        // Live view background loop
        private CancellationTokenSource _liveViewCancellationTokenSource;

        public PhotoBoothPage()
        {
            this.InitializeComponent();

            // Create save directory
            string photosDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "PhotoBoothApp");

            if (!Directory.Exists(photosDir))
            {
                Directory.CreateDirectory(photosDir);
            }

            // Set up UI
            SetupUI();

            // Initialize camera controller
            _cameraController = new CanonCameraController(App.Logger, photosDir);
        }

        private void SetupUI()
        {
            // Create a grid as the main container
            Grid mainGrid = new Grid();

            // Define rows
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(150) });

            // Define columns for settings panel
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(250) });

            // Create image preview area
            Grid previewGrid = new Grid();
            Border previewBorder = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10)
            };

            _previewImage = new Image
            {
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _statusText = new TextBlock
            {
                Text = "Connect to camera to start",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            previewGrid.Children.Add(_previewImage);
            previewGrid.Children.Add(_statusText);
            previewBorder.Child = previewGrid;

            Grid.SetRow(previewBorder, 0);
            Grid.SetColumn(previewBorder, 0);
            mainGrid.Children.Add(previewBorder);

            // Create settings panel
            ScrollViewer settingsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 10, 10, 10)
            };

            StackPanel settingsPanel = new StackPanel
            {
                Margin = new Thickness(10)
            };

            TextBlock settingsTitle = new TextBlock
            {
                Text = "Camera Settings",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            settingsPanel.Children.Add(settingsTitle);

            // ISO setting
            TextBlock isoLabel = new TextBlock
            {
                Text = "ISO Speed:",
                Margin = new Thickness(0, 5, 0, 2)
            };
            settingsPanel.Children.Add(isoLabel);

            _isoComboBox = new ComboBox
            {
                Width = double.NaN, // Auto width
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = false
            };
            settingsPanel.Children.Add(_isoComboBox);

            // Aperture setting
            TextBlock apertureLabel = new TextBlock
            {
                Text = "Aperture:",
                Margin = new Thickness(0, 10, 0, 2)
            };
            settingsPanel.Children.Add(apertureLabel);

            _apertureComboBox = new ComboBox
            {
                Width = double.NaN, // Auto width
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = false
            };
            settingsPanel.Children.Add(_apertureComboBox);

            // Shutter speed setting
            TextBlock shutterSpeedLabel = new TextBlock
            {
                Text = "Shutter Speed:",
                Margin = new Thickness(0, 10, 0, 2)
            };
            settingsPanel.Children.Add(shutterSpeedLabel);

            _shutterSpeedComboBox = new ComboBox
            {
                Width = double.NaN, // Auto width
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = false
            };
            settingsPanel.Children.Add(_shutterSpeedComboBox);

            // Image quality setting
            TextBlock imageQualityLabel = new TextBlock
            {
                Text = "Image Quality:",
                Margin = new Thickness(0, 10, 0, 2)
            };
            settingsPanel.Children.Add(imageQualityLabel);

            _imageQualityComboBox = new ComboBox
            {
                Width = double.NaN, // Auto width
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = false
            };
            settingsPanel.Children.Add(_imageQualityComboBox);

            // Apply settings button
            Button applySettingsButton = new Button
            {
                Content = "Apply Settings",
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = false
            };
            applySettingsButton.Click += ApplySettingsButton_Click;
            settingsPanel.Children.Add(applySettingsButton);

            settingsScroll.Content = settingsPanel;
            Grid.SetRow(settingsScroll, 0);
            Grid.SetColumn(settingsScroll, 1);
            mainGrid.Children.Add(settingsScroll);

            // Create buttons panel
            StackPanel buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 15,
                Margin = new Thickness(10)
            };

            // Connect button
            Button connectButton = new Button
            {
                Content = "Connect Camera",
                Padding = new Thickness(15, 8, 15, 8),
                MinWidth = 150
            };
            connectButton.Click += ConnectButton_Click;
            buttonsPanel.Children.Add(connectButton);

            // Take Picture button
            Button takePictureButton = new Button
            {
                Content = "Take Picture",
                Padding = new Thickness(15, 8, 15, 8),
                MinWidth = 150,
                IsEnabled = false
            };
            takePictureButton.Click += TakePictureButton_Click;
            buttonsPanel.Children.Add(takePictureButton);

            // Live View button
            Button liveViewButton = new Button
            {
                Content = "Start Live View",
                Padding = new Thickness(15, 8, 15, 8),
                MinWidth = 150,
                IsEnabled = false
            };
            liveViewButton.Click += LiveViewButton_Click;
            buttonsPanel.Children.Add(liveViewButton);

            // Countdown Toggle
            CheckBox countdownToggle = new CheckBox
            {
                Content = "3s Countdown",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0),
                IsEnabled = false
            };
            countdownToggle.Checked += CountdownToggle_CheckedChanged;
            countdownToggle.Unchecked += CountdownToggle_CheckedChanged;
            buttonsPanel.Children.Add(countdownToggle);

            Grid.SetRow(buttonsPanel, 1);
            Grid.SetColumn(buttonsPanel, 0);
            Grid.SetColumnSpan(buttonsPanel, 2);
            mainGrid.Children.Add(buttonsPanel);

            // Set content
            this.Content = mainGrid;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;

            if (button.Content.ToString() == "Connect Camera")
            {
                button.IsEnabled = false;
                _statusText.Text = "Connecting to camera...";

                try
                {
                    bool connected = _cameraController.Connect();

                    if (connected)
                    {
                        _statusText.Text = "Camera connected";
                        button.Content = "Disconnect Camera";

                        // Enable camera control buttons
                        foreach (var child in ((StackPanel)((Grid)this.Content).Children[2]).Children)
                        {
                            if (child is Button btn && btn != button)
                            {
                                btn.IsEnabled = true;
                            }
                            else if (child is CheckBox chk)
                            {
                                chk.IsEnabled = true;
                            }
                        }

                        // Enable settings controls
                        _isoComboBox.IsEnabled = true;
                        _apertureComboBox.IsEnabled = true;
                        _shutterSpeedComboBox.IsEnabled = true;
                        _imageQualityComboBox.IsEnabled = true;

                        // Find a Button with "Apply Settings" content
                        Button applyButton = null;
                        StackPanel settingsPanel = (StackPanel)((ScrollViewer)((Grid)this.Content).Children[1]).Content;
                        foreach (var child in settingsPanel.Children)
                        {
                            if (child is Button btn && btn.Content.ToString() == "Apply Settings")
                            {
                                btn.IsEnabled = true;
                                break;
                            }
                        }

                        // Load available camera settings
                        await LoadCameraSettings();
                    }
                    else
                    {
                        _statusText.Text = "Failed to connect to camera";

                        ContentDialog dialog = new ContentDialog
                        {
                            Title = "Connection Error",
                            Content = "Failed to connect to the camera. Ensure it's powered on and properly connected via USB.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };

                        await dialog.ShowAsync();
                    }
                }
                catch (Exception ex)
                {
                    _statusText.Text = $"Error: {ex.Message}";
                    App.Logger.Error(ex, "Error connecting to camera");
                }
                finally
                {
                    button.IsEnabled = true;
                }
            }
            else // Disconnect
            {
                button.IsEnabled = false;
                _statusText.Text = "Disconnecting camera...";

                try
                {
                    _cameraController.Disconnect();

                    _statusText.Text = "Camera disconnected";
                    button.Content = "Connect Camera";

                    // Disable camera control buttons
                    foreach (var child in ((StackPanel)((Grid)this.Content).Children[2]).Children)
                    {
                        if (child is Button btn && btn != button)
                        {
                            btn.IsEnabled = false;
                        }
                        else if (child is CheckBox chk)
                        {
                            chk.IsEnabled = false;
                        }
                    }

                    // Disable settings controls
                    _isoComboBox.IsEnabled = false;
                    _apertureComboBox.IsEnabled = false;
                    _shutterSpeedComboBox.IsEnabled = false;
                    _imageQualityComboBox.IsEnabled = false;

                    // Find a Button with "Apply Settings" content
                    Button applyButton = null;
                    StackPanel settingsPanel = (StackPanel)((ScrollViewer)((Grid)this.Content).Children[1]).Content;
                    foreach (var child in settingsPanel.Children)
                    {
                        if (child is Button btn && btn.Content.ToString() == "Apply Settings")
                        {
                            btn.IsEnabled = false;
                            break;
                        }
                    }

                    // Clear preview image
                    _previewImage.Source = null;
                }
                catch (Exception ex)
                {
                    _statusText.Text = $"Error: {ex.Message}";
                    App.Logger.Error(ex, "Error disconnecting camera");
                }
                finally
                {
                    button.IsEnabled = true;
                }
            }
        }

        private async Task LoadCameraSettings()
        {
            try
            {
                _statusText.Text = "Loading camera settings...";

                // Clear existing items
                _isoComboBox.Items.Clear();
                _apertureComboBox.Items.Clear();
                _shutterSpeedComboBox.Items.Clear();
                _imageQualityComboBox.Items.Clear();

                // Get current settings
                uint currentIso = 0;
                uint currentAperture = 0;
                uint currentShutterSpeed = 0;
                //bool success = _cameraController.GetCurrentSettings(out currentIso, out currentAperture, out currentShutterSpeed);

                // Load ISO options
                /*
                List<uint> isoValues = CameraPropertyMethods.GetAvailableIsoSpeeds(_cameraController.CameraRef, App.Logger);
                foreach (uint isoValue in isoValues)
                {
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = CameraPropertyMethods.IsoSpeedValueToString(isoValue),
                        Tag = isoValue
                    };

                    _isoComboBox.Items.Add(item);

                    if (isoValue == currentIso)
                    {
                        _isoComboBox.SelectedItem = item;
                    }
                }

                // Load aperture options
                List<uint> apertureValues = CameraPropertyMethods.GetAvailableApertures(_cameraController.CameraRef, App.Logger);
                foreach (uint apertureValue in apertureValues)
                {
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = CameraPropertyMethods.ApertureValueToFNumber(apertureValue),
                        Tag = apertureValue
                    };

                    _apertureComboBox.Items.Add(item);

                    if (apertureValue == currentAperture)
                    {
                        _apertureComboBox.SelectedItem = item;
                    }
                }

                // Load shutter speed options
                List<uint> shutterSpeedValues = CameraPropertyMethods.GetAvailableShutterSpeeds(_cameraController.CameraRef, App.Logger);
                foreach (uint tvValue in shutterSpeedValues)
                {
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = CameraPropertyMethods.ShutterSpeedValueToExposureTime(tvValue),
                        Tag = tvValue
                    };

                    _shutterSpeedComboBox.Items.Add(item);

                    if (tvValue == currentShutterSpeed)
                    {
                        _shutterSpeedComboBox.SelectedItem = item;
                    }
                }

                // Load image quality options
                List<uint> imageQualityValues = _cameraController.GetAvailableImageQualities();
                foreach (uint qualityValue in imageQualityValues)
                {
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = $"Quality {qualityValue}", // You could make a helper method to convert these values to readable names
                        Tag = qualityValue
                    };

                    _imageQualityComboBox.Items.Add(item);
                }
                */

                _statusText.Text = "Camera ready";
            }
            catch (Exception ex)
            {
                _statusText.Text = "Error loading camera settings";
                App.Logger.Error(ex, "Error loading camera settings");
            }
        }

        private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            button.IsEnabled = false;

            
            try
            {
                _statusText.Text = "Applying camera settings...";

                /*
                // Apply ISO setting
                if (_isoComboBox.SelectedItem != null)
                {
                    uint isoValue = (uint)((ComboBoxItem)_isoComboBox.SelectedItem).Tag;
                    _cameraController.SetIsoSpeed(isoValue);
                }

                // Apply aperture setting
                if (_apertureComboBox.SelectedItem != null)
                {
                    uint apertureValue = (uint)((ComboBoxItem)_apertureComboBox.SelectedItem).Tag;
                    _cameraController.SetAperture(apertureValue);
                }

                // Apply shutter speed setting
                if (_shutterSpeedComboBox.SelectedItem != null)
                {
                    uint shutterSpeedValue = (uint)((ComboBoxItem)_shutterSpeedComboBox.SelectedItem).Tag;
                    _cameraController.SetShutterSpeed(shutterSpeedValue);
                }

                // Apply image quality setting
                if (_imageQualityComboBox.SelectedItem != null)
                {
                    uint imageQualityValue = (uint)((ComboBoxItem)_imageQualityComboBox.SelectedItem).Tag;
                    _cameraController.SetImageQuality(imageQualityValue);
                }
                */

                _statusText.Text = "Settings applied";
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Error applying settings: {ex.Message}";
                App.Logger.Error(ex, "Error applying camera settings");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async void TakePictureButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            button.IsEnabled = false;

            try
            {
                _statusText.Text = "Taking picture...";

                // 1. Stop Live View fetching
                //_liveViewCancellationTokenSource?.Cancel();

                // 2. Tell the camera to take a photo
                bool success = _cameraController.TakePicture();

                if (success)
                {
                    _statusText.Text = "Picture taken!";

                    // 3. Small wait for camera to save
                    await Task.Delay(2000);

                    // 4. OPTIONAL: Load the latest captured image
                    // For now we'll just show a placeholder or black screen

                    // DVS: maybe not download it from camera, but use the low quality preview image only for on-screen

                    // If you have image download already (in ObjectEventHandler), you could load it here!

                    /*
                    var latestPhotoBytes = await LoadLatestPhotoBytesAsync();
                    if (latestPhotoBytes != null)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            using var stream = new MemoryStream(latestPhotoBytes);
                            var bitmap = new BitmapImage();
                            bitmap.SetSource(stream.AsRandomAccessStream());
                            _previewImage.Source = bitmap;
                        });

                        await Task.Delay(3000); // Show the photo for 3 seconds
                    }
                    */

                    await Task.Delay(3000); // Just wait 3 seconds for now
                }
                else
                {
                    _statusText.Text = "Failed to take picture.";
                }
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                // 5. Restart Live View
                if (_cameraController.StartLiveView())
                {
                    StartLiveViewDisplayLoop();
                    _statusText.Text = "Ready for next photo!";
                }
                else
                {
                    _statusText.Text = "Failed to restart Live View.";
                }

                button.IsEnabled = true;
            }
        }


        private async void LiveViewButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            button.IsEnabled = false;

            try
            {
                if (button.Content.ToString() == "Start Live View")
                {
                    _statusText.Text = "Starting Live View...";

                    bool success = _cameraController.StartLiveView();

                    if (success)
                    {
                        button.Content = "Stop Live View";
                        _statusText.Text = "Live View active";

                        // In a real implementation, you would download and display
                        // live view frames continuously here

                        StartLiveViewDisplayLoop(); // <-- start showing frames
                    }
                    else
                    {
                        _statusText.Text = "Failed to start Live View";

                        ContentDialog dialog = new ContentDialog
                        {
                            Title = "Live View Error",
                            Content = "Failed to start Live View. Make sure your camera supports this feature.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };

                        await dialog.ShowAsync();
                    }
                }
                else
                {
                    _statusText.Text = "Stopping Live View...";

                    // Stop continuous frame download
                    _liveViewCancellationTokenSource?.Cancel();

                    // Send stop live view command to the camera
                    bool success = _cameraController.StopLiveView();

                    if (success)
                    {
                        button.Content = "Start Live View";
                        _statusText.Text = "Live View stopped";
                    }
                    else
                    {
                        _statusText.Text = "Failed to stop Live View";
                    }
                }
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Error: {ex.Message}";
                App.Logger.Error(ex, "Error with Live View");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private void CountdownToggle_CheckedChanged(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = (CheckBox)sender;
            _isCountdownActive = checkBox.IsChecked ?? false;
        }

        // Clean up resources when navigating away from this page
        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // Make sure camera is disconnected when navigating away
            if (_cameraController != null)
            {
                _cameraController.Disconnect();
                _cameraController.Dispose();
                _cameraController = null;
            }
        }

        private async void StartLiveViewDisplayLoop()
        {
            _liveViewCancellationTokenSource = new CancellationTokenSource();
            var token = _liveViewCancellationTokenSource.Token;
            //const int targetFrameTimeMs = 66; // 15 fps
            const int targetFrameTimeMs = 45;


            try
            {
                while (!token.IsCancellationRequested)
                {
                    var frameStartTime = DateTime.UtcNow;

                    var frameData = await _cameraController.DownloadLiveViewFrameAsync();
                    if (frameData != null)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            using var stream = new MemoryStream(frameData);
                            var bitmap = new BitmapImage();
                            bitmap.SetSource(stream.AsRandomAccessStream());
                            _previewImage.Source = bitmap;
                        });
                    }

                    var frameDuration = (DateTime.UtcNow - frameStartTime).TotalMilliseconds;
                    var delay = Math.Max(0, targetFrameTimeMs - (int)frameDuration);

                    if (delay > 0)
                    {
                        await Task.Delay(delay, token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "LiveView display loop error");
            }
        }


    }
}