using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Text.Json;
using System.Net;
using System.IO.Compression;

namespace LeetStoreApp
{
    public partial class MainWindow : Window
    {
        private readonly string _appVersion = "1.0.0";
        private readonly string _backendUrl = "http://localhost:3000"; // Replace with your actual backend URL
        private readonly string _installDir;
        private bool _isUpdating = false;
        private List<FileInfo> _filesToUpdate = new List<FileInfo>();
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private TimeSpan _updateCheckInterval = TimeSpan.FromHours(1);
        private string _cacheFilePath;
        private HttpClient _httpClient;

        public class VersionInfo
        {
            public string Current { get; set; }
            public List<VersionEntry> Versions { get; set; }
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

        public MainWindow()
        {
            InitializeComponent();
            
            _installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _cacheFilePath = Path.Combine(_installDir, "update_cache.json");
            _httpClient = new HttpClient();
            
            // Set timeout to avoid hanging
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            SetupWindow();
            CheckForUpdatesAsync();
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
            
            VersionLabel.Content = $"Version {_appVersion}";
            UpdateProgressBar.Visibility = Visibility.Hidden;
            
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
        }
        
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                UpdateStatusText.Text = "Checking for updates...";
                
                // Check if we should use cached data
                var now = DateTime.Now;
                bool useCache = File.Exists(_cacheFilePath) && 
                               (now - _lastUpdateCheck) < _updateCheckInterval;
                
                List<FileInfo> filesInfo;
                string serverVersion;
                
                if (useCache)
                {
                    // Use cached data
                    UpdateStatusText.Text = "Using cached data...";
                    var cacheData = LoadCachedData();
                    filesInfo = cacheData.Files;
                    serverVersion = cacheData.Version;
                }
                else
                {
                    try
                    {
                        UpdateStatusText.Text = "Connecting to server...";
                        
                        // Get files data from server
                        using (var response = await _httpClient.GetAsync($"{_backendUrl}/api/files"))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                throw new Exception($"Server returned {response.StatusCode}");
                            }
                            
                            var responseContent = await response.Content.ReadAsStringAsync();
                            var filesResponse = JsonSerializer.Deserialize<FilesResponse>(responseContent);
                            
                            filesInfo = filesResponse.Files;
                            serverVersion = filesResponse.Version;
                            
                            // Update cache
                            SaveCacheData(filesInfo, serverVersion);
                            _lastUpdateCheck = now;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (File.Exists(_cacheFilePath))
                        {
                            UpdateStatusText.Text = "Error connecting to server. Using cached data...";
                            var cacheData = LoadCachedData();
                            filesInfo = cacheData.Files;
                            serverVersion = cacheData.Version;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                
                // Compare versions
                if (serverVersion != _appVersion)
                {
                    UpdateStatusText.Text = $"New version available: {serverVersion}";
                    UpdateButton.Content = "Update to version " + serverVersion;
                    UpdateButton.Visibility = Visibility.Visible;
                    return;
                }
                
                // Process files data to find updates needed
                _filesToUpdate.Clear();
                
                foreach (var fileInfo in filesInfo)
                {
                    var localFilePath = Path.Combine(_installDir, fileInfo.Name);
                    var needsUpdate = false;
                    
                    if (!File.Exists(localFilePath))
                    {
                        needsUpdate = true;
                    }
                    else
                    {
                        var localHash = CalculateFileHash(localFilePath);
                        var remoteHash = fileInfo.Hash;
                        
                        if (localHash != remoteHash)
                        {
                            needsUpdate = true;
                        }
                    }
                    
                    if (needsUpdate)
                    {
                        _filesToUpdate.Add(fileInfo);
                    }
                }
                
                if (_filesToUpdate.Count > 0)
                {
                    UpdateStatusText.Text = $"Updates available: {_filesToUpdate.Count} files need updating";
                    UpdateButton.Visibility = Visibility.Visible;
                }
                else
                {
                    UpdateStatusText.Text = "Your application is up to date";
                    UpdateButton.Visibility = Visibility.Hidden;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Error checking for updates: {ex.Message}";
            }
        }
        
        private string CalculateFileHash(string filePath)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        
        private List<FileInfo> LoadCachedData()
        {
            try
            {
                string json = File.ReadAllText(_cacheFilePath);
                var cacheData = JsonSerializer.Deserialize<CacheData>(json);
                return cacheData?.Files ?? new List<FileInfo>();
            }
            catch
            {
                return new List<FileInfo>();
            }
        }
        
        private void SaveCacheData(List<FileInfo> filesInfo, string version)
        {
            try
            {
                var cacheData = new CacheData
                {
                    Files = filesInfo,
                    Version = version,
                    LastUpdated = DateTime.Now
                };
                
                string json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_cacheFilePath, json);
            }
            catch
            {
                // Ignore cache save errors
            }
        }
        
        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
                return;
                
            _isUpdating = true;
            UpdateButton.IsEnabled = false;
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;
            
            try
            {
                int totalFiles = _filesToUpdate.Count;
                int processedFiles = 0;
                
                foreach (var fileInfo in _filesToUpdate)
                {
                    UpdateStatusText.Text = $"Updating {fileInfo.Name}...";
                    
                    try
                    {
                        // Download the file
                        string fileUrl = _backendUrl + fileInfo.Url;
                        byte[] fileContent = await _httpClient.GetByteArrayAsync(fileUrl);
                        
                        var localFilePath = Path.Combine(_installDir, fileInfo.Name);
                        
                        // Create directory if it doesn't exist
                        var directory = Path.GetDirectoryName(localFilePath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        
                        // Backup the existing file if it exists
                        if (File.Exists(localFilePath))
                        {
                            string backupPath = localFilePath + ".bak";
                            if (File.Exists(backupPath))
                                File.Delete(backupPath);
                                
                            File.Copy(localFilePath, backupPath);
                        }
                        
                        // Write the new file
                        File.WriteAllBytes(localFilePath, fileContent);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to update {fileInfo.Name}: {ex.Message}");
                    }
                    
                    processedFiles++;
                    UpdateProgressBar.Value = (double)processedFiles / totalFiles * 100;
                }
                
                UpdateStatusText.Text = "Update completed successfully";
                
                var animation = new ColorAnimation
                {
                    To = Colors.LightGreen,
                    Duration = TimeSpan.FromSeconds(0.3)
                };
                
                var brush = new SolidColorBrush(Colors.LightGray);
                UpdateStatusText.Foreground = brush;
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                
                _filesToUpdate.Clear();
                UpdateButton.Visibility = Visibility.Hidden;
                
                // Force refresh the cache after a successful update
                try
                {
                    if (File.Exists(_cacheFilePath))
                        File.Delete(_cacheFilePath);
                }
                catch { }
                
                await Task.Delay(2000);
                
                MessageBoxResult result = MessageBox.Show("Application has been updated successfully. Would you like to restart now?", "Update Complete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    RestartApplication();
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Error updating files: {ex.Message}";
                MessageBox.Show($"Error updating application: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            _isUpdating = false;
            UpdateButton.IsEnabled = true;
        }
        
        private void RestartApplication()
        {
            string appPath = Assembly.GetExecutingAssembly().Location;
            Process.Start(appPath);
            Application.Current.Shutdown();
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
