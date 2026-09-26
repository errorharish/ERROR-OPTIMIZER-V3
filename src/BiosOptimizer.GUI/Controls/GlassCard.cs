#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Flagship GlassCard container control for Error Optimizer V3.
    /// Provides depth levels, smooth micro-interactions, subtle accent border lighting,
    /// soft drop shadows, and optional hover scaling.
    /// Fully respects UIMotionEngine settings.
    /// </summary>
    public class GlassCard : ContentControl
    {
        // ── Dependency Properties ─────────────────────────────────────────

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(GlassCard),
                new PropertyMetadata(new CornerRadius(12)));

        public static readonly DependencyProperty DepthProperty =
            DependencyProperty.Register(nameof(Depth), typeof(int), typeof(GlassCard),
                new PropertyMetadata(1, OnDepthChanged));

        public static readonly DependencyProperty IsInteractiveProperty =
            DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(GlassCard),
                new PropertyMetadata(false));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public int Depth
        {
            get => (int)GetValue(DepthProperty);
            set => SetValue(DepthProperty, value);
        }

        public bool IsInteractive
        {
            get => (bool)GetValue(IsInteractiveProperty);
            set => SetValue(IsInteractiveProperty, value);
        }

        static GlassCard()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(GlassCard), new FrameworkPropertyMetadata(typeof(GlassCard)));
        }

        private static void OnDepthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GlassCard card)
                card.UpdateDepthEffect();
        }

        private void UpdateDepthEffect()
        {
            if (Effect is DropShadowEffect shadow)
            {
                switch (Depth)
                {
                    case 0:
                        shadow.Opacity = 0.0;
                        shadow.BlurRadius = 0;
                        break;
                    case 1:
                        shadow.Opacity = 0.25;
                        shadow.BlurRadius = 12;
                        shadow.ShadowDepth = 2;
                        break;
                    case 2:
                        shadow.Opacity = 0.40;
                        shadow.BlurRadius = 20;
                        shadow.ShadowDepth = 4;
                        break;
                    case 3:
                        shadow.Opacity = 0.55;
                        shadow.BlurRadius = 28;
                        shadow.ShadowDepth = 6;
                        break;
                }
            }
        }
    }
}
