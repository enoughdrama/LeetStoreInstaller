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

        public MainWindow()
        {
            InitializeComponent();
            
            _installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _githubClient = new GitHubClient(new ProductHeaderValue("LeetStore-Updater"));
            
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
                
                var contents = await _githubClient.Repository.Content.GetAllContents(_githubUser, _githubRepo);
                
                if (contents != null && contents.Count > 0)
                {
                    foreach (var content in contents)
                    {
                        if (content.Type == ContentType.File)
                        {
                            var localFilePath = Path.Combine(_installDir, content.Name);
                            var needsUpdate = false;
                            
                            if (!File.Exists(localFilePath))
                            {
                                needsUpdate = true;
                            }
                            else
                            {
                                var localHash = CalculateFileHash(localFilePath);
                                var remoteHash = content.Sha;
                                
                                if (localHash != remoteHash)
                                {
                                    needsUpdate = true;
                                }
                            }
                            
                            if (needsUpdate)
                            {
                                _filesToUpdate.Add(content.Name);
                            }
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
                else
                {
                    UpdateStatusText.Text = "No files found in repository";
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
                
                foreach (var fileName in _filesToUpdate)
                {
                    UpdateStatusText.Text = $"Updating {fileName}...";
                    
                    var fileContent = await _githubClient.Repository.Content.GetRawContent(_githubUser, _githubRepo, fileName);
                    var localFilePath = Path.Combine(_installDir, fileName);
                    
                    File.WriteAllBytes(localFilePath, fileContent);
                    
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
