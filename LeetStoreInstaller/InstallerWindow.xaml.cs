using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private readonly string _backendUrl = "http://localhost:3000"; // Replace with your actual backend URL
        private readonly string _downloadUrl;
        private bool _installComplete = false;
        private HttpClient _httpClient;

        public class VersionInfo
        {
            public string Current { get; set; }
            public VersionEntry[] Versions { get; set; }
        }

        public class VersionEntry
        {
            public string Version { get; set; }
            public string ReleaseDate { get; set; }
            public string Notes { get; set; }
        }

        public class FileInfo
        {
            public string Name { get; set; }
            public long Size { get; set; }
            public string Hash { get; set; }
            public string Url { get; set; }
        }

        public class FilesResponse
        {
            public string Version { get; set; }
            public List<FileInfo> Files { get; set; }
        }

        public class CacheData
        {
            public string Version { get; set; }
            public List<FileInfo> Files { get; set; } = new List<FileInfo>();
            public DateTime LastUpdated { get; set; }
        }

        public InstallerWindow()
        {
            InitializeComponent();
            
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _downloadUrl = $"{_backendUrl}/api/latest";
            
            SetupWindow();
            CheckAdminRights();
            LoadVersionInfoAsync();
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
        
        private async void LoadVersionInfoAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"{_backendUrl}/api/version");
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(response);
                
                if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.Current))
                {
                    StatusText.Text = $"Ready to install version {versionInfo.Current}";
                    
                    // Find the current version entry for notes
                    var currentVersionEntry = Array.Find(versionInfo.Versions, v => v.Version == versionInfo.Current);
                    if (currentVersionEntry != null && !string.IsNullOrEmpty(currentVersionEntry.Notes))
                    {
                        // Could display release notes here if UI had a place for it
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Unable to connect to server";
                Debug.WriteLine($"Error loading version info: {ex.Message}");
            }
        }
        
        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_installComplete)
            {
                if (File.Exists(Path.Combine(_installPath, _appName)))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(_installPath, _appName),
                        UseShellExecute = true
                    });
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
                await Task.Run(async () => 
                {
                    Dispatcher.Invoke(() => InstallProgressBar.Value = 10);
                    Dispatcher.Invoke(() => StatusText.Text = "Creating installation directory...");
                    
                    if (!Directory.Exists(_installPath))
                    {
                        Directory.CreateDirectory(_installPath);
                    }
                    
                    Dispatcher.Invoke(() => InstallProgressBar.Value = 20);
                    Dispatcher.Invoke(() => StatusText.Text = "Downloading application files...");
                    
                    string tempZipFile = Path.Combine(Path.GetTempPath(), "LeetStoreApp.zip");
                    
                    int maxRetries = 3;
                    int retryCount = 0;
                    bool downloadSuccess = false;
                    
                    while (!downloadSuccess && retryCount < maxRetries)
                    {
                        try
                        {
                            using (var httpClient = new HttpClient())
                            {
                                httpClient.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for download
                                
                                Dispatcher.Invoke(() => StatusText.Text = "Downloading application package...");
                                
                                // Stream the download to a file
                                using (var response = await httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                                {
                                    response.EnsureSuccessStatusCode();
                                    
                                    long? totalBytes = response.Content.Headers.ContentLength;
                                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                                    using (var fileStream = new FileStream(tempZipFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                                    {
                                        byte[] buffer = new byte[8192];
                                        long totalBytesRead = 0;
                                        int bytesRead;
                                        
                                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                                        {
                                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                                            totalBytesRead += bytesRead;
                                            
                                            if (totalBytes.HasValue)
                                            {
                                                double progressPercentage = (double)totalBytesRead / totalBytes.Value * 40 + 20;
                                                Dispatcher.Invoke(() => InstallProgressBar.Value = progressPercentage);
                                            }
                                        }
                                    }
                                }
                            }
                            downloadSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            retryCount++;
                            
                            if (retryCount >= maxRetries)
                            {
                                Dispatcher.Invoke(() => StatusText.Text = "All download attempts failed.");
                                throw;
                            }
                            
                            // Wait before retry
                            int waitTime = (int)Math.Pow(2, retryCount);
                            Dispatcher.Invoke(() => StatusText.Text = $"Download error. Retrying in {waitTime} seconds...");
                            await Task.Delay(waitTime * 1000);
                        }
                    }
                    
                    Dispatcher.Invoke(() => InstallProgressBar.Value = 60);
                    Dispatcher.Invoke(() => StatusText.Text = "Extracting files...");
                    
                    // Ensure the installation directory is empty or backup existing files
                    if (Directory.Exists(_installPath) && Directory.GetFileSystemEntries(_installPath).Length > 0)
                    {
                        string backupDir = Path.Combine(Path.GetTempPath(), "LeetStoreBackup_" + DateTime.Now.Ticks);
                        Directory.CreateDirectory(backupDir);
                        
                        foreach (string item in Directory.GetFileSystemEntries(_installPath))
                        {
                            string destPath = Path.Combine(backupDir, Path.GetFileName(item));
                            
                            if (Directory.Exists(item))
                            {
                                DirectoryCopy(item, destPath, true);
                            }
                            else
                            {
                                System.IO.File.Copy(item, destPath, true);
                            }
                        }
                        
                        Directory.Delete(_installPath, true);
                        Directory.CreateDirectory(_installPath);
                    }
                    
                    ZipFile.ExtractToDirectory(tempZipFile, _installPath, true);
                    
                    Dispatcher.Invoke(() => InstallProgressBar.Value = 80);
                    Dispatcher.Invoke(() => StatusText.Text = "Creating shortcuts...");
                    
                    CreateDesktopShortcut();
                    CreateStartMenuShortcut();
                    
                    System.IO.File.Delete(tempZipFile);
                    
                    Dispatcher.Invoke(() => InstallProgressBar.Value = 100);
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
        
        private void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            System.IO.DirectoryInfo dir = new System.IO.DirectoryInfo(sourceDirName);
            System.IO.DirectoryInfo[] dirs = dir.GetDirectories();

            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            System.IO.FileInfo[] files = dir.GetFiles();
            foreach (System.IO.FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, true);
            }

            if (copySubDirs)
            {
                foreach (System.IO.DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
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
        
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}
