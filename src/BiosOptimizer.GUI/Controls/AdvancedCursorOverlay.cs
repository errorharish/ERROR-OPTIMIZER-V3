#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Master High-Precision Luminous Custom Cursor for Error Optimizer V3.
    /// 
    /// Authoritative Position Pipeline:
    /// 1. Hardware Screen Mouse (GetCursorPos / PreviewMouseMove)
    /// 2. Unified Coordinate Conversion (Window.PointFromScreen)
    /// 3. SINGLE SOURCE OF TRUTH: CurrentCursorPosition (_currentPos)
    /// 4. ALL 6 LAYERS (Point, Specular, Point Bloom, Halo Core, Halo Glow, Ambient Bloom)
    ///    are locked 100% to _currentPos in real-time.
    /// </summary>
    public class AdvancedCursorOverlay : FrameworkElement
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private Point _currentPos = new(-500, -500);
        private Point _prevPos = new(-500, -500);

        private double _speed;
        private double _moveAngle;

        private double _hoverScale = 1.0;
        private double _targetHoverScale = 1.0;
        private bool _isHoveringInteractive;

        private double _glowIntensity = 0.85;
        private double _targetGlowIntensity = 0.85;

        private double _visibilityOpacity = 0.0;
        private double _targetVisibilityOpacity = 0.0;
        private bool _isMouseInside;

        private double _pointPressScale = 1.0;
        private double _idleSeconds = 0.0;
        private double _totalTime = 0.0;
        private readonly Stopwatch _stopwatch = new();
        private long _lastTicks;

        private bool _isRenderingHooked;
        private bool _isPaused = false;

        public void PauseRendering()
        {
            _isPaused = true;
            Unhook();
        }

        public void ResumeRendering()
        {
            _isPaused = false;
            UpdateHook();
            // Idle & convergence check: only invalidate if visible and either active or animating
            if (_visibilityOpacity > 0.001 && (_isMouseInside || _visibilityOpacity > 0.01 || _pointPressScale < 0.99 || _hoverScale != _targetHoverScale || _idleSeconds < 1.2))
            {
                InvalidateVisual();
            }
        }

        private Color _targetAccentColor = Color.FromRgb(0x21, 0x96, 0xF3);
        private double _curR = 33, _curG = 150, _curB = 243;
        private byte _lastR = 0, _lastG = 0, _lastB = 0;

        private SolidColorBrush? _cachedPointBrush;
        private SolidColorBrush? _cachedCoreBrush;
        private Pen? _cachedRingCorePen;
        private Pen? _cachedRingGlowPen;
        private Pen? _cachedHoverRingCorePen;
        private Pen? _cachedHoverRingGlowPen;
        private RadialGradientBrush? _cachedAmbientBloomBrush;
        private RadialGradientBrush? _cachedPointBloomBrush;

        public AdvancedCursorOverlay()
        {
            IsHitTestVisible = false;
            Focusable = false;
            ClipToBounds = true;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += (s, e) => UpdateHook();

            AppSettingsService.Instance.SettingsChanged += OnSettingsChanged;
            ThemeBrushService.Instance.AccentColorChanged += OnAccentColorChanged;
            UpdateCachedColorImmediate();
        }

        private void OnAccentColorChanged(Color newColor)
        {
            _targetAccentColor = newColor;
            if (AppSettingsService.Instance.ReducedMotion)
            {
                _curR = newColor.R;
                _curG = newColor.G;
                _curB = newColor.B;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateCachedColorImmediate();
            UpdateHook();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unhook();
        }

        private void UpdateHook()
        {
            if (_isPaused) return;
            if (IsVisible && !_isRenderingHooked)
            {
                CompositionTarget.Rendering += OnCompositionRendering;
                _isRenderingHooked = true;
                _stopwatch.Restart();
                _lastTicks = _stopwatch.ElapsedTicks;
            }
            else if (!IsVisible && _isRenderingHooked)
            {
                Unhook();
            }
        }

        private void _legacyUpdateHook()
        {
            if (IsVisible && !_isRenderingHooked)
            {
                _stopwatch.Restart();
                _lastTicks = _stopwatch.ElapsedTicks;
                CompositionTarget.Rendering += OnCompositionRendering;
                _isRenderingHooked = true;
            }
            else if (!IsVisible && _isRenderingHooked)
            {
                Unhook();
            }
        }

        private void Unhook()
        {
            if (_isRenderingHooked)
            {
                CompositionTarget.Rendering -= OnCompositionRendering;
                _isRenderingHooked = false;
                _stopwatch.Stop();
            }
        }

        private void OnSettingsChanged()
        {
            UpdateCachedColorImmediate();
            // Idle & convergence check: only invalidate if visible and either active or animating
            if (_visibilityOpacity > 0.001 && (_isMouseInside || _visibilityOpacity > 0.01 || _pointPressScale < 0.99 || _hoverScale != _targetHoverScale || _idleSeconds < 1.2))
            {
                InvalidateVisual();
            }
        }

        private void UpdateCachedColorImmediate()
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
                _targetAccentColor = Color.FromRgb(0x21, 0x96, 0xF3);
            }
        }

        public void HandleMouseMove(Point pos, DependencyObject? originalSource)
        {
            try
            {
                _currentPos = pos;
                _idleSeconds = 0.0;

                if (!_isMouseInside)
                {
                    _isMouseInside = true;
                    _targetVisibilityOpacity = 1.0;
                    _visibilityOpacity = 1.0;
                    _prevPos = pos;
                }

                bool isInteractive = CheckIsInteractive(originalSource);
                if (isInteractive != _isHoveringInteractive)
                {
                    _isHoveringInteractive = isInteractive;
                    _targetHoverScale = isInteractive ? 1.25 : 1.0;
                    _targetGlowIntensity = isInteractive ? 1.0 : 0.85;
                }
            }
            catch { }
        }

        public void HandleMouseEnter(Point pos)
        {
            try
            {
                _currentPos = pos;
                _prevPos = pos;
                _isMouseInside = true;
                _targetVisibilityOpacity = 1.0;
                _visibilityOpacity = 1.0;
                _idleSeconds = 0.0;
            }
            catch { }
        }

        public void HandleMouseLeave()
        {
            try
            {
                _isMouseInside = false;
                _targetVisibilityOpacity = 0.0;
                _isHoveringInteractive = false;
                _targetHoverScale = 1.0;
                _targetGlowIntensity = 0.85;
            }
            catch { }
        }

        public void HandleMouseDown(Point pos, MouseButton button)
        {
            try
            {
                _currentPos = pos;
                _pointPressScale = 0.72;
                _idleSeconds = 0.0;
            }
            catch { }
        }

        public void HandleMouseUp(Point pos, MouseButton button)
        {
            try
            {
                _currentPos = pos;
                _pointPressScale = 1.0;
                _idleSeconds = 0.0;
            }
            catch { }
        }

        private static DependencyObject? _lastCheckedObj = null;
        private static bool _lastInteractiveResult = false;
        private static long _lastHitCheckTicks = 0;

        private static bool CheckIsInteractive(DependencyObject? obj)
        {
            if (obj == null) return false;

            try
            {
                long now = Stopwatch.GetTimestamp();
                int throttleMs = EffectBudgetManager.Instance.Cursor.HitTestThrottleMs;
                if (ReferenceEquals(obj, _lastCheckedObj) && throttleMs > 0)
                {
                    double elapsedMs = (double)(now - _lastHitCheckTicks) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs < throttleMs)
                    {
                        return _lastInteractiveResult;
                    }
                }

                _lastCheckedObj = obj;
                _lastHitCheckTicks = now;

                DependencyObject? curr = obj;
                int depth = 0;

                while (curr != null && depth < 16)
                {
                    // 1. Direct interactive control check
                    if (curr is ButtonBase ||
                        curr is TextBoxBase ||
                        curr is PasswordBox ||
                        curr is ComboBox ||
                        curr is Slider ||
                        curr is Hyperlink ||
                        curr is TabItem ||
                        curr is TreeViewItem ||
                        curr is ListBoxItem ||
                        curr is DataGridRow ||
                        curr is ScrollBar ||
                        curr is Thumb ||
                        curr is ToggleButton)
                    {
                        _lastInteractiveResult = true;
                        return true;
                    }

                    // 2. FrameworkElement properties check (Cursor == Hand or Tag == Interactive)
                    if (curr is FrameworkElement fe)
                    {
                        if (fe.Cursor == Cursors.Hand) { _lastInteractiveResult = true; return true; }
                        if (fe.Tag is string tag && (tag.Equals("Interactive", StringComparison.OrdinalIgnoreCase) || 
                                                     tag.Equals("Nav", StringComparison.OrdinalIgnoreCase) ||
                                                     tag.Equals("Button", StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }

                    // 3. FrameworkContentElement (Run, Inline, Span, Bold, Italic, TextElement, etc.)
                    // NEVER pass a non-Visual to VisualTreeHelper.GetParent!
                    if (curr is FrameworkContentElement fce)
                    {
                        if (fce.Cursor == Cursors.Hand) { _lastInteractiveResult = true; return true; }
                        curr = fce.Parent;
                    }
                    else if (curr is Visual || curr is System.Windows.Media.Media3D.Visual3D)
                    {
                        // Only Visual or Visual3D is allowed in VisualTreeHelper.GetParent
                        curr = VisualTreeHelper.GetParent(curr);
                    }
                    else
                    {
                        // Logical fallback for any other DependencyObject
                        curr = LogicalTreeHelper.GetParent(curr);
                    }

                    depth++;
                }
            }
            catch
            {
                // Safe fallback — never crash the rendering pipeline or mouse movement
            }

            _lastInteractiveResult = false;
            return false;
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            long currentTicks = _stopwatch.ElapsedTicks;
            double dt = (double)(currentTicks - _lastTicks) / Stopwatch.Frequency;
            _lastTicks = currentTicks;

            if (dt <= 0.0) dt = 0.016;
            if (dt > 0.1) dt = 0.1;

            _totalTime += dt;

            var settings = AppSettingsService.Instance;

            double targetR = _targetAccentColor.R;
            double targetG = _targetAccentColor.G;
            double targetB = _targetAccentColor.B;

            double colorLerpFactor = settings.ReducedMotion ? 1.0 : Math.Min(1.0, 14.0 * dt);
            _curR += (targetR - _curR) * colorLerpFactor;
            _curG += (targetG - _curG) * colorLerpFactor;
            _curB += (targetB - _curB) * colorLerpFactor;

            double fadeSpeed = 16.0 * dt;
            _visibilityOpacity += (_targetVisibilityOpacity - _visibilityOpacity) * Math.Min(1.0, fadeSpeed);

            if (_isMouseInside && _visibilityOpacity > 0.01)
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null && parentWindow.IsActive)
                {
                    if (GetCursorPos(out POINT pt))
                    {
                        try
                        {
                            var screenPoint = new Point(pt.X, pt.Y);
                            var windowPoint = parentWindow.PointFromScreen(screenPoint);
                            var elementPoint = parentWindow.TranslatePoint(windowPoint, this);

                            if (elementPoint.X >= -100 && elementPoint.X <= ActualWidth + 100 &&
                                elementPoint.Y >= -100 && elementPoint.Y <= ActualHeight + 100)
                            {
                                _currentPos = elementPoint;
                            }
                        }
                        catch { }
                    }
                }
            }

            _idleSeconds += dt;

            double dx = _currentPos.X - _prevPos.X;
            double dy = _currentPos.Y - _prevPos.Y;
            _prevPos = _currentPos;

            double instantSpeed = Math.Sqrt(dx * dx + dy * dy) / Math.Max(0.001, dt);
            _speed += (instantSpeed - _speed) * Math.Min(1.0, 16.0 * dt);

            if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
            {
                _moveAngle = Math.Atan2(dy, dx);
            }

            if (settings.ReducedMotion || !settings.AnimationsEnabled)
            {
                _hoverScale = _targetHoverScale;
                _glowIntensity = _targetGlowIntensity;
                _pointPressScale = 1.0;
            }
            else
            {
                _hoverScale += (_targetHoverScale - _hoverScale) * Math.Min(1.0, 24.0 * dt);
                _glowIntensity += (_targetGlowIntensity - _glowIntensity) * Math.Min(1.0, 20.0 * dt);

                if (_pointPressScale < 1.0)
                {
                    _pointPressScale += (1.0 - _pointPressScale) * Math.Min(1.0, 24.0 * dt);
                }
            }

            // Idle & convergence check: only invalidate if visible and either active or animating
            if (_visibilityOpacity > 0.001 && (_isMouseInside || _visibilityOpacity > 0.01 || _pointPressScale < 0.99 || _hoverScale != _targetHoverScale || _idleSeconds < 1.2))
            {
                InvalidateVisual();
            }
        }

        private double _lastGlowIntensity = -1.0;

        private void EnsureCachedBrushes(byte r, byte g, byte b, double glowIntensity)
        {
            if (_cachedPointBrush == null || _lastR != r || _lastG != g || _lastB != b || Math.Abs(_lastGlowIntensity - glowIntensity) > 0.02)
            {
                _lastR = r;
                _lastG = g;
                _lastB = b;
                _lastGlowIntensity = glowIntensity;

                double glowMult = Math.Clamp(glowIntensity, 0.1, 2.0);

                var ptBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
                ptBrush.Freeze();
                _cachedPointBrush = ptBrush;

                var coreBrush = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
                coreBrush.Freeze();
                _cachedCoreBrush = coreBrush;

                var ringBrush = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                ringBrush.Freeze();
                var ringPen = new Pen(ringBrush, 2.0);
                ringPen.Freeze();
                _cachedRingCorePen = ringPen;

                byte ringGlowAlpha = (byte)Math.Clamp((int)(140 * glowMult), 0, 255);
                var ringGlowBrush = new SolidColorBrush(Color.FromArgb(ringGlowAlpha, r, g, b));
                ringGlowBrush.Freeze();
                var ringGlowPen = new Pen(ringGlowBrush, 5.0);
                ringGlowPen.Freeze();
                _cachedRingGlowPen = ringGlowPen;

                var hoverRingBrush = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                hoverRingBrush.Freeze();
                var hoverRingPen = new Pen(hoverRingBrush, 2.4);
                hoverRingPen.Freeze();
                _cachedHoverRingCorePen = hoverRingPen;

                byte hoverGlowAlpha = (byte)Math.Clamp((int)(180 * glowMult), 0, 255);
                var hoverRingGlowBrush = new SolidColorBrush(Color.FromArgb(hoverGlowAlpha, r, g, b));
                hoverRingGlowBrush.Freeze();
                var hoverRingGlowPen = new Pen(hoverRingGlowBrush, 7.5);
                hoverRingGlowPen.Freeze();
                _cachedHoverRingGlowPen = hoverRingGlowPen;

                byte amb0 = (byte)Math.Clamp((int)(120 * glowMult), 0, 255);
                byte amb1 = (byte)Math.Clamp((int)(55 * glowMult), 0, 255);
                byte amb2 = (byte)Math.Clamp((int)(18 * glowMult), 0, 255);

                var ambientBloom = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5
                };
                ambientBloom.GradientStops.Add(new GradientStop(Color.FromArgb(amb0, r, g, b), 0.0));
                ambientBloom.GradientStops.Add(new GradientStop(Color.FromArgb(amb1, r, g, b), 0.45));
                ambientBloom.GradientStops.Add(new GradientStop(Color.FromArgb(amb2, r, g, b), 0.8));
                ambientBloom.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                ambientBloom.Freeze();
                _cachedAmbientBloomBrush = ambientBloom;

                byte pt0 = (byte)Math.Clamp((int)(220 * glowMult), 0, 255);
                byte pt1 = (byte)Math.Clamp((int)(80 * glowMult), 0, 255);

                var pointBloom = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5
                };
                pointBloom.GradientStops.Add(new GradientStop(Color.FromArgb(pt0, r, g, b), 0.0));
                pointBloom.GradientStops.Add(new GradientStop(Color.FromArgb(pt1, r, g, b), 0.5));
                pointBloom.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                pointBloom.Freeze();
                _cachedPointBloomBrush = pointBloom;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var settings = AppSettingsService.Instance;
            if (!settings.CursorEffect || _visibilityOpacity <= 0.001 || _currentPos.X < -50.0 || _currentPos.Y < -50.0) return;

            byte r = (byte)Math.Clamp(_curR, 0, 255);
            byte g = (byte)Math.Clamp(_curG, 0, 255);
            byte b = (byte)Math.Clamp(_curB, 0, 255);

            double haloSizeMultiplier = settings.CursorHaloSize;
            double haloGlowMultiplier = settings.CursorGlowIntensity;

            EnsureCachedBrushes(r, g, b, haloGlowMultiplier);

            double idleBreath = 0.0;
            double breathScale = 1.0;
            if (_idleSeconds > 0.3 && _speed < 10.0)
            {
                idleBreath = Math.Sin(_idleSeconds * 2.8) * 0.40;
                breathScale = 1.0 + Math.Sin(_idleSeconds * 2.8) * 0.06;
            }

            if (settings.ReducedMotion || !settings.AnimationsEnabled)
            {
                if (_cachedRingCorePen != null)
                    dc.DrawEllipse(null, _cachedRingCorePen, _currentPos, 12.0 * haloSizeMultiplier, 12.0 * haloSizeMultiplier);
                if (_cachedPointBrush != null)
                    dc.DrawEllipse(_cachedPointBrush, null, _currentPos, 3.2, 3.2);
                return;
            }

            double budgetRing = EffectBudgetManager.Instance.Cursor.HaloBaseRadius;
            double budgetAmbient = EffectBudgetManager.Instance.Cursor.GlowBloomRadius;

            double breathScaled = idleBreath * Math.Clamp(haloSizeMultiplier, 0.2, 1.0);
            double ringRadius = (budgetRing * _hoverScale + breathScaled) * haloSizeMultiplier;
            double ambientRadius = (budgetAmbient * _hoverScale * breathScale) * haloSizeMultiplier;

            var corePen = _isHoveringInteractive ? _cachedHoverRingCorePen : _cachedRingCorePen;
            var glowPen = _isHoveringInteractive ? _cachedHoverRingGlowPen : _cachedRingGlowPen;

            // LAYER 1: Ambient Outer Bloom
            if (settings.GlowEnabled && _cachedAmbientBloomBrush != null)
            {
                dc.DrawEllipse(_cachedAmbientBloomBrush, null, _currentPos, ambientRadius, ambientRadius);
            }

            // LAYER 2: Ring Outer Glow
            if (glowPen != null)
            {
                dc.DrawEllipse(null, glowPen, _currentPos, ringRadius, ringRadius);
            }

            // LAYER 3: Ring Luminous Core
            if (corePen != null)
            {
                dc.DrawEllipse(null, corePen, _currentPos, ringRadius, ringRadius);
            }

            // LAYER 4: Point Bloom
            if (settings.GlowEnabled && _cachedPointBloomBrush != null)
            {
                double ptBloomRad = 12.0 * _pointPressScale;
                dc.DrawEllipse(_cachedPointBloomBrush, null, _currentPos, ptBloomRad, ptBloomRad);
            }

            // LAYER 5: Sharp Pointer Body
            if (_cachedPointBrush != null)
            {
                double ptRadius = 3.2 * _pointPressScale;
                dc.DrawEllipse(_cachedPointBrush, null, _currentPos, ptRadius, ptRadius);
            }

            // LAYER 6: Crisp White Specular Core
            if (_cachedCoreBrush != null)
            {
                double specRadius = 1.4 * _pointPressScale;
                dc.DrawEllipse(_cachedCoreBrush, null, _currentPos, specRadius, specRadius);
            }
        }
    }
}
