using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BiosOptimizer.GUI.ViewModels;

namespace BiosOptimizer.GUI.Views
{
    public partial class RegistryTweakView : UserControl
    {
        public RegistryTweakView()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[REGISTRY-TWEAK] Creating view");
                InitializeComponent();
                
                this.Loaded += OnLoaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[REGISTRY-TWEAK] FAILED\n" + ex.GetType() + "\n" + ex.Message + "\n" + ex.StackTrace);
                ShowFallbackError(ex);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[REGISTRY-TWEAK] Loading page");
                if (DataContext is RegistryTweakViewModel vm)
                {
                    System.Diagnostics.Debug.WriteLine("[REGISTRY-TWEAK] Setting DataContext & starting detection");
                    vm.OnLoaded();
                }
                System.Diagnostics.Debug.WriteLine("[REGISTRY-TWEAK] Ready");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[REGISTRY-TWEAK] LOAD FAILED\n" + ex.Message);
                ShowFallbackError(ex);
            }
        }

        private void ShowFallbackError(Exception ex)
        {
            this.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 15, 20)),
                Padding = new Thickness(40),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "REGISTRY TWEAK FAILED TO LOAD", Foreground = Brushes.Red, FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,20) },
                        new TextBlock { Text = "Reason:\n" + ex.Message, Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,20) },
                        new TextBlock { Text = "Stack Trace:\n" + ex.StackTrace, Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") }
                    }
                }
            };
        }
    }
}
