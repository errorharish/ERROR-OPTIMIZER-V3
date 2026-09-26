using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

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
                    element.MouseEnter += Element_MouseEnter;
                    element.MouseLeave += Element_MouseLeave;
                }
                else
                {
                    element.MouseEnter -= Element_MouseEnter;
                    element.MouseLeave -= Element_MouseLeave;
                }
            }
        }

        private static void Element_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!BiosOptimizer.GUI.Services.AppSettingsService.Instance.AnimationsEnabled || BiosOptimizer.GUI.Services.AppSettingsService.Instance.ReducedMotion) return;

            if (sender is UIElement element && element.RenderTransform is TransformGroup group)
            {
                var translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (translate != null)
                {
                    var anim = new DoubleAnimation(-2.0, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    translate.BeginAnimation(TranslateTransform.YProperty, anim);
                }
            }
        }

        private static void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.RenderTransform is TransformGroup group)
            {
                var translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (translate != null)
                {
                    var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    translate.BeginAnimation(TranslateTransform.YProperty, anim);
                }
            }
        }
    }
}
