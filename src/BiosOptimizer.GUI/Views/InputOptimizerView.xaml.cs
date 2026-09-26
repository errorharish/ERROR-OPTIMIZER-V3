using System.Windows;
using System.Windows.Controls;
using BiosOptimizer.GUI.ViewModels;

namespace BiosOptimizer.GUI.Views
{
    public partial class InputOptimizerView : UserControl
    {
        public InputOptimizerView()
        {
            try { InitializeComponent(); } catch (System.Exception ex) { this.Content = new System.Windows.Controls.TextBlock { Text = "INPUT OPTIMIZER UNAVAILABLE\n\nUnable to load the Input Optimizer interface.\n\nReason:\n" + ex.Message, Foreground = System.Windows.Media.Brushes.Red, Margin = new System.Windows.Thickness(32) }; }
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Handled by NavigationService via OnNavigatedToAsync
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Handled by NavigationService via OnNavigatedFromAsync
        }
    }
}

