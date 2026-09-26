using System.Windows.Controls;
namespace BiosOptimizer.GUI.Views { public partial class ServiceManagerView : UserControl { public ServiceManagerView() { try { InitializeComponent(); } catch (System.Exception ex) { this.Content = new System.Windows.Controls.TextBlock { Text = "PAGE LOAD ERROR\n\nFailed to load the UI for this page.\n\nReason:\n" + ex.Message, Foreground = System.Windows.Media.Brushes.Red, Margin = new System.Windows.Thickness(32), TextWrapping = System.Windows.TextWrapping.Wrap }; } } } }

