using System.Windows.Controls;
using System.Windows.Input;
using BiosOptimizer.GUI.ViewModels;

namespace BiosOptimizer.GUI.Views
{
    public partial class AboutView : UserControl
    {
        public AboutView()
        {
            try
            {
                InitializeComponent();
                Loaded += (s, e) =>
                {
                    var win = System.Windows.Window.GetWindow(this);
                    if (win != null)
                    {
                        win.PreviewKeyDown += OnWindowPreviewKeyDown;
                    }
                };
                Unloaded += (s, e) =>
                {
                    (DataContext as AboutViewModel)?.CancelLoading();
                    var win = System.Windows.Window.GetWindow(this);
                    if (win != null)
                    {
                        win.PreviewKeyDown -= OnWindowPreviewKeyDown;
                    }
                };
            }
            catch (System.Exception ex)
            {
                this.Content = new System.Windows.Controls.TextBlock
                {
                    Text = "PAGE LOAD ERROR\n\nFailed to load the UI for this page.\n\nReason:\n" + ex.Message,
                    Foreground = System.Windows.Media.Brushes.Red,
                    Margin = new System.Windows.Thickness(32),
                    TextWrapping = System.Windows.TextWrapping.Wrap
                };
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (DataContext is AboutViewModel vm && (vm.IsOptimizationModalOpen || vm.IsProfileModalOpen))
                {
                    vm.IsOptimizationModalOpen = false;
                    vm.IsProfileModalOpen = false;
                    e.Handled = true;
                }
            }
        }
    }
}

