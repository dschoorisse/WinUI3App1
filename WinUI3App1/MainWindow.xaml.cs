// WinUI3App1/MainWindow.xaml.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI3App;
using System.Collections.ObjectModel; // Required for ObservableCollection
using System.Linq; // Required for .Any()
using System.Collections.Specialized; // Required for INotifyCollectionChanged

namespace WinUI3App1
{
    // The converter class is no longer needed for the status overlay
    /*
    public class IntToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (int)value > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    */

    public sealed partial class MainWindow : Window
    {
        public Frame AppFrame => ContentFrame;

        public MainWindow()
        {
            this.InitializeComponent();

            // FIX: Manually set the ItemsSource and handle visibility changes through events
            StatusItemsControl.ItemsSource = App.StatusItems;
            App.StatusItems.CollectionChanged += StatusItems_CollectionChanged;

            // Set initial visibility
            UpdateStatusOverlayVisibility();

            // Navigate to the MainPage (welcome screen)
            ContentFrame.Navigate(typeof(MainPage));
        }

        private void StatusItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // The collection has changed, so update the visibility of the overlay.
            // This needs to run on the UI thread.
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateStatusOverlayVisibility();
            });
        }

        private void UpdateStatusOverlayVisibility()
        {
            // Show the overlay if there are any items, otherwise hide it.
            StatusOverlayGrid.Visibility = App.StatusItems.Any() ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
