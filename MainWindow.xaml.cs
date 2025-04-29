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
using Octokit;
using System.Net;
using System.IO.Compression;

namespace LeetStoreApp
{
    public partial class MainWindow : Window
    {
        private readonly string _appVersion = "1.0.0";
        private readonly string _githubUser = "enoughdrama";
        private readonly string _githubRepo = "LeetStore";
        private readonly string _installDir;
        private readonly GitHubClient _githubClient;
        private bool _isUpdating = false;
        private List<string> _filesToUpdate = new List<string>();
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private TimeSpan _updateCheckInterval = TimeSpan.FromHours(1);
        private string _cacheFilePath;

        public MainWindow()
        {
            InitializeComponent();
            
            _installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _githubClient = new GitHubClient(new ProductHeaderValue("LeetStore-Updater"));
            _cacheFilePath = Path.Combine(_installDir, "update_cache.json");
            
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
        
        private class CacheData
        {
            public List<FileInfo> Files { get; set; } = new List<FileInfo>();
            public DateTime LastUpdated { get; set; }
        }

        private class FileInfo
        {
            public string Name { get; set; }
            public string Sha { get; set; }
            public string DownloadUrl { get; set; }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                UpdateStatusText.Text = "Checking for updates...";
                
                // Check if we should use cached data to avoid rate limits
                var now = DateTime.Now;
                bool useCache = File.Exists(_cacheFilePath) && 
                               (now - _lastUpdateCheck) < _updateCheckInterval;
                
                List<FileInfo> filesInfo;
                
                if (useCache)
                {
                    // Use cached data
                    UpdateStatusText.Text = "Using cached repository data...";
                    filesInfo = LoadCachedData();
                }
                else
                {
                    try
                    {
                        UpdateStatusText.Text = "Fetching repository data...";
                        
                        // Get rate limit info first
                        var rateLimit = await _githubClient.Miscellaneous.GetRateLimits();
                        int remainingRequests = rateLimit.Resources.Core.Remaining;
                        
                        if (remainingRequests < 5)
                        {
                            var resetTime = rateLimit.Resources.Core.Reset.ToLocalTime();
                            UpdateStatusText.Text = $"GitHub API rate limit reached. Retry after {resetTime.ToShortTimeString()}";
                            
                            // If we have a cache, use it despite the interval
                            if (File.Exists(_cacheFilePath))
                            {
                                UpdateStatusText.Text = "Using cached data due to rate limits...";
                                filesInfo = LoadCachedData();
                            }
                            else
                            {
                                return;
                            }
                        }
                        else
                        {
                            // Fetch fresh data
                            var contents = await _githubClient.Repository.Content.GetAllContents(_githubUser, _githubRepo);
                            
                            filesInfo = contents
                                .Where(c => c.Type == ContentType.File)
                                .Select(c => new FileInfo 
                                { 
                                    Name = c.Name, 
                                    Sha = c.Sha,
                                    DownloadUrl = c.DownloadUrl
                                })
                                .ToList();
                                
                            // Update cache
                            SaveCacheData(filesInfo);
                            _lastUpdateCheck = now;
                        }
                    }
                    catch (RateLimitExceededException ex)
                    {
                        if (File.Exists(_cacheFilePath))
                        {
                            UpdateStatusText.Text = "Using cached data due to rate limits...";
                            filesInfo = LoadCachedData();
                        }
                        else
                        {
                            UpdateStatusText.Text = $"GitHub API rate limit exceeded. Please try again later.";
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (File.Exists(_cacheFilePath))
                        {
                            UpdateStatusText.Text = "Error accessing GitHub. Using cached data...";
                            filesInfo = LoadCachedData();
                        }
                        else
                        {
                            throw;
                        }
                    }
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
                        var remoteHash = fileInfo.Sha;
                        
                        if (localHash != remoteHash)
                        {
                            needsUpdate = true;
                        }
                    }
                    
                    if (needsUpdate)
                    {
                        _filesToUpdate.Add(fileInfo.Name);
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
        
        private void SaveCacheData(List<FileInfo> filesInfo)
        {
            try
            {
                var cacheData = new CacheData
                {
                    Files = filesInfo,
                    LastUpdated = DateTime.Now
                };
                
                string json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_cacheFilePath, json);
            }
            catch
            {
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
                int retryCount = 0;
                bool useDirectDownload = false;
                
                // Prepare a backup HttpClient for direct downloads as fallback
                using (HttpClient httpClient = new HttpClient())
                {
                    // Set appropriate headers to mimic a browser
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "LeetStore-Updater");
                    
                    foreach (var fileName in _filesToUpdate)
                    {
                        UpdateStatusText.Text = $"Updating {fileName}...";
                        
                        bool fileUpdated = false;
                        retryCount = 0;
                        
                        while (!fileUpdated && retryCount < 3)
                        {
                            try
                            {
                                byte[] fileContent;
                                
                                if (useDirectDownload)
                                {
                                    // Use direct URL download as fallback for rate limits
                                    string rawUrl = $"https://raw.githubusercontent.com/{_githubUser}/{_githubRepo}/main/{fileName}";
                                    fileContent = await httpClient.GetByteArrayAsync(rawUrl);
                                }
                                else
                                {
                                    // Try GitHub API first
                                    fileContent = await _githubClient.Repository.Content.GetRawContent(_githubUser, _githubRepo, fileName);
                                }
                                
                                var localFilePath = Path.Combine(_installDir, fileName);
                                
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
                                fileUpdated = true;
                            }
                            catch (RateLimitExceededException)
                            {
                                useDirectDownload = true;
                                retryCount++;
                                
                                if (retryCount >= 3)
                                {
                                    throw new Exception("GitHub API rate limit exceeded. Please try again later.");
                                }
                                
                                await Task.Delay(1000); // Wait before retry
                            }
                            catch (Exception ex)
                            {
                                retryCount++;
                                
                                if (retryCount >= 3)
                                {
                                    throw new Exception($"Failed to update {fileName} after multiple attempts: {ex.Message}");
                                }
                                
                                // Exponential backoff
                                await Task.Delay(1000 * retryCount); 
                            }
                        }
                        
                        processedFiles++;
                        UpdateProgressBar.Value = (double)processedFiles / totalFiles * 100;
                    }
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
