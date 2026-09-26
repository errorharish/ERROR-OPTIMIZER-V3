using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BiosOptimizer.GUI.Controls
{
    public static class GlassEffect
    {
        public static readonly DependencyProperty IsHoverHighlightEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsHoverHighlightEnabled",
                typeof(bool),
                typeof(GlassEffect),
                new PropertyMetadata(false, OnIsHoverHighlightEnabledChanged));

        public static bool GetIsHoverHighlightEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsHoverHighlightEnabledProperty);
        }

        public static void SetIsHoverHighlightEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsHoverHighlightEnabledProperty, value);
        }

        private static void OnIsHoverHighlightEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.MouseMove += Element_MouseMove;
                    element.MouseEnter += Element_MouseEnter;
                    element.MouseLeave += Element_MouseLeave;
                    element.Loaded += Element_Loaded;
                }
                else
                {
                    element.MouseMove -= Element_MouseMove;
                    element.MouseEnter -= Element_MouseEnter;
                    element.MouseLeave -= Element_MouseLeave;
                    element.Loaded -= Element_Loaded;
                }
            }
        }

        private static void Element_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                EnsureHighlightElement(element);
            }
        }

        private static void Element_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                EnsureHighlightElement(element);
                var highlight = GetHighlightElement(element);
                if (highlight != null)
                {
                    var anim = new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(200));
                    highlight.BeginAnimation(UIElement.OpacityProperty, anim);
                }
            }
        }

        private static void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var highlight = GetHighlightElement(element);
                if (highlight != null)
                {
                    var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(400));
                    highlight.BeginAnimation(UIElement.OpacityProperty, anim);
                }
            }
        }

        private static void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var highlight = GetHighlightElement(element);
                if (highlight != null)
                {
                    var pos = e.GetPosition(element);
                    var brush = highlight.Fill as RadialGradientBrush;
                    if (brush != null && element.ActualWidth > 0 && element.ActualHeight > 0)
                    {
                        var relativePoint = new Point(pos.X / element.ActualWidth, pos.Y / element.ActualHeight);
                        brush.Center = relativePoint;
                        brush.GradientOrigin = relativePoint;
                    }
                }
            }
        }

        private static void EnsureHighlightElement(FrameworkElement element)
        {
            if (GetHighlightElement(element) != null) return;
            
            if (element is Panel panel || (element is Border b && b.Child is Panel))
            {
                Panel targetPanel = element as Panel ?? (Panel)((Border)element).Child;
                
                var rect = new Rectangle
                {
                    IsHitTestVisible = false,
                    Opacity = 0.0,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(120, 229, 57, 53), 0.0), // Accent red glow
                            new GradientStop(Color.FromArgb(0, 229, 57, 53), 1.0)
                        },
                        RadiusX = 1.0,
                        RadiusY = 1.0
                    }
                };
                
                Grid.SetColumnSpan(rect, 10);
                Grid.SetRowSpan(rect, 10);
                Panel.SetZIndex(rect, 99); // Put it on top of other content in the panel if possible
                targetPanel.Children.Add(rect);
                SetHighlightElement(element, rect);
            }
        }

        public static readonly DependencyProperty HighlightElementProperty =
            DependencyProperty.RegisterAttached("HighlightElement", typeof(Rectangle), typeof(GlassEffect), new PropertyMetadata(null));

        public static Rectangle GetHighlightElement(DependencyObject obj)
        {
            return (Rectangle)obj.GetValue(HighlightElementProperty);
        }

        public static void SetHighlightElement(DependencyObject obj, Rectangle value)
        {
            obj.SetValue(HighlightElementProperty, value);
        }
    }
}
