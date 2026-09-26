using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BiosOptimizer.GUI
{
    /// <summary>
    /// Icon + label content for sidebar navigation buttons.
    /// </summary>
    public class SidebarNavItem : StackPanel
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SidebarNavItem),
                new PropertyMetadata(string.Empty, OnIconChanged));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(SidebarNavItem),
                new PropertyMetadata(string.Empty, OnLabelChanged));

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(SidebarNavItem),
                new PropertyMetadata(false, OnActiveChanged));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        private Path _iconPath;
        private TextBlock _labelText;

        public SidebarNavItem()
        {
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(0);
            BuildContent();
        }

        private void BuildContent()
        {
            // Icon container
            var iconContainer = new Border
            {
                Width = 32, Height = 32,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(0, 229, 57, 53)),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            _iconPath = new Path
            {
                Width = 16, Height = 16,
                Stretch = Stretch.Uniform,
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromArgb(140, 157, 163, 176)),
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            iconContainer.Child = _iconPath;
            Children.Add(iconContainer);

            // Label
            _labelText = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 157, 163, 176)),
                FontWeight = FontWeights.Normal
            };
            Children.Add(_labelText);

            UpdateIcon();
            UpdateLabel();
            UpdateActive();
        }

        private void UpdateIcon()
        {
            if (_iconPath == null || string.IsNullOrEmpty(Icon)) return;
            try { _iconPath.Data = Geometry.Parse(Icon); }
            catch { /* ignore parse errors */ }
        }

        private void UpdateLabel()
        {
            if (_labelText != null) _labelText.Text = Label;
        }

        private void UpdateActive()
        {
            if (_iconPath == null) return;
            if (IsActive)
            {
                _iconPath.Stroke = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                _labelText.Foreground = new SolidColorBrush(Color.FromRgb(240, 241, 243));
                _labelText.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                _iconPath.Stroke = new SolidColorBrush(Color.FromArgb(140, 157, 163, 176));
                _labelText.Foreground = new SolidColorBrush(Color.FromArgb(160, 157, 163, 176));
                _labelText.FontWeight = FontWeights.Normal;
            }
        }

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as SidebarNavItem)?.UpdateIcon();

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as SidebarNavItem)?.UpdateLabel();

        private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as SidebarNavItem)?.UpdateActive();
    }
}
