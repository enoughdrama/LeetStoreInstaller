using System;
using System.Windows;

namespace LeetStoreApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                MessageBox.Show($"An unexpected error occurred: {args.ExceptionObject}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}
