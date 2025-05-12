using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI3App;

namespace WinUI3App1
{
    public sealed partial class MainWindow : Window
    {
        public Frame AppFrame => ContentFrame; // Assumes your Frame in MainWindow.xaml is x:Name="ContentFrame"

        public MainWindow()
        {
            this.InitializeComponent();

            // Navigate to the MainPage (welcome screen)
            ContentFrame.Navigate(typeof(MainPage));
        }
    }
}