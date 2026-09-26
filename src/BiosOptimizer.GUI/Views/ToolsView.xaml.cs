using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BiosOptimizer.GUI.Views
{
    /// <summary>
    /// Interaction logic for ToolsView.xaml
    /// </summary>
    public partial class ToolsView : UserControl
    {
        public ToolsView()
        {
            InitializeComponent();
            SizeChanged += OnToolsViewSizeChanged;
        }

        private void OnToolsViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var grid = FindVisualChild<UniformGrid>(this);
            if (grid != null)
            {
                if (e.NewSize.Width < 680)
                    grid.Columns = 1;
                else if (e.NewSize.Width > 1650)
                    grid.Columns = 3;
                else
                    grid.Columns = 2;
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }
    }
}


