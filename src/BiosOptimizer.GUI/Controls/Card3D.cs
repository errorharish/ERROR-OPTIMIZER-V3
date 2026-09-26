#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Spatial 3D Card Control for Error Optimizer V3.
    /// Layered Architecture:
    /// - LAYER 1: Background Glass Shell
    /// - LAYER 2: Glow Underlay / Spotlight (Behind Content)
    /// - LAYER 3: Dedicated Foreground Content Layer (100% Razor Sharp Native ClearType)
    /// Guarantees zero text softening or rasterization on cursor hover.
    /// </summary>
    public class Card3D : ContentControl
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(Card3D),
                new PropertyMetadata(new CornerRadius(12)));

        public static readonly DependencyProperty DepthProperty =
            DependencyProperty.Register(nameof(Depth), typeof(int), typeof(Card3D),
                new PropertyMetadata(1));

        public static readonly DependencyProperty IsInteractiveProperty =
            DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(Card3D),
                new PropertyMetadata(true));

        public static readonly DependencyProperty IsSpotlightEnabledProperty =
            DependencyProperty.Register(nameof(IsSpotlightEnabled), typeof(bool), typeof(Card3D),
                new PropertyMetadata(true));

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

        public bool IsSpotlightEnabled
        {
            get => (bool)GetValue(IsSpotlightEnabledProperty);
            set => SetValue(IsSpotlightEnabledProperty, value);
        }

        private Border? _backgroundBorder;
        private Border? _glowUnderlay;
        private Rectangle? _spotlightOverlay;
        private RadialGradientBrush? _spotlightBrush;

        static Card3D()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Card3D), new FrameworkPropertyMetadata(typeof(Card3D)));
        }

        public Card3D()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            MouseMove += OnCardMouseMove;
            MouseEnter += OnCardMouseEnter;
            MouseLeave += OnCardMouseLeave;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _backgroundBorder = GetTemplateChild("PART_BackgroundBorder") as Border;
            _glowUnderlay = GetTemplateChild("PART_GlowUnderlay") as Border;
            _spotlightOverlay = GetTemplateChild("PART_SpotlightOverlay") as Rectangle;

            if (_spotlightOverlay != null)
            {
                var accent = ThemeBrushService.Instance.CurrentAccentColor;
                _spotlightBrush = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 0.75,
                    RadiusY = 0.75
                };
                _spotlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(95, accent.R, accent.G, accent.B), 0.0));
                _spotlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(50, accent.R, accent.G, accent.B), 0.38));
                _spotlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(18, accent.R, accent.G, accent.B), 0.75));
                _spotlightBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                _spotlightOverlay.Fill = _spotlightBrush;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ThemeBrushService.Instance.AccentColorChanged += OnAccentColorChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeBrushService.Instance.AccentColorChanged -= OnAccentColorChanged;
        }

        private void OnAccentColorChanged(Color accent)
        {
            if (_spotlightBrush != null && _spotlightBrush.GradientStops.Count >= 3)
            {
                _spotlightBrush.GradientStops[0].Color = Color.FromArgb(95, accent.R, accent.G, accent.B);
                _spotlightBrush.GradientStops[1].Color = Color.FromArgb(50, accent.R, accent.G, accent.B);
                _spotlightBrush.GradientStops[2].Color = Color.FromArgb(18, accent.R, accent.G, accent.B);
            }
        }

        private void OnCardMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsInteractive || !IsSpotlightEnabled || _spotlightBrush == null) return;

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            var pos = e.GetPosition(this);
            _spotlightBrush.Center = new Point(pos.X / w, pos.Y / h);
            _spotlightBrush.GradientOrigin = new Point(pos.X / w, pos.Y / h);
        }

        private void OnCardMouseEnter(object sender, MouseEventArgs e)
        {
            if (!IsInteractive) return;

            var motion = UI3DMotionEngine.Instance;
            if (!motion.IsHoverEnabled) return;

            var dur = motion.GetDuration(DurationCategory.ButtonHover);
            var ease = motion.GetEasing(EasingCategory.Decelerate);

            if (_glowUnderlay != null)
            {
                var glowAnim = new DoubleAnimation(0.95 * motion.GlowIntensity, new Duration(dur)) { EasingFunction = ease };
                _glowUnderlay.BeginAnimation(UIElement.OpacityProperty, glowAnim);
            }

            if (_backgroundBorder != null)
            {
                var accent = ThemeBrushService.Instance.CurrentAccentColor;
                _backgroundBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B));
                _backgroundBorder.Background = new SolidColorBrush(Color.FromArgb(22, accent.R, accent.G, accent.B));
            }
        }

        private void OnCardMouseLeave(object sender, MouseEventArgs e)
        {
            if (!IsInteractive) return;

            var motion = UI3DMotionEngine.Instance;
            var dur = motion.GetDuration(DurationCategory.ButtonHoverExit);

            if (_glowUnderlay != null)
            {
                var glowAnim = new DoubleAnimation(0.0, new Duration(dur));
                _glowUnderlay.BeginAnimation(UIElement.OpacityProperty, glowAnim);
            }

            if (_backgroundBorder != null)
            {
                _backgroundBorder.BorderBrush = (Brush)FindResource("AccentBorderSubtleBrush");
                _backgroundBorder.Background = (Brush)FindResource("BgCardBrush");
            }
        }
    }
}
