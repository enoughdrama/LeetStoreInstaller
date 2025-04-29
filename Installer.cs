using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.IO.Compression;

namespace LeetStoreInstaller
{
    public partial class InstallerWindow : Window
    {
        private readonly string _installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LeetStore");
        private readonly string _appName = "LeetStoreApp.exe";
        private readonly string _githubReleaseUrl = "https://github.com/enoughdrama/LeetStore/releases/latest/download/LeetStoreApp.zip";
        private bool _installComplete = false;

        public InstallerWindow()
        {
            InitializeComponent();
            
            SetupWindow();
            CheckAdminRights();
        }

        private void SetupWindow()
        {
            var converter = new BrushConverter();
            Background = (Brush)converter.ConvertFromString("#F5F5F7");
            
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(5),
                CornerRadius = new CornerRadius(8),
                GlassFrameThickness = new Thickness(0)
            });
            
            var dropShadowEffect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 320,
                ShadowDepth = 3,
                Opacity = 0.2,
                BlurRadius = 10
            };
            
            MainPanel.Effect = dropShadowEffect;
            
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            this.BeginAnimation(UIElement.OpacityProperty, animation);
            
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform());
            MainPanel.RenderTransform = transformGroup;
            
            var scaleAnimation = new DoubleAnimation
            {
                From = 0.95,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            ((TransformGroup)MainPanel.RenderTransform).Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            ((TransformGroup)MainPanel.RenderTransform).Children[0].BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

            InstallButton.Content = "Install";
            InstallProgressBar.Visibility = Visibility.Hidden;
        }

        private void CheckAdminRights()
        {
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (!isAdmin)
            {
                MessageBox.Show("This installer requires administrator privileges. Please run as administrator.", "Administrator Rights Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "Please restart installer as administrator";
                InstallButton.IsEnabled = false;
            }
        }
        
        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_installComplete)
            {
                if (File.Exists(Path.Combine(_installPath, _appName)))
                {
                    Process.Start(Path.Combine(_installPath, _appName));
                }
                
                Application.Current.Shutdown();
                return;
            }
            
            InstallButton.IsEnabled = false;
            InstallProgressBar.Visibility = Visibility.Visible;
            InstallProgressBar.Value = 0;
            StatusText.Text = "Preparing installation...";
            
            try
            {
                await Task.Run(() => 
                {
                    InstallProgressBar.Dispatcher.Invoke(() => InstallProgressBar.Value = 10);
                    StatusText.Dispatcher.Invoke(() => StatusText.Text = "Creating installation directory...");
                    
                    if (!Directory.Exists(_installPath))
                    {
                        Directory.CreateDirectory(_installPath);
                    }
                    
                    InstallProgressBar.Dispatcher.Invoke(() => InstallProgressBar.Value = 20);
                    StatusText.Dispatcher.Invoke(() => StatusText.Text = "Downloading application files...");
                    
                    string tempZipFile = Path.Combine(Path.GetTempPath(), "LeetStoreApp.zip");
                    
                    using (WebClient client = new WebClient())
                    {
                        client.DownloadFile(_githubReleaseUrl, tempZipFile);
                    }
                    
                    InstallProgressBar.Dispatcher.Invoke(() => InstallProgressBar.Value = 60);
                    StatusText.Dispatcher.Invoke(() => StatusText.Text = "Extracting files...");
                    
                    ZipFile.ExtractToDirectory(tempZipFile, _installPath, true);
                    
                    InstallProgressBar.Dispatcher.Invoke(() => InstallProgressBar.Value = 80);
                    StatusText.Dispatcher.Invoke(() => StatusText.Text = "Creating shortcuts...");
                    
                    CreateDesktopShortcut();
                    CreateStartMenuShortcut();
                    
                    File.Delete(tempZipFile);
                    
                    InstallProgressBar.Dispatcher.Invoke(() => InstallProgressBar.Value = 100);
                });
                
                StatusText.Text = "Installation completed successfully!";
                InstallButton.Content = "Launch Application";
                _installComplete = true;
                InstallButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Installation failed: {ex.Message}";
                MessageBox.Show($"Installation failed: {ex.Message}", "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                InstallButton.IsEnabled = true;
            }
        }
        
        private void CreateDesktopShortcut()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string shortcutPath = Path.Combine(desktopPath, "LeetStore.lnk");
            
            IWshRuntimeLibrary.WshShell shell = new IWshRuntimeLibrary.WshShell();
            IWshRuntimeLibrary.IWshShortcut shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            
            shortcut.TargetPath = Path.Combine(_installPath, _appName);
            shortcut.WorkingDirectory = _installPath;
            shortcut.Description = "LeetStore Application";
            shortcut.IconLocation = Path.Combine(_installPath, _appName);
            shortcut.Save();
        }
        
        private void CreateStartMenuShortcut()
        {
            string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "LeetStore");
            
            if (!Directory.Exists(startMenuPath))
            {
                Directory.CreateDirectory(startMenuPath);
            }
            
            string shortcutPath = Path.Combine(startMenuPath, "LeetStore.lnk");
            
            IWshRuntimeLibrary.WshShell shell = new IWshRuntimeLibrary.WshShell();
            IWshRuntimeLibrary.IWshShortcut shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            
            shortcut.TargetPath = Path.Combine(_installPath, _appName);
            shortcut.WorkingDirectory = _installPath;
            shortcut.Description = "LeetStore Application";
            shortcut.IconLocation = Path.Combine(_installPath, _appName);
            shortcut.Save();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        
        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }
    }
}
