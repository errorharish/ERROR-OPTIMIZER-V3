#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// A TextBlock that smoothly interpolates its displayed numeric value when the Value property changes.
    /// Visual-only — the backend value is never altered.
    /// Supports integer and decimal display with a configurable format string.
    /// Respects UIMotionEngine.IsAnimationEnabled and cleans up on Unloaded.
    /// </summary>
    public class AnimatedNumberTextBlock : TextBlock
    {
        // ── Dependency Properties ─────────────────────────────────────────

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(AnimatedNumberTextBlock),
                new PropertyMetadata(0.0, OnValueChanged));

        public static readonly DependencyProperty FormatStringProperty =
            DependencyProperty.Register(nameof(FormatString), typeof(string), typeof(AnimatedNumberTextBlock),
                new PropertyMetadata("{0:0}", OnFormatChanged));

        public static readonly DependencyProperty SuffixProperty =
            DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(AnimatedNumberTextBlock),
                new PropertyMetadata("", OnFormatChanged));

        // Internal animated property
        private static readonly DependencyProperty DisplayValueProperty =
            DependencyProperty.Register(nameof(DisplayValue), typeof(double), typeof(AnimatedNumberTextBlock),
                new PropertyMetadata(0.0, OnDisplayValueChanged));

        // ── Public Properties ─────────────────────────────────────────────

        /// <summary>The target numeric value to animate to.</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Format string for the displayed text. Use {0} for the number. Default "{0:0}"</summary>
        public string FormatString
        {
            get => (string)GetValue(FormatStringProperty);
            set => SetValue(FormatStringProperty, value);
        }

        /// <summary>Optional suffix appended after the formatted number (e.g. "%", " MB").</summary>
        public string Suffix
        {
            get => (string)GetValue(SuffixProperty);
            set => SetValue(SuffixProperty, value);
        }

        private double DisplayValue
        {
            get => (double)GetValue(DisplayValueProperty);
            set => SetValue(DisplayValueProperty, value);
        }

        // ── Private State ─────────────────────────────────────────────────

        private DoubleAnimation? _currentAnimation;
        private bool _isLoaded;

        // ── Constructor ───────────────────────────────────────────────────

        public AnimatedNumberTextBlock()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            // Snap display to current value immediately on first load
            DisplayValue = Value;
            UpdateText(DisplayValue);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            StopAnimation();
        }

        // ── Value Change Handler ──────────────────────────────────────────

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedNumberTextBlock ctrl)
                ctrl.AnimateTo((double)e.NewValue);
        }

        private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedNumberTextBlock ctrl)
                ctrl.UpdateText(ctrl.DisplayValue);
        }

        private static void OnDisplayValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedNumberTextBlock ctrl)
                ctrl.UpdateText((double)e.NewValue);
        }

        // ── Animation ─────────────────────────────────────────────────────

        private void AnimateTo(double targetValue)
        {
            if (!_isLoaded)
            {
                DisplayValue = targetValue;
                return;
            }

            if (!UIMotionEngine.Instance.IsAnimationEnabled)
            {
                DisplayValue = targetValue;
                return;
            }

            StopAnimation();

            _currentAnimation = new DoubleAnimation(
                fromValue: DisplayValue,
                toValue: targetValue,
                duration: new Duration(UIMotionEngine.Instance.GetDuration(DurationCategory.NumberInterpolation)))
            {
                EasingFunction = UIMotionEngine.Instance.GetEasing(EasingCategory.Decelerate),
                FillBehavior = FillBehavior.HoldEnd
            };

            _currentAnimation.Completed += (s, e) =>
            {
                // Snap to exact value when done
                _currentAnimation = null;
                DisplayValue = targetValue;
            };

            BeginAnimation(DisplayValueProperty, _currentAnimation);
        }

        private void StopAnimation()
        {
            if (_currentAnimation != null)
            {
                // Freeze at current interpolated position
                double current = DisplayValue;
                BeginAnimation(DisplayValueProperty, null);
                DisplayValue = current;
                _currentAnimation = null;
            }
        }

        // ── Text Update ───────────────────────────────────────────────────

        private void UpdateText(double value)
        {
            try
            {
                string formatted = string.Format(FormatString ?? "{0:0}", value);
                Text = formatted + (Suffix ?? "");
            }
            catch
            {
                Text = value.ToString("0") + (Suffix ?? "");
            }
        }
    }
}
