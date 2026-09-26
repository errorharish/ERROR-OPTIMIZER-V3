#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Centralized Navigation Transition Host for Error Optimizer V3.
    /// Orchestrates smooth page transitions and completely clears RenderTransform
    /// in steady-state to guarantee 100% razor-sharp native ClearType text rendering.
    /// </summary>
    public class PageTransitionHost : ContentControl
    {
        private int _currentTransitionId = 0;

        static PageTransitionHost()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PageTransitionHost),
                new FrameworkPropertyMetadata(typeof(PageTransitionHost)));
        }

        public PageTransitionHost()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        }

        public void TransitionTo(object? newContent)
        {
            var motion = UI3DMotionEngine.Instance;
            if (!motion.IsTransitionsEnabled || App.IsSafeMode || App.IsNoEffectsMode)
            {
                BeginAnimation(OpacityProperty, null);
                RenderTransform = null;
                Content = newContent;
                Opacity = 1.0;
                return;
            }

            int transitionId = unchecked(++_currentTransitionId);

            var transformGroup = new TransformGroup();
            var translateTransform = new TranslateTransform(0, 0);
            transformGroup.Children.Add(translateTransform);
            RenderTransform = transformGroup;
            RenderTransformOrigin = new Point(0.5, 0.5);

            var outDuration = motion.GetDuration(DurationCategory.PageTransitionOut);
            var inDuration = motion.GetDuration(DurationCategory.PageTransitionIn);
            var ease = motion.GetEasing(EasingCategory.Decelerate);

            if (Content == null)
            {
                Content = newContent;
                Opacity = 0;
                translateTransform.Y = 6;

                var fadeInDirect = new DoubleAnimation(0, 1, new Duration(inDuration)) { EasingFunction = ease };
                var slideInDirect = new DoubleAnimation(6, 0, new Duration(inDuration)) { EasingFunction = ease };

                fadeInDirect.Completed += (s, e) =>
                {
                    if (transitionId != _currentTransitionId) return;
                    BeginAnimation(OpacityProperty, null);
                    Opacity = 1.0;
                    RenderTransform = null; // Clear completely for 100% native ClearType
                };

                BeginAnimation(OpacityProperty, fadeInDirect);
                translateTransform.BeginAnimation(TranslateTransform.YProperty, slideInDirect);
                return;
            }

            var fadeOut = new DoubleAnimation(Opacity, 0, new Duration(outDuration)) { EasingFunction = ease };
            var slideOut = new DoubleAnimation(translateTransform.Y, -6, new Duration(outDuration)) { EasingFunction = ease };

            fadeOut.Completed += (s, e) =>
            {
                if (transitionId != _currentTransitionId) return;

                Content = newContent;
                translateTransform.Y = 6;

                var fadeIn = new DoubleAnimation(0, 1, new Duration(inDuration)) { EasingFunction = ease };
                var slideIn = new DoubleAnimation(6, 0, new Duration(inDuration)) { EasingFunction = ease };

                fadeIn.Completed += (s2, e2) =>
                {
                    if (transitionId != _currentTransitionId) return;
                    BeginAnimation(OpacityProperty, null);
                    Opacity = 1.0;
                    RenderTransform = null; // Clear completely for 100% native ClearType
                };

                BeginAnimation(OpacityProperty, fadeIn);
                translateTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
            };

            BeginAnimation(OpacityProperty, fadeOut);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
        }
    }
}
