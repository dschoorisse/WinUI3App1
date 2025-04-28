using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUI3App1
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            // Optional: Set DLL directory if needed
            string dllPath = Path.Combine(AppContext.BaseDirectory);
            SetDllDirectory(dllPath);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private void myButton_Click(object sender, RoutedEventArgs e)
        {
            myButton.Content = "Clicked";
        }

        private void testSDKButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Step 1: Check process architecture
                resultText.Text = $"Process is {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}\n";

                // Step 2: Check if DLL exists
                string dllPath = Path.Combine(Path.Combine(AppContext.BaseDirectory, "EDSDK"), "EDSDK.dll");
                bool exists = File.Exists(dllPath);
                resultText.Text += $"EDSDK.dll exists: {exists}\n";

                if (!exists)
                {
                    // Try alternative path
                    dllPath = Path.Combine(AppContext.BaseDirectory, "NativeDlls", "EDSDK.dll");
                    exists = File.Exists(dllPath);
                    resultText.Text += $"EDSDK.dll in NativeDlls exists: {exists}\n";
                }

                if (exists)
                {
                    // Step 3: Try to initialize with error handling around each step
                    resultText.Text += "Attempting SDK initialization...\n";

                    uint err = EDSDK.EdsInitializeSDK();
                    resultText.Text += $"SDK initialization result: 0x{err:X}\n";

                    if (err == EDSDK.EDS_ERR_OK)
                    {
                        // Successfully initialized
                        EDSDK.EdsTerminateSDK();
                        resultText.Text += "SDK was initialized and terminated successfully!";
                    }
                }
            }
            catch (DllNotFoundException ex)
            {
                resultText.Text += $"DLL not found: {ex.Message}";
            }
            catch (BadImageFormatException ex)
            {
                resultText.Text += $"Architecture mismatch: {ex.Message}\n" +
                                  "This usually means you're mixing 32-bit and 64-bit components";
            }
            catch (AccessViolationException ex)
            {
                resultText.Text += $"Memory access violation: {ex.Message}\n" +
                                  "This may indicate a calling convention or parameter marshaling issue";
            }
            catch (Exception ex)
            {
                resultText.Text += $"Exception: {ex.GetType().Name}\n{ex.Message}";
            }
        }
    }
}