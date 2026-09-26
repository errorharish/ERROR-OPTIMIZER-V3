using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Attached behavior that animates child elements of a Panel in a staggered entrance sequence.
    /// Each child fades in and translates up from a small Y offset.
    /// 
    /// Usage in XAML:
    ///   &lt;StackPanel controls:StaggeredEntrance.IsEnabled="True"
    ///               controls:StaggeredEntrance.StaggerDelayMs="40"&gt;
    ///     &lt;Border .../&gt;
    ///     &lt;Border .../&gt;
    ///   &lt;/StackPanel&gt;
    /// 
    /// The host Panel must be Loaded for the animation to trigger.
    /// Animation respects UIMotionEngine.IsAnimationEnabled.
    /// Memory-safe: handlers are cleaned up on Unloaded.
    /// </summary>
    public static class StaggeredEntrance
    {
        // ── Attached Properties ───────────────────────────────────────────

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(StaggeredEntrance),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty StaggerDelayMsProperty =
            DependencyProperty.RegisterAttached("StaggerDelayMs", typeof(double), typeof(StaggeredEntrance),
                new PropertyMetadata(40.0));

        public static readonly DependencyProperty TranslateYAmountProperty =
            DependencyProperty.RegisterAttached("TranslateYAmount", typeof(double), typeof(StaggeredEntrance),
                new PropertyMetadata(14.0));

        public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

        public static double GetStaggerDelayMs(DependencyObject d) => (double)d.GetValue(StaggerDelayMsProperty);
        public static void SetStaggerDelayMs(DependencyObject d, double value) => d.SetValue(StaggerDelayMsProperty, value);

        public static double GetTranslateYAmount(DependencyObject d) => (double)d.GetValue(TranslateYAmountProperty);
        public static void SetTranslateYAmount(DependencyObject d, double value) => d.SetValue(TranslateYAmountProperty, value);

        // ── Tracking ──────────────────────────────────────────────────────

        // Map from panel to its Loaded handler so we can unhook on Unloaded
        private static readonly Dictionary<FrameworkElement, RoutedEventHandler> _loadedHandlers = new();
        private static readonly Dictionary<FrameworkElement, RoutedEventHandler> _unloadedHandlers = new();

        // ── Enable/Disable ────────────────────────────────────────────────

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement panel) return;

            if ((bool)e.NewValue)
            {
                RoutedEventHandler loadedHandler = (s, _) => RunEntrance(panel);
                RoutedEventHandler unloadedHandler = (s, _) => Cleanup(panel);

                _loadedHandlers[panel] = loadedHandler;
                _unloadedHandlers[panel] = unloadedHandler;

                panel.Loaded += loadedHandler;
                panel.Unloaded += unloadedHandler;

                // If already loaded, run immediately
                if (panel.IsLoaded)
                    RunEntrance(panel);
            }
            else
            {
                Cleanup(panel);
            }
        }

        private static void Cleanup(FrameworkElement panel)
        {
            if (_loadedHandlers.TryGetValue(panel, out var lh))
            {
                panel.Loaded -= lh;
                _loadedHandlers.Remove(panel);
            }
            if (_unloadedHandlers.TryGetValue(panel, out var uh))
            {
                panel.Unloaded -= uh;
                _unloadedHandlers.Remove(panel);
            }
        }

        // ── Entrance Animation ────────────────────────────────────────────

        private static void RunEntrance(FrameworkElement panel)
        {
            if (!UIMotionEngine.Instance.IsAnimationEnabled) return;

            var motion = UIMotionEngine.Instance;
            var staggerMs = GetStaggerDelayMs(panel);
            var translateY = GetTranslateYAmount(panel);
            var cardDuration = motion.GetDuration(DurationCategory.CardEntrance);
            var easing = motion.GetEasing(EasingCategory.Decelerate);

            int index = 0;
            foreach (var child in GetDirectChildren(panel))
            {
                double delayMs = index * staggerMs;
                AnimateChild(child, TimeSpan.FromMilliseconds(delayMs), cardDuration, translateY, easing);
                index++;
            }
        }

        private static void AnimateChild(FrameworkElement child, TimeSpan delay, TimeSpan duration, double translateY, IEasingFunction? easing)
        {
            // Set up transform if not already present
            if (child.RenderTransform is not TranslateTransform)
            {
                child.RenderTransform = new TranslateTransform(0, translateY);
                child.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else
            {
                ((TranslateTransform)child.RenderTransform).Y = translateY;
            }

            child.Opacity = 0;

            // Opacity animation
            var opacityAnim = new DoubleAnimation(0, 1, new Duration(duration))
            {
                BeginTime = delay,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            opacityAnim.Completed += (s, e) => child.BeginAnimation(UIElement.OpacityProperty, null);

            // Y translate animation
            var translateAnim = new DoubleAnimation(translateY, 0, new Duration(duration))
            {
                BeginTime = delay,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            translateAnim.Completed += (s, e) =>
            {
                child.BeginAnimation(UIElement.OpacityProperty, null);
                if (child.RenderTransform is TranslateTransform tt) tt.Y = 0;
                child.RenderTransform = new TranslateTransform(0, 0);
            };

            child.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            if (child.RenderTransform is TranslateTransform trans)
            {
                trans.BeginAnimation(TranslateTransform.YProperty, translateAnim);
            }
        }

        private static IEnumerable<FrameworkElement> GetDirectChildren(FrameworkElement panel)
        {
            int count = VisualTreeHelper.GetChildrenCount(panel);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(panel, i) is FrameworkElement fe)
                    yield return fe;
            }
        }
    }
}
