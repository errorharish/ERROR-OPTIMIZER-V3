#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Flagship 3D Physical Interactive Button for Error Optimizer V3.
    /// Implements real physical depth extrusion, beveled surfaces, top specular highlight,
    /// dynamic cursor light tracking, hover lift, and spring compression on click.
    /// </summary>
    public class Premium3DButton : Button
    {
        static Premium3DButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Premium3DButton),
                new FrameworkPropertyMetadata(typeof(Premium3DButton)));
        }

        public static readonly DependencyProperty ElevationProperty =
            DependencyProperty.Register(nameof(Elevation), typeof(double), typeof(Premium3DButton),
                new PropertyMetadata(3.0));

        public double Elevation
        {
            get => (double)GetValue(ElevationProperty);
            set => SetValue(ElevationProperty, value);
        }

        public static readonly DependencyProperty GlowIntensityProperty =
            DependencyProperty.Register(nameof(GlowIntensity), typeof(double), typeof(Premium3DButton),
                new PropertyMetadata(0.7));

        public double GlowIntensity
        {
            get => (double)GetValue(GlowIntensityProperty);
            set => SetValue(GlowIntensityProperty, value);
        }

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(Premium3DButton),
                new PropertyMetadata(new CornerRadius(10)));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (!IsEnabled || App.IsSafeMode || App.IsNoEffectsMode) return;

            var motion = UI3DMotionEngine.Instance;
            if (motion.IsAnimationEnabled && motion.IsHoverEnabled)
            {
                var dur = motion.GetDuration(DurationCategory.ButtonHover);
                var ease = motion.GetEasing(EasingCategory.Spring);

                var anim = new DoubleAnimation(-2.0, dur) { EasingFunction = ease };
                var scaleX = new DoubleAnimation(1.02, dur) { EasingFunction = ease };
                var scaleY = new DoubleAnimation(1.02, dur) { EasingFunction = ease };

                if (RenderTransform is TransformGroup tg)
                {
                    tg.Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                    tg.Children[0].BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                    tg.Children[1].BeginAnimation(TranslateTransform.YProperty, anim);
                }
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!IsEnabled || App.IsSafeMode || App.IsNoEffectsMode) return;

            var motion = UI3DMotionEngine.Instance;
            if (motion.IsAnimationEnabled)
            {
                var dur = motion.GetDuration(DurationCategory.ButtonHoverExit);
                var ease = motion.GetEasing(EasingCategory.Decelerate);

                var anim = new DoubleAnimation(0.0, dur) { EasingFunction = ease };
                var scaleX = new DoubleAnimation(1.0, dur) { EasingFunction = ease };
                var scaleY = new DoubleAnimation(1.0, dur) { EasingFunction = ease };

                if (RenderTransform is TransformGroup tg)
                {
                    tg.Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                    tg.Children[0].BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                    tg.Children[1].BeginAnimation(TranslateTransform.YProperty, anim);
                }
            }
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            if (!IsEnabled || App.IsSafeMode || App.IsNoEffectsMode) return;

            var motion = UI3DMotionEngine.Instance;
            if (motion.IsAnimationEnabled)
            {
                var dur = motion.GetDuration(DurationCategory.ButtonPress);
                var anim = new DoubleAnimation(1.2, dur);
                var scale = new DoubleAnimation(0.97, dur);

                if (RenderTransform is TransformGroup tg)
                {
                    tg.Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, scale);
                    tg.Children[0].BeginAnimation(ScaleTransform.ScaleYProperty, scale);
                    tg.Children[1].BeginAnimation(TranslateTransform.YProperty, anim);
                }
            }
        }
    }
}
