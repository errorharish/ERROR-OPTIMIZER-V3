using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace BiosOptimizer.GUI.Controls
{
    public partial class OptimizationProgressControl : UserControl
    {
        public static readonly DependencyProperty OptimizationProgressProperty =
            DependencyProperty.Register(nameof(OptimizationProgress), typeof(double), typeof(OptimizationProgressControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnProgressChanged));

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(OptimizationProgressControl),
                new PropertyMetadata("OPTIMIZING SYSTEM"));

        public static readonly DependencyProperty CurrentActionProperty =
            DependencyProperty.Register(nameof(CurrentAction), typeof(string), typeof(OptimizationProgressControl),
                new PropertyMetadata("Preparing optimization engine..."));

        public static readonly DependencyProperty CurrentStepProperty =
            DependencyProperty.Register(nameof(CurrentStep), typeof(int), typeof(OptimizationProgressControl),
                new PropertyMetadata(0, OnStepsChanged));

        public static readonly DependencyProperty TotalStepsProperty =
            DependencyProperty.Register(nameof(TotalSteps), typeof(int), typeof(OptimizationProgressControl),
                new PropertyMetadata(0, OnStepsChanged));

        public static readonly DependencyProperty SuccessCountProperty =
            DependencyProperty.Register(nameof(SuccessCount), typeof(int), typeof(OptimizationProgressControl),
                new PropertyMetadata(0));

        public static readonly DependencyProperty SkippedCountProperty =
            DependencyProperty.Register(nameof(SkippedCount), typeof(int), typeof(OptimizationProgressControl),
                new PropertyMetadata(0));

        public static readonly DependencyProperty FailedCountProperty =
            DependencyProperty.Register(nameof(FailedCount), typeof(int), typeof(OptimizationProgressControl),
                new PropertyMetadata(0));

        public double OptimizationProgress
        {
            get => (double)GetValue(OptimizationProgressProperty);
            set => SetValue(OptimizationProgressProperty, value);
        }

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        public string CurrentAction
        {
            get => (string)GetValue(CurrentActionProperty);
            set => SetValue(CurrentActionProperty, value);
        }

        public int CurrentStep
        {
            get => (int)GetValue(CurrentStepProperty);
            set => SetValue(CurrentStepProperty, value);
        }

        public int TotalSteps
        {
            get => (int)GetValue(TotalStepsProperty);
            set => SetValue(TotalStepsProperty, value);
        }

        public int SuccessCount
        {
            get => (int)GetValue(SuccessCountProperty);
            set => SetValue(SuccessCountProperty, value);
        }

        public int SkippedCount
        {
            get => (int)GetValue(SkippedCountProperty);
            set => SetValue(SkippedCountProperty, value);
        }

        public int FailedCount
        {
            get => (int)GetValue(FailedCountProperty);
            set => SetValue(FailedCountProperty, value);
        }

        public OptimizationProgressControl()
        {
            InitializeComponent();
            SizeChanged += (s, e) => UpdateProgressBarWidth(OptimizationProgress, false);
            Loaded += (s, e) => UpdateProgressBarWidth(OptimizationProgress, false);
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OptimizationProgressControl ctrl)
            {
                ctrl.UpdateProgressBarWidth((double)e.NewValue, true);
            }
        }

        private static void OnStepsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Do not mutate OptimizationProgress here to protect data-binding integrity.
        }

        private void UpdateProgressBarWidth(double progress, bool animate)
        {
            if (TrackBorder == null || FillBorder == null) return;

            double trackWidth = TrackBorder.ActualWidth;
            if (trackWidth <= 0) return;

            double clampedProgress = Math.Min(100.0, Math.Max(0.0, progress));
            double targetWidth = (clampedProgress / 100.0) * trackWidth;

            if (animate)
            {
                var anim = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                FillBorder.BeginAnimation(WidthProperty, anim);
            }
            else
            {
                FillBorder.BeginAnimation(WidthProperty, null);
                FillBorder.Width = targetWidth;
            }
        }
    }
}

