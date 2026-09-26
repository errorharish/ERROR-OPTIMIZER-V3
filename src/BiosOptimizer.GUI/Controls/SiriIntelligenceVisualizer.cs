#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Master Siri-Style Living Optimization Score Orb for Error Optimizer V3.
    /// In Solid Theme: Center focal point and scene follow the active solid accent hue.
    /// In Gradient Theme: Center focal point and inner halo strictly use a SINGLE, stable, high-luminance focal color,
    /// while the outer atmospheric environment (aura, ripples, orbital filaments) gracefully reflects the gradient palette.
    /// </summary>
    public class SiriIntelligenceVisualizer : FrameworkElement
    {
        #region Dependency Properties

        public static readonly DependencyProperty ScoreProperty =
            DependencyProperty.Register(nameof(Score), typeof(double), typeof(SiriIntelligenceVisualizer),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnScoreChanged));

        public static readonly DependencyProperty MaxScoreProperty =
            DependencyProperty.Register(nameof(MaxScore), typeof(double), typeof(SiriIntelligenceVisualizer),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ScoreLabelProperty =
            DependencyProperty.Register(nameof(ScoreLabel), typeof(string), typeof(SiriIntelligenceVisualizer),
                new FrameworkPropertyMetadata("Analyzing...", FrameworkPropertyMetadataOptions.AffectsRender, OnScoreLabelChanged));

        public static readonly DependencyProperty IsLowResourceModeProperty =
            DependencyProperty.Register(nameof(IsLowResourceMode), typeof(bool), typeof(SiriIntelligenceVisualizer),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Score
        {
            get => (double)GetValue(ScoreProperty);
            set => SetValue(ScoreProperty, value);
        }

        public double MaxScore
        {
            get => (double)GetValue(MaxScoreProperty);
            set => SetValue(MaxScoreProperty, value);
        }

        public string ScoreLabel
        {
            get => (string)GetValue(ScoreLabelProperty);
            set => SetValue(ScoreLabelProperty, value);
        }

        public bool IsLowResourceMode
        {
            get => (bool)GetValue(IsLowResourceModeProperty);
            set => SetValue(IsLowResourceModeProperty, value);
        }

        #endregion

        #region Particle Structure

        private struct OrbParticle
        {
            public double BaseAngle;
            public double Speed;
            public double RadiusFactor;
            public double PulseFreq;
            public double Size;
            public double Phase;
        }

        #endregion

        #region Private Fields

        private readonly OrbParticle[] _particles;
        private bool _isHooked = false;
        private double _timeSeconds = 0;
        private double _displayedScore = 0;
        private double _lastTargetScore = 0;
        private double _rippleSurge = 0.0;
        private DateTime _lastRenderTime = DateTime.UtcNow;
        private Color _currentAccentColor = Color.FromRgb(0x00, 0xE5, 0xFF);

        // Interactive mouse tracking
        private Point _mousePos = new(70, 70);
        private bool _isMouseOver = false;
        private double _mouseProximity = 0.0;

        // Cached typography
        private static readonly Typeface TypefaceBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface TypefaceSemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface TypefaceRegular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        #endregion

        public SiriIntelligenceVisualizer()
        {
            Width = 140;
            Height = 140;
            ClipToBounds = false;

            _particles = new OrbParticle[18];
            var rng = new Random(42);
            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i] = new OrbParticle
                {
                    BaseAngle = (Math.PI * 2 * i) / _particles.Length,
                    Speed = 0.35 + rng.NextDouble() * 0.65,
                    RadiusFactor = 0.70 + rng.NextDouble() * 0.38,
                    PulseFreq = 1.4 + rng.NextDouble() * 2.2,
                    Size = 1.4 + rng.NextDouble() * 1.8,
                    Phase = rng.NextDouble() * Math.PI * 2
                };
            }

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;

            MouseMove += OnMouseMove;
            MouseEnter += (s, e) => { _isMouseOver = true; };
            MouseLeave += (s, e) => { _isMouseOver = false; };

            ThemeBrushService.Instance.AccentColorChanged += OnAccentColorChanged;
            ThemeBrushService.Instance.ThemeChanged += OnThemeChanged;
            AppSettingsService.Instance.SettingsChanged += OnSettingsChanged;

            UpdateAccentColor();
        }

        #region Lifecycle & Event Hooks

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateAccentColor();
            StartAnimation();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                UpdateAccentColor();
                StartAnimation();
            }
            else
            {
                StopAnimation();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _mousePos = e.GetPosition(this);
        }

        private void OnAccentColorChanged(Color newColor)
        {
            _currentAccentColor = newColor;
            InvalidateVisual();
        }

        private void OnThemeChanged()
        {
            UpdateAccentColor();
            InvalidateVisual();
        }

        private void OnSettingsChanged()
        {
            UpdateAccentColor();
            if (!AppSettingsService.Instance.AnimationsEnabled)
            {
                StopAnimation();
                InvalidateVisual();
            }
            else
            {
                StartAnimation();
            }
        }

        private void UpdateAccentColor()
        {
            _currentAccentColor = ThemeBrushService.Instance.CurrentAccentColor;
        }

        private void StartAnimation()
        {
            if (!AppSettingsService.Instance.AnimationsEnabled)
            {
                InvalidateVisual();
                return;
            }

            if (!_isHooked)
            {
                _lastRenderTime = DateTime.UtcNow;
                CompositionTarget.Rendering += OnRendering;
                _isHooked = true;
            }
        }

        private void StopAnimation()
        {
            if (_isHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isHooked = false;
            }
        }

        private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SiriIntelligenceVisualizer visualizer)
            {
                visualizer.InvalidateVisual();
            }
        }

        private static void OnScoreLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SiriIntelligenceVisualizer visualizer)
            {
                visualizer.InvalidateVisual();
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!IsVisible) return;

            var now = DateTime.UtcNow;
            double dt = (now - _lastRenderTime).TotalSeconds;

            double targetInterval = (IsLowResourceMode || !AppSettingsService.Instance.AnimationsEnabled) ? 0.032 : 0.015;
            if (dt < targetInterval) return;
            _lastRenderTime = now;

            bool reducedMotion = AppSettingsService.Instance.ReducedMotion;
            double motionSpeed = reducedMotion ? 0.35 : 1.0;
            _timeSeconds += dt * motionSpeed;

            double targetScore = Score > 0 ? Score : 0;
            if (Math.Abs(_lastTargetScore - targetScore) > 0.1)
            {
                _lastTargetScore = targetScore;
                _rippleSurge = 1.0;
            }

            if (_rippleSurge > 0.001)
            {
                _rippleSurge -= dt * 0.75;
                if (_rippleSurge < 0) _rippleSurge = 0;
            }

            if (Math.Abs(_displayedScore - targetScore) > 0.04)
            {
                double blendSpeed = reducedMotion ? 2.5 : 4.5;
                _displayedScore += (targetScore - _displayedScore) * Math.Min(1.0, dt * blendSpeed);
            }
            else
            {
                _displayedScore = targetScore;
            }

            double targetProx = _isMouseOver ? 1.0 : 0.0;
            _mouseProximity += (targetProx - _mouseProximity) * Math.Min(1.0, dt * 6.0);

            InvalidateVisual();
        }

        #endregion

        #region Render Pipeline

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            if (w <= 0 || h <= 0) return;

            double cx = w / 2.0;
            double cy = h / 2.0;
            double baseRadius = Math.Min(cx, cy) * 0.86;
            double t = _timeSeconds;

            bool animations = AppSettingsService.Instance.AnimationsEnabled;
            bool reducedMotion = AppSettingsService.Instance.ReducedMotion;
            bool lowEnd = IsLowResourceMode;

            // =========================================================================
            // 1. SINGLE SOLID FOCAL COLOR (MANDATORY FOR CENTER POINT & HALO)
            // =========================================================================
            Color focalColor = ThemeBrushService.Instance.GetSiriFocalColor();
            Color focalBright = ThemeBrushService.AdjustBrightness(focalColor, 1.35);
            Color focalLight = ThemeBrushService.AdjustBrightness(focalColor, 1.18);
            Color focalDark = ThemeBrushService.AdjustBrightness(focalColor, 0.70);

            // =========================================================================
            // 2. ATMOSPHERIC GRADIENT ENVIRONMENT COLORS (OUTER AMBIENT & SURROUNDINGS)
            // =========================================================================
            Color envStart, envMid, envEnd;
            if (ThemeBrushService.Instance.CurrentThemeMode == ThemeMode.Gradient)
            {
                envStart = ThemeBrushService.Instance.GetColorAtOffset(0.0);
                envMid = ThemeBrushService.Instance.GetColorAtOffset(0.5);
                envEnd = ThemeBrushService.Instance.GetColorAtOffset(1.0);
            }
            else
            {
                envStart = focalColor;
                envMid = focalLight;
                envEnd = focalBright;
            }

            bool isAnalyzing = Score <= 0 || ScoreLabel.Contains("Analyzing", StringComparison.OrdinalIgnoreCase) || ScoreLabel.Contains("Calculating", StringComparison.OrdinalIgnoreCase);
            double stateAgitation = isAnalyzing ? 1.25 : (_displayedScore < 50 ? 1.55 : (_displayedScore < 75 ? 1.20 : 0.95));
            double totalVitality = stateAgitation * (1.0 + _rippleSurge * 0.45);

            // ============================================================
            // LAYER 1: AMBIENT PULSING AURA (SOFT BACKGROUND GLOW)
            // ============================================================
            double auraBreath = animations ? Math.Sin(t * 1.2) * 0.06 : 0.0;
            double auraR = baseRadius * (1.08 + auraBreath + _mouseProximity * 0.06);
            Point auraOrigin = animations
                ? new Point(cx + Math.Sin(t * 0.8) * 3.0 + (_mousePos.X - cx) * 0.05, cy + Math.Cos(t * 0.6) * 3.0 + (_mousePos.Y - cy) * 0.05)
                : new Point(cx, cy);

            var auraBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(auraOrigin.X / w, auraOrigin.Y / h),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.54,
                RadiusY = 0.54
            };
            byte auraAlpha1 = (byte)(0x45 + _mouseProximity * 0x15);
            byte auraAlpha2 = (byte)(0x22 + _mouseProximity * 0x0A);
            auraBrush.GradientStops.Add(new GradientStop(Color.FromArgb(auraAlpha1, envEnd.R, envEnd.G, envEnd.B), 0.0));
            auraBrush.GradientStops.Add(new GradientStop(Color.FromArgb(auraAlpha2, envStart.R, envStart.G, envStart.B), 0.55));
            auraBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
            auraBrush.Freeze();

            dc.DrawEllipse(auraBrush, null, new Point(cx, cy), auraR, auraR);

            // ============================================================
            // LAYER 2: ORGANIC SDF HARMONIC WAVE DEFORMATION (OUTER PERIMETER)
            // ============================================================
            if (animations && !reducedMotion)
            {
                DrawSdfHarmonicWaveContour(dc, cx, cy, baseRadius * 0.88, t * totalVitality, envStart, envMid, envEnd);
            }

            // ============================================================
            // LAYER 3: FLUID MULTI-PHASE OUTER ENERGY BLOBS
            // ============================================================
            if (!lowEnd)
            {
                // Blob 1
                double b1Angle = t * 1.1 * totalVitality;
                double b1Dist = baseRadius * 0.16;
                Point b1Center = new(cx + Math.Cos(b1Angle) * b1Dist, cy + Math.Sin(b1Angle) * b1Dist);
                double b1Radius = baseRadius * 0.48 * (1.0 + Math.Sin(t * 1.5) * 0.10);
                DrawOrganicBlob(dc, b1Center, b1Radius, Color.FromArgb(0x45, envEnd.R, envEnd.G, envEnd.B), Color.FromArgb(0x10, envStart.R, envStart.G, envStart.B));

                // Blob 2 (Counter-Rotating)
                double b2Angle = -t * 0.80 * totalVitality + 1.9;
                double b2Dist = baseRadius * 0.20;
                Point b2Center = new(cx + Math.Cos(b2Angle) * b2Dist, cy + Math.Sin(b2Angle) * b2Dist);
                double b2Radius = baseRadius * 0.42 * (1.0 + Math.Cos(t * 1.2) * 0.12);
                DrawOrganicBlob(dc, b2Center, b2Radius, Color.FromArgb(0x40, envStart.R, envStart.G, envStart.B), Color.FromArgb(0x0C, envEnd.R, envEnd.G, envEnd.B));
            }

            // ============================================================
            // LAYER 4: SINGLE-COLOR FOCAL HALO & SOLID CENTRAL CORE POINT
            // ============================================================
            // 4A. Single-Color Outer Halo (Same Focal Hue)
            double haloBreath = animations ? Math.Sin(t * 1.6) * 0.03 : 0.0;
            double haloRadius = baseRadius * (0.72 + haloBreath);
            var haloBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0x35 + _mouseProximity * 0x15), focalBright.R, focalBright.G, focalBright.B), 0.0));
            haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0x15 + _mouseProximity * 0x0A), focalColor.R, focalColor.G, focalColor.B), 0.60));
            haloBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
            haloBrush.Freeze();
            dc.DrawEllipse(haloBrush, null, new Point(cx, cy), haloRadius, haloRadius);

            // 4B. Central Luminous Core / Point (STRICT SINGLE SOLID COLOR - NEVER MULTI-COLOR BLEND)
            double coreBreath = animations ? Math.Sin(t * 1.8) * 0.04 : 0.0;
            double coreRadius = baseRadius * (0.60 + coreBreath);
            var coreBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.46, 0.43),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            // Specular white center -> Bright solid focal hue -> Stable solid focal hue -> Soft falloff
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x95, 0xFF, 0xFF, 0xFF), 0.0));
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, focalBright.R, focalBright.G, focalBright.B), 0.35));
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x48, focalColor.R, focalColor.G, focalColor.B), 0.68));
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x04, focalDark.R, focalDark.G, focalDark.B), 1.0));
            coreBrush.Freeze();

            dc.DrawEllipse(coreBrush, null, new Point(cx, cy), coreRadius, coreRadius);

            // ============================================================
            // LAYER 5: FLOWING ENERGY ARCS & ORBITAL FILAMENTS
            // ============================================================
            if (animations)
            {
                // Primary Accent Ribbon (Uses controlled gradient / environment palette)
                double arc1Start = (t * 0.85 * totalVitality) % (Math.PI * 2);
                double arc1Sweep = 1.4 + Math.Sin(t * 1.6) * 0.30;
                double arc1R = baseRadius * (0.76 + Math.Sin(t * 1.4) * 0.03);
                DrawEnergyArc(dc, cx, cy, arc1R, arc1Start, arc1Sweep, 2.6, envStart, envEnd);

                // Secondary Counter Ribbon
                double arc2Start = (-t * 1.15 * totalVitality + 2.1) % (Math.PI * 2);
                double arc2Sweep = 1.3 + Math.Cos(t * 1.3) * 0.35;
                double arc2R = baseRadius * (0.84 + Math.Cos(t * 1.7) * 0.03);
                DrawEnergyArc(dc, cx, cy, arc2R, arc2Start, arc2Sweep, 1.8, envMid, focalBright);
            }

            // Layer 5B: Counter-Orbital Dashed Filaments
            double ring1Angle = animations ? (t * 22.0 * totalVitality) % 360.0 : 0.0;
            var ring1Pen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, envStart.R, envStart.G, envStart.B)), 1.1)
            {
                DashStyle = new DashStyle(new double[] { 8, 12 }, ring1Angle / 10.0)
            };
            ring1Pen.Freeze();
            dc.DrawEllipse(null, ring1Pen, new Point(cx, cy), baseRadius * 0.80, baseRadius * 0.80);

            // ============================================================
            // LAYER 6: TRANSIENT SCORE RIPPLE SURGE & MICRO PARTICLES
            // ============================================================
            if (_rippleSurge > 0.01)
            {
                double surgeProgress = 1.0 - _rippleSurge;
                double surgeR = baseRadius * (0.45 + surgeProgress * 0.65);
                byte surgeAlpha = (byte)(Math.Pow(_rippleSurge, 1.5) * 85);
                var surgePen = new Pen(new SolidColorBrush(Color.FromArgb(surgeAlpha, focalBright.R, focalBright.G, focalBright.B)), 1.6);
                surgePen.Freeze();
                dc.DrawEllipse(null, surgePen, new Point(cx, cy), surgeR, surgeR);
            }

            // Micro Particles
            if (animations)
            {
                int pCount = (reducedMotion || lowEnd) ? 6 : _particles.Length;
                for (int i = 0; i < pCount; i++)
                {
                    ref readonly var p = ref _particles[i];
                    double pAngle = (p.BaseAngle + t * p.Speed * 0.70 * totalVitality) % (Math.PI * 2);
                    double pDist = baseRadius * p.RadiusFactor * (1.0 + Math.Sin(t * 1.8 + p.Phase) * 0.06);
                    double px = cx + Math.Cos(pAngle) * pDist;
                    double py = cy + Math.Sin(pAngle) * pDist;

                    double alpha = Math.Clamp(0.35 + 0.65 * Math.Sin(t * p.PulseFreq + p.Phase), 0.0, 1.0);
                    Color pColor = (i % 3 == 0) ? focalBright : (i % 3 == 1 ? envEnd : envStart);
                    var pBrush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 180), pColor.R, pColor.G, pColor.B));
                    pBrush.Freeze();

                    dc.DrawEllipse(pBrush, null, new Point(px, py), p.Size * 0.65, p.Size * 0.65);
                }
            }

            // ============================================================
            // LAYER 7: GLASS SPECULAR SHEEN & CENTER SCORE TYPOGRAPHY
            // ============================================================
            double specOffset = _mouseProximity * 6.0;
            var glassBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0.2, 0.1),
                EndPoint = new Point(0.5, 0.6)
            };
            glassBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0x40 + _mouseProximity * 0x20), 0xFF, 0xFF, 0xFF), 0.0));
            glassBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.7));
            glassBrush.Freeze();
            dc.DrawEllipse(glassBrush, null, new Point(cx - baseRadius * 0.12 + specOffset, cy - baseRadius * 0.18 + specOffset), baseRadius * 0.38, baseRadius * 0.24);

            // Center Typography
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (isAnalyzing)
            {
                double pulseOpacity = animations ? 0.6 + Math.Sin(t * 3.0) * 0.35 : 0.9;
                var analyzingBrush = new SolidColorBrush(Color.FromArgb((byte)(pulseOpacity * 255), 0xFF, 0xFF, 0xFF));
                analyzingBrush.Freeze();

                var ftAnalyzing = new FormattedText("ANALYZING...", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TypefaceBold, 11, analyzingBrush, dpi);
                dc.DrawText(ftAnalyzing, new Point(cx - ftAnalyzing.Width / 2.0, cy - ftAnalyzing.Height / 2.0 - 4));

                var subBrush = new SolidColorBrush(Color.FromArgb(0xB0, focalBright.R, focalBright.G, focalBright.B));
                subBrush.Freeze();
                var ftSub = new FormattedText("INTELLIGENCE ORB", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TypefaceSemiBold, 8.5, subBrush, dpi);
                dc.DrawText(ftSub, new Point(cx - ftSub.Width / 2.0, cy + ftAnalyzing.Height / 2.0));
            }
            else
            {
                string scoreStr = $"{Math.Round(_displayedScore):F0}";
                var scoreNumBrush = Brushes.White;
                var ftScore = new FormattedText(scoreStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TypefaceBold, 26, scoreNumBrush, dpi);

                var ftMax = new FormattedText("/100", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TypefaceSemiBold, 9.5, new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), dpi);

                double combinedWidth = ftScore.Width + ftMax.Width + 2;
                double numX = cx - combinedWidth / 2.0;
                double numY = cy - ftScore.Height / 2.0 - 5;

                dc.DrawText(ftScore, new Point(numX, numY));
                dc.DrawText(ftMax, new Point(numX + ftScore.Width + 2, numY + ftScore.Height - ftMax.Height - 3));

                string statusText = ScoreLabel.ToUpperInvariant();
                var ftStatus = new FormattedText(statusText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TypefaceBold, 8.5, new SolidColorBrush(focalBright), dpi);
                dc.DrawText(ftStatus, new Point(cx - ftStatus.Width / 2.0, numY + ftScore.Height - 2));
            }
        }

        private static void DrawSdfHarmonicWaveContour(DrawingContext dc, double cx, double cy, double radius, double t, Color c1, Color c2, Color c3)
        {
            const int steps = 36;
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                for (int i = 0; i <= steps; i++)
                {
                    double theta = (Math.PI * 2 * i) / steps;
                    
                    double wave1 = Math.Sin(theta * 3.0 + t * 2.0) * 3.2;
                    double wave2 = Math.Cos(theta * 5.0 - t * 1.5) * 2.0;
                    double wave3 = Math.Sin(theta * 2.0 + t * 0.8) * 1.8;
                    double r = radius + wave1 + wave2 + wave3;

                    double px = cx + Math.Cos(theta) * r;
                    double py = cy + Math.Sin(theta) * r;

                    if (i == 0)
                        ctx.BeginFigure(new Point(px, py), false, true);
                    else
                        ctx.LineTo(new Point(px, py), true, false);
                }
            }
            geom.Freeze();

            var wavePen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, c1.R, c1.G, c1.B)), 1.3);
            wavePen.Freeze();
            dc.DrawGeometry(null, wavePen, geom);
        }

        private static void DrawOrganicBlob(DrawingContext dc, Point center, double radius, Color cInner, Color cOuter)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.45, 0.45),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            brush.GradientStops.Add(new GradientStop(cInner, 0.0));
            brush.GradientStops.Add(new GradientStop(cOuter, 0.65));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
            brush.Freeze();

            dc.DrawEllipse(brush, null, center, radius, radius);
        }

        private static void DrawEnergyArc(DrawingContext dc, double cx, double cy, double radius, double startAngle, double sweepAngle, double thickness, Color cStart, Color cEnd)
        {
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                double endAngle = startAngle + sweepAngle;
                Point startPt = new(cx + Math.Cos(startAngle) * radius, cy + Math.Sin(startAngle) * radius);
                Point endPt = new(cx + Math.Cos(endAngle) * radius, cy + Math.Sin(endAngle) * radius);

                ctx.BeginFigure(startPt, false, false);
                ctx.ArcTo(endPt, new Size(radius, radius), 0, sweepAngle > Math.PI, SweepDirection.Clockwise, true, false);
            }
            geom.Freeze();

            var arcBrush = new LinearGradientBrush(cStart, cEnd, new Point(0, 0), new Point(1, 1));
            arcBrush.Freeze();

            var pen = new Pen(arcBrush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();

            dc.DrawGeometry(null, pen, geom);
        }

        #endregion
    }
}
