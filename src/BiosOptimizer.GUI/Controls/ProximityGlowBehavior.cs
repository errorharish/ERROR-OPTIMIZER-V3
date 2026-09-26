#nullable enable
using System.Windows;
using System.Windows.Controls;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Attached Behavior enabling cursor proximity lighting on any Border, Card3D, or reactive text element.
    /// </summary>
    public static class ProximityGlowBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(ProximityGlowBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public static readonly DependencyProperty IsReactiveTextProperty =
            DependencyProperty.RegisterAttached(
                "IsReactiveText",
                typeof(bool),
                typeof(ProximityGlowBehavior),
                new PropertyMetadata(false, OnIsReactiveTextChanged));

        public static bool GetIsReactiveText(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsReactiveTextProperty);
        }

        public static void SetIsReactiveText(DependencyObject obj, bool value)
        {
            obj.SetValue(IsReactiveTextProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement fe)
            {
                if ((bool)e.NewValue)
                {
                    fe.Loaded += Card_Loaded;
                    fe.Unloaded += Card_Unloaded;
                    if (fe.IsLoaded)
                    {
                        GlobalProximityGlowEngine.Instance.RegisterCard(fe);
                    }
                }
                else
                {
                    fe.Loaded -= Card_Loaded;
                    fe.Unloaded -= Card_Unloaded;
                    GlobalProximityGlowEngine.Instance.UnregisterCard(fe);
                }
            }
        }

        private static void Card_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                GlobalProximityGlowEngine.Instance.RegisterCard(fe);
            }
        }

        private static void Card_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                GlobalProximityGlowEngine.Instance.UnregisterCard(fe);
            }
        }

        private static void OnIsReactiveTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb)
            {
                if ((bool)e.NewValue)
                {
                    tb.Loaded += Text_Loaded;
                    tb.Unloaded += Text_Unloaded;
                    if (tb.IsLoaded)
                    {
                        GlobalProximityGlowEngine.Instance.RegisterReactiveText(tb);
                    }
                }
                else
                {
                    tb.Loaded -= Text_Loaded;
                    tb.Unloaded -= Text_Unloaded;
                    GlobalProximityGlowEngine.Instance.UnregisterReactiveText(tb);
                }
            }
        }

        private static void Text_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb)
            {
                GlobalProximityGlowEngine.Instance.RegisterReactiveText(tb);
            }
        }

        private static void Text_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb)
            {
                GlobalProximityGlowEngine.Instance.UnregisterReactiveText(tb);
            }
        }
    }
}
