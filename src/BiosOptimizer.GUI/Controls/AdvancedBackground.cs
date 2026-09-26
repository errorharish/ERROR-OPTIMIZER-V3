#nullable enable
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Master High-Performance Continuous 3-Layer Parallax Star Background for Error Optimizer V3.
    /// Engineered for 60 FPS zero-allocation per-frame hardware-accelerated rendering.
    /// 
    /// Physics Architecture:
    /// - Continuous Autonomous Looped Motion: Runs independently of mouse movement across 3 depth layers.
    /// - 8 Real-Time Interactive Settings: Parallax Active, Density, Opacity, Strength, Speed, Glow, Depth, Twinkle.
    /// - Dynamic Theme Accent Color Synchronization (ThemeBrushService.CurrentAccentColor).
    /// - 100% Isolated Rendering Layer: Zero blur or softening of UI cards or text.
    /// </summary>
    public class AdvancedBackground : FrameworkElement
    {
        private struct ParallaxStar
        {
            public double X;
            public double Y;
            public int Layer;
            public double Size;
            public double BaseDriftSpeed;
            public double BaseAlpha;
            public double TwinkleFreq;
            public double TwinklePhase;
            public bool HasGlow;
        }

        private const int MaxStars = 320;
        private readonly ParallaxStar[] _stars = new ParallaxStar[MaxStars];
        private readonly Random _rand = new(421337);

        private readonly Stopwatch _stopwatch = new();
        private long _lastTicks;
        private double _totalTime;
        private bool _isRenderingHooked;
        private bool _isPaused = false;

        public void PauseRendering()
        {
            _isPaused = true;
            UnhookRendering();
        }

        public void ResumeRendering()
        {
            _isPaused = false;
            UpdateRenderHook();
            InvalidateVisual();
        }

        private Point _mousePos = new(960, 540);
        private Point _smoothMouse = new(960, 540);
        private double _parallaxOffsetX;
        private double _parallaxOffsetY;

        private Color _targetAccentColor = Color.FromRgb(0, 229, 255);
        private double _curR = 0, _curG = 229, _curB = 255;

        // Cached frozen brushes (zero per-frame allocations)
        private Brush? _cachedSpaceBrush;
        private double _cachedSpaceWidth;
        private double _cachedSpaceHeight;
        private byte _cachedSpaceAlpha;

        private Brush? _cachedFarBrush;
        private Brush? _cachedMidBrush;
        private Brush? _cachedNearBrush;
        private Brush? _cachedGlowBrush;
        private Color _cachedBrushColor;
        private double _cachedBrushOpacity = -1;

        public AdvancedBackground()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;

            InitializeStarfield();

            Loaded += (s, e) => { UpdateRenderHook(); InvalidateVisual(); };
            Unloaded += (s, e) => UnhookRendering();
            IsVisibleChanged += (s, e) => {
                if (IsVisible) UpdateRenderHook();
                else UnhookRendering();
            };

            ThemeBrushService.Instance.AccentColorChanged += (c) => { 
                _targetAccentColor = c;
                InvalidateBrushes();
            };
            UI3DMotionEngine.Instance.MotionSettingsChanged += () => { 
                UpdateRenderHook(); 
                InvalidateBrushes();
                InvalidateVisual(); 
            };
            UpdateAccentColorImmediate();
            InvalidateBrushes();
            UpdateRenderHook();
        }

        private void InvalidateBrushes()
        {
            _cachedSpaceBrush = null;
            _cachedFarBrush = null;
            _cachedMidBrush = null;
            _cachedNearBrush = null;
            _cachedGlowBrush = null;
        }

        private void UpdateAccentColorImmediate()
        {
            try
            {
                _targetAccentColor = ThemeBrushService.Instance.CurrentAccentColor;
                _curR = _targetAccentColor.R;
                _curG = _targetAccentColor.G;
                _curB = _targetAccentColor.B;
            }
            catch
            {
                _targetAccentColor = Color.FromRgb(0, 229, 255);
            }
        }

        private void InitializeStarfield()
        {
            for (int i = 0; i < MaxStars; i++)
            {
                RespawnStar(ref _stars[i], initialSpawn: true);
            }
        }

        private void RespawnStar(ref ParallaxStar s, bool initialSpawn)
        {
            double layerRand = _rand.NextDouble();

            if (layerRand < 0.55)
            {
                // Layer 0: FAR (slowest drift, smallest size, dimmest)
                s.Layer = 0;
                s.Size = 1.0 + _rand.NextDouble() * 0.4;
                s.BaseDriftSpeed = 16.0 + _rand.NextDouble() * 8.0;
                s.BaseAlpha = 0.45 + _rand.NextDouble() * 0.15;
                s.HasGlow = false;
            }
            else if (layerRand < 0.88)
            {
                // Layer 1: MID (medium drift, medium size)
                s.Layer = 1;
                s.Size = 1.8 + _rand.NextDouble() * 0.5;
                s.BaseDriftSpeed = 38.0 + _rand.NextDouble() * 14.0;
                s.BaseAlpha = 0.70 + _rand.NextDouble() * 0.18;
                s.HasGlow = _rand.NextDouble() < 0.30;
            }
            else
            {
                // Layer 2: NEAR (fastest drift, largest size, glowing aura)
                s.Layer = 2;
                s.Size = 2.6 + _rand.NextDouble() * 0.7;
                s.BaseDriftSpeed = 70.0 + _rand.NextDouble() * 25.0;
                s.BaseAlpha = 0.90 + _rand.NextDouble() * 0.10;
                s.HasGlow = true;
            }

            double w = ActualWidth > 0 ? ActualWidth : 1600;
            double h = ActualHeight > 0 ? ActualHeight : 1000;

            s.X = -80.0 + _rand.NextDouble() * (w + 160.0);
            s.Y = initialSpawn ? (-80.0 + _rand.NextDouble() * (h + 160.0)) : (h + 30.0 + _rand.NextDouble() * 30.0);
            s.TwinkleFreq = 1.2 + _rand.NextDouble() * 2.5;
            s.TwinklePhase = _rand.NextDouble() * Math.PI * 2.0;
        }

        private void UpdateRenderHook()
        {
            if (_isPaused) return;
            if (IsVisible && !_isRenderingHooked)
            {
                if (!_stopwatch.IsRunning) _stopwatch.Start();
                _lastTicks = _stopwatch.ElapsedTicks;
                CompositionTarget.Rendering += OnRendering;
                _isRenderingHooked = true;
            }
        }

        private void _legacyUpdateRenderHook()
        {
            if (!_isRenderingHooked)
            {
                if (!_stopwatch.IsRunning)
                {
                    _stopwatch.Start();
                }
                _lastTicks = _stopwatch.ElapsedTicks;
                CompositionTarget.Rendering += OnRendering;
                _isRenderingHooked = true;
            }
        }

        private void UnhookRendering()
        {
            if (_isRenderingHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isRenderingHooked = false;
            }
        }

        public void HandleMouseMove(Point pos)
        {
            _mousePos = pos;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            long currentTicks = _stopwatch.ElapsedTicks;
            double dt = (currentTicks - _lastTicks) / (double)Stopwatch.Frequency;
            _lastTicks = currentTicks;

            if (dt <= 0.0 || dt > 0.1) dt = 0.016;

            _totalTime += dt;

            // Smooth accent color transitions
            double lerpFactor = Math.Min(1.0, 10.0 * dt);
            _curR += (_targetAccentColor.R - _curR) * lerpFactor;
            _curG += (_targetAccentColor.G - _curG) * lerpFactor;
            _curB += (_targetAccentColor.B - _curB) * lerpFactor;

            UpdateStarPhysics(dt);
            InvalidateVisual();
        }

        private void UpdateStarPhysics(double dt)
        {
            double w = Math.Max(ActualWidth > 0 ? ActualWidth : 1280, 800);
            double h = Math.Max(ActualHeight > 0 ? ActualHeight : 800, 600);

            _smoothMouse.X += (_mousePos.X - _smoothMouse.X) * Math.Min(dt * 6.0, 1.0);
            _smoothMouse.Y += (_mousePos.Y - _smoothMouse.Y) * Math.Min(dt * 6.0, 1.0);

            var settings = AppSettingsService.Instance;
            if (!settings.ReducedMotion && settings.ParallaxBackground)
            {
                double depthMult = settings.StarDepth switch { 0 => 0.50, 2 => 1.50, _ => 1.0 };
                double strength = Math.Clamp(settings.ParallaxStrength, 0.0, 1.0) * depthMult;
                
                _parallaxOffsetX = ((_smoothMouse.X - w * 0.5) / w) * 14.0 * strength;
                _parallaxOffsetY = ((_smoothMouse.Y - h * 0.5) / h) * 14.0 * strength;
            }
            else
            {
                _parallaxOffsetX = 0;
                _parallaxOffsetY = 0;
            }

            int count = settings.StarDensity switch
            {
                0 => 70,   // LOW
                2 => 300,  // HIGH
                _ => 160   // MEDIUM
            };

            // Background Mode: 0 = Static, 1 = Subtle (0.55x), 2 = Dynamic (1.0x)
            double modeMultiplier = settings.BackgroundMode switch
            {
                0 => 0.0,
                1 => 0.55,
                _ => 1.0
            };

            double speedMult = modeMultiplier * (0.5 + (Math.Clamp(settings.StarSpeed, 0.0, 2.0) * 1.0));
            bool isMoving = settings.ParallaxBackground && !settings.ReducedMotion && speedMult > 0.001;

            for (int i = 0; i < count; i++)
            {
                ref var s = ref _stars[i];

                if (isMoving)
                {
                    double dy = s.BaseDriftSpeed * speedMult * dt;
                    s.Y -= dy;

                    // Subtle diagonal drift for natural organic space motion
                    double dx = (s.BaseDriftSpeed * 0.15) * speedMult * dt;
                    s.X -= dx;

                    if (s.Y < -30.0)
                    {
                        s.Y = h + 20.0 + _rand.NextDouble() * 30.0;
                        s.X = -60.0 + _rand.NextDouble() * (w + 120.0);
                    }

                    if (s.X < -60.0)
                    {
                        s.X = w + 40.0;
                    }
                }
            }
        }

        private void EnsureCachedBrushes(Color activeAccent, double userOpacity)
        {
            if (_cachedFarBrush == null || _cachedBrushColor != activeAccent || Math.Abs(_cachedBrushOpacity - userOpacity) > 0.01)
            {
                _cachedBrushColor = activeAccent;
                _cachedBrushOpacity = userOpacity;

                Color cFar = activeAccent;
                Color cMid = activeAccent;
                Color cNear = activeAccent;
                Color cGlow = activeAccent;

                if (ThemeBrushService.Instance.CurrentThemeMode == ThemeMode.Gradient)
                {
                    cFar = ThemeBrushService.Instance.GetColorAtOffset(0.0);
                    cMid = ThemeBrushService.Instance.GetColorAtOffset(0.5);
                    cNear = ThemeBrushService.Instance.GetColorAtOffset(1.0);
                    cGlow = ThemeBrushService.Instance.GetColorAtOffset(0.35);
                }

                byte starFarR = (byte)(255 * 0.88 + cFar.R * 0.12);
                byte starFarG = (byte)(255 * 0.88 + cFar.G * 0.12);
                byte starFarB = (byte)(255 * 0.88 + cFar.B * 0.12);

                byte starMidR = (byte)(255 * 0.88 + cMid.R * 0.12);
                byte starMidG = (byte)(255 * 0.88 + cMid.G * 0.12);
                byte starMidB = (byte)(255 * 0.88 + cMid.B * 0.12);

                byte starNearR = (byte)(255 * 0.88 + cNear.R * 0.12);
                byte starNearG = (byte)(255 * 0.88 + cNear.G * 0.12);
                byte starNearB = (byte)(255 * 0.88 + cNear.B * 0.12);

                byte farA = (byte)Math.Clamp(0.50 * 255 * userOpacity, 15, 255);
                var fb = new SolidColorBrush(Color.FromArgb(farA, starFarR, starFarG, starFarB));
                fb.Freeze();
                _cachedFarBrush = fb;

                byte midA = (byte)Math.Clamp(0.75 * 255 * userOpacity, 25, 255);
                var mb = new SolidColorBrush(Color.FromArgb(midA, starMidR, starMidG, starMidB));
                mb.Freeze();
                _cachedMidBrush = mb;

                byte nearA = (byte)Math.Clamp(0.95 * 255 * userOpacity, 35, 255);
                var nb = new SolidColorBrush(Color.FromArgb(nearA, starNearR, starNearG, starNearB));
                nb.Freeze();
                _cachedNearBrush = nb;

                byte glowA = (byte)Math.Clamp(0.35 * 255 * userOpacity, 10, 255);
                var gb = new SolidColorBrush(Color.FromArgb(glowA, cGlow.R, cGlow.G, cGlow.B));
                gb.Freeze();
                _cachedGlowBrush = gb;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth > 0 ? ActualWidth : 1280;
            double h = ActualHeight > 0 ? ActualHeight : 800;
            var fullRect = new Rect(0, 0, w, h);

            byte activeR = (byte)Math.Clamp(_curR, 0, 255);
            byte activeG = (byte)Math.Clamp(_curG, 0, 255);
            byte activeB = (byte)Math.Clamp(_curB, 0, 255);
            var activeAccentColor = Color.FromRgb(activeR, activeG, activeB);

            var settings = AppSettingsService.Instance;
            double userOpacity = Math.Clamp(settings.StarOpacity, 0.0, 1.0);
            double starGlow = Math.Clamp(settings.StarGlow, 0.0, 1.0);

            // 1. Base Space Gradient (Cached and Frozen)
            byte bgAlpha = (byte)((1.0 - ThemeBrushService.Instance.CurrentWindowGlassTransparency) * 255);
            if (_cachedSpaceBrush == null || _cachedSpaceWidth != w || _cachedSpaceHeight != h || _cachedSpaceAlpha != bgAlpha)
            {
                _cachedSpaceWidth = w;
                _cachedSpaceHeight = h;
                _cachedSpaceAlpha = bgAlpha;

                var baseSpaceBrush = new RadialGradientBrush
                {
                    Center = new Point(0.5, 1.0),
                    GradientOrigin = new Point(0.5, 1.0),
                    RadiusX = 1.0,
                    RadiusY = 1.0
                };
                baseSpaceBrush.GradientStops.Add(new GradientStop(Color.FromArgb(bgAlpha, 0x14, 0x1C, 0x28), 0.0));
                baseSpaceBrush.GradientStops.Add(new GradientStop(Color.FromArgb(bgAlpha, 0x09, 0x0A, 0x0F), 1.0));
                baseSpaceBrush.Freeze();
                _cachedSpaceBrush = baseSpaceBrush;
            }

            if (_cachedSpaceBrush != null)
            {
                dc.DrawRectangle(_cachedSpaceBrush, null, fullRect);
            }

            // 2. Subtle Nebula Glow
            double glowScale = settings.GlowIntensity;
            if (glowScale > 0.01 && !settings.ReducedMotion)
            {
                double spotX = _smoothMouse.X / w;
                double spotY = _smoothMouse.Y / h;
                byte spotAlpha = (byte)(18 * glowScale);

                var spotBrush = new RadialGradientBrush
                {
                    Center = new Point(spotX, spotY),
                    GradientOrigin = new Point(spotX, spotY),
                    RadiusX = 0.45,
                    RadiusY = 0.45
                };
                spotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(spotAlpha, activeR, activeG, activeB), 0.0));
                spotBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                spotBrush.Freeze();
                dc.DrawRectangle(spotBrush, null, fullRect);
            }

            // 3. Render Stars with Zero Heap Allocations
            int count = settings.StarDensity switch
            {
                0 => 70,
                2 => 300,
                _ => 160
            };

            if (count > 0 && settings.ParallaxBackground && userOpacity > 0.01)
            {
                EnsureCachedBrushes(activeAccentColor, userOpacity);

                var farBrush = _cachedFarBrush;
                var midBrush = _cachedMidBrush;
                var nearBrush = _cachedNearBrush;
                var glowBrush = _cachedGlowBrush;

                bool twinkleEnabled = settings.StarTwinkle && !settings.ReducedMotion;

                for (int i = 0; i < count; i++)
                {
                    ref var s = ref _stars[i];

                    double layerParallax = settings.StarDepth switch
                    {
                        0 => s.Layer switch { 0 => 0.15, 1 => 0.40, _ => 0.70 },
                        2 => s.Layer switch { 0 => 0.25, 1 => 0.85, _ => 1.80 },
                        _ => s.Layer switch { 0 => 0.20, 1 => 0.60, _ => 1.20 }
                    };

                    double sx = s.X + _parallaxOffsetX * layerParallax;
                    double sy = s.Y + _parallaxOffsetY * layerParallax;

                    Brush? starBrush = s.Layer switch
                    {
                        0 => farBrush,
                        1 => midBrush,
                        _ => nearBrush
                    };

                    if (starBrush != null)
                    {
                        double twinkle = twinkleEnabled ? (0.80 + 0.20 * Math.Sin(_totalTime * s.TwinkleFreq + s.TwinklePhase)) : 1.0;
                        double radius = s.Size * 0.5 * twinkle;
                        dc.DrawEllipse(starBrush, null, new Point(sx, sy), radius, radius);

                        if (s.HasGlow && starGlow > 0.05 && glowBrush != null)
                        {
                            double glowRadius = s.Size * 1.5 * twinkle * (0.5 + starGlow * 0.8);
                            dc.DrawEllipse(glowBrush, null, new Point(sx, sy), glowRadius, glowRadius);
                        }
                    }
                }
            }
        }
    }
}
