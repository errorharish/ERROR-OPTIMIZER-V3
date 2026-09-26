#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Emissive Glowing TextBlock for Brand, Hero Metrics, and Page Titles.
    /// </summary>
    public class GlowText : TextBlock
    {
        public static readonly DependencyProperty GlowRadiusProperty =
            DependencyProperty.Register(nameof(GlowRadius), typeof(double), typeof(GlowText),
                new PropertyMetadata(12.0, OnGlowChanged));

        public double GlowRadius
        {
            get => (double)GetValue(GlowRadiusProperty);
            set => SetValue(GlowRadiusProperty, value);
        }

        public GlowText()
        {
            FontWeight = FontWeights.Bold;
            UpdateEffect();
        }

        private static void OnGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GlowText gt) gt.UpdateEffect();
        }

        private void UpdateEffect()
        {
            Effect = new DropShadowEffect
            {
                Color = Colors.Red,
                BlurRadius = GlowRadius,
                ShadowDepth = 0,
                Opacity = 0.6
            };
        }
    }
}
