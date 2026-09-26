using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BiosOptimizer.GUI.Tools
{
    public static class InteractiveCardBehavior
    {
        public static readonly DependencyProperty IsInteractiveProperty =
            DependencyProperty.RegisterAttached("IsInteractive", typeof(bool), typeof(InteractiveCardBehavior), new PropertyMetadata(false, OnIsInteractiveChanged));

        public static bool GetIsInteractive(DependencyObject obj) => (bool)obj.GetValue(IsInteractiveProperty);
        public static void SetIsInteractive(DependencyObject obj, bool value) => obj.SetValue(IsInteractiveProperty, value);

        private static void OnIsInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.MouseMove += Element_MouseMove;
                    element.MouseLeave += Element_MouseLeave;
                    element.MouseEnter += Element_MouseEnter;
                }
                else
                {
                    element.MouseMove -= Element_MouseMove;
                    element.MouseLeave -= Element_MouseLeave;
                    element.MouseEnter -= Element_MouseEnter;
                }
            }
        }

        private static void Element_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is UIElement element && element.RenderTransform is TransformGroup group)
            {
                var scale = group.Children[0] as ScaleTransform;
                if (scale != null)
                {
                    var anim = new System.Windows.Media.Animation.DoubleAnimation(1.02, TimeSpan.FromMilliseconds(200));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }
            }
        }

        private static void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.RenderTransform is TransformGroup group)
            {
                var translate = group.Children[1] as TranslateTransform;
                if (translate != null)
                {
                    var pos = e.GetPosition(element);
                    var centerX = element.ActualWidth / 2;
                    var centerY = element.ActualHeight / 2;
                    
                    var moveX = (pos.X - centerX) / centerX * 2; // -2 to +2
                    var moveY = (pos.Y - centerY) / centerY * 2;
                    
                    translate.X = moveX;
                    translate.Y = moveY;
                }
            }
        }

        private static void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.RenderTransform is TransformGroup group)
            {
                var scale = group.Children[0] as ScaleTransform;
                if (scale != null)
                {
                    var anim = new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }
                var translate = group.Children[1] as TranslateTransform;
                if (translate != null)
                {
                    var animX = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                    translate.BeginAnimation(TranslateTransform.XProperty, animX);
                    translate.BeginAnimation(TranslateTransform.YProperty, animX);
                }
            }
        }
    }
}
