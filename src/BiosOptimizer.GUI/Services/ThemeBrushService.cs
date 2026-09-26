#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace BiosOptimizer.GUI.Services
{
    public enum ThemeMode
    {
        Solid = 0,
        Gradient = 1
    }

    public class GradientColorStop
    {
        public Color Color { get; set; }
        public string Hex { get; set; } = "#00D4FF";
        public double Offset { get; set; } = 0.0;

        public GradientColorStop() { }

        public GradientColorStop(string hex, double offset)
        {
            Hex = hex;
            Offset = offset;
            Color = ThemeBrushService.ParseColor(hex, Colors.Cyan);
        }

        public GradientColorStop(Color color, double offset)
        {
            Color = color;
            Hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            Offset = offset;
        }
    }

    /// <summary>
    /// Master Centralized Theme & Global Color Engine for Error Optimizer V3.
    /// Supports both:
    /// 1. SOLID COLOR THEME (100% Hue Preserved HSV Studio)
    /// 2. MULTI-COLOR GRADIENT THEME (2 to 5 Color Stops, Live Offset/Angle/Animation Control)
    /// </summary>
    public sealed class ThemeBrushService
    {
        private static readonly Lazy<ThemeBrushService> _instance =
            new(() => new ThemeBrushService());
        public static ThemeBrushService Instance => _instance.Value;

        public ThemeMode CurrentThemeMode { get; private set; } = ThemeMode.Solid;
        public Color CurrentAccentColor { get; private set; } = Color.FromRgb(0x00, 0xE5, 0xFF);
        public List<GradientColorStop> CurrentGradientStops { get; private set; } = new();
        public double CurrentGradientAngle { get; private set; } = 90.0;
        public string CurrentGradientDirection { get; private set; } = "LeftToRight";
        public int CurrentGradientAnimationMode { get; private set; } = 1; // 0=Off, 1=Subtle, 2=Dynamic

        public Brush CurrentGradientBrush { get; private set; } = Brushes.Transparent;
        public double CurrentWindowGlassTransparency { get; private set; } = 0.0;
        public double CurrentCardOpacity { get; private set; } = 0.90;
        public bool CurrentGlowEnabled { get; private set; } = true;
        public double CurrentGlowIntensity { get; private set; } = 0.6;

        public event Action<Color>? AccentColorChanged;
        public event Action<ThemeMode>? ThemeModeChanged;
        public event Action? ThemeChanged;

        private ThemeBrushService()
        {
            // Default gradient stops (Cyber Blue: Cyan -> Blue -> Purple)
            CurrentGradientStops.Add(new GradientColorStop("#00D4FF", 0.0));
            CurrentGradientStops.Add(new GradientColorStop("#0051FF", 0.5));
            CurrentGradientStops.Add(new GradientColorStop("#7A00FF", 1.0));
        }

        /// <summary>
        /// Applies all visual appearance settings from AppSettingsService at startup.
        /// </summary>
        public void ApplyAll(AppSettingsService settings)
        {
            CurrentThemeMode = (ThemeMode)settings.ThemeMode;
            if (CurrentThemeMode == ThemeMode.Gradient)
            {
                var stops = ParseGradientStopsJson(settings.GradientStops);
                ApplyGradient(stops, settings.GradientAngle, settings.GradientDirection, settings.GradientAnimationMode);
            }
            else
            {
                ApplyAccentColor(settings.AccentColor);
            }

            ApplyWindowGlassTransparency(settings.WindowGlassTransparency);
            ApplyOpacity(settings.CardOpacity);
            ApplyGlow(settings.GlowEnabled);
        }

        public void SetThemeMode(ThemeMode mode)
        {
            CurrentThemeMode = mode;
            if (mode == ThemeMode.Gradient)
            {
                ApplyGradient(CurrentGradientStops, CurrentGradientAngle, CurrentGradientDirection, CurrentGradientAnimationMode);
            }
            else
            {
                ApplyAccentColor(CurrentAccentColor);
            }
            ThemeModeChanged?.Invoke(mode);
            ThemeChanged?.Invoke();
        }

        public void ApplyAccentColor(string hexColor)
        {
            Color color = ParseColor(hexColor, Color.FromRgb(0x00, 0xE5, 0xFF));
            ApplyAccentColor(color);
        }

        public void ApplyAccentColor(Color primary)
        {
            CurrentAccentColor = primary;
            if (Application.Current == null) return;

            var action = new Action(() =>
            {
                Color bright = AdjustBrightness(primary, 1.35);
                Color light = AdjustBrightness(primary, 1.18);
                Color dark = AdjustBrightness(primary, 0.68);
                Color soft = Color.FromArgb(0x22, primary.R, primary.G, primary.B);
                Color border = Color.FromArgb(0x55, primary.R, primary.G, primary.B);
                Color borderSubtle = Color.FromArgb(0x28, primary.R, primary.G, primary.B);
                Color selection = Color.FromArgb(0x35, primary.R, primary.G, primary.B);
                Color glow = Color.FromArgb(0x60, primary.R, primary.G, primary.B);
                Color glowSoft = Color.FromArgb(0x30, primary.R, primary.G, primary.B);
                Color glowStrong = Color.FromArgb(0x99, primary.R, primary.G, primary.B);
                Color transparent = Color.FromArgb(0x00, primary.R, primary.G, primary.B);
                Color overlay = Color.FromArgb(0x14, primary.R, primary.G, primary.B);
                Color bgTint = Color.FromArgb(0x18, primary.R, primary.G, primary.B);
                Color ambientGlow = Color.FromArgb(0x28, primary.R, primary.G, primary.B);

                double luminance = (0.299 * primary.R + 0.587 * primary.G + 0.114 * primary.B) / 255.0;
                Color contrastText = luminance > 0.68 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White;

                SetBrush("AccentBrush", new SolidColorBrush(primary));
                SetBrush("AccentBaseBrush", new SolidColorBrush(primary));
                SetBrush("AccentPrimaryBrush", new SolidColorBrush(primary));
                SetBrush("AccentBrightBrush", new SolidColorBrush(bright));
                SetBrush("AccentLightBrush", new SolidColorBrush(light));
                SetBrush("AccentDarkBrush", new SolidColorBrush(dark));
                SetBrush("AccentHoverBrush", new SolidColorBrush(bright));
                SetBrush("AccentPressedBrush", new SolidColorBrush(dark));
                SetBrush("AccentFocusBrush", new SolidColorBrush(bright));
                SetBrush("AccentSoftBrush", new SolidColorBrush(soft));
                SetBrush("AccentBorderBrush", new SolidColorBrush(border));
                SetBrush("AccentBorderSubtleBrush", new SolidColorBrush(borderSubtle));
                SetBrush("AccentProgressBrush", new SolidColorBrush(primary));
                SetBrush("AccentSelectionBrush", new SolidColorBrush(selection));
                SetBrush("AccentGlowBrush", new SolidColorBrush(glow));
                SetBrush("AccentGlowSoftBrush", new SolidColorBrush(glowSoft));
                SetBrush("AccentGlowStrongBrush", new SolidColorBrush(glowStrong));
                SetBrush("AccentTransparentBrush", new SolidColorBrush(transparent));
                SetBrush("AccentTextBrush", new SolidColorBrush(primary));
                SetBrush("AccentContrastTextBrush", new SolidColorBrush(contrastText));
                SetBrush("AccentOverlayBrush", new SolidColorBrush(overlay));
                SetBrush("AccentSliderBrush", new SolidColorBrush(primary));
                SetBrush("AccentToggleBrush", new SolidColorBrush(primary));
                SetBrush("AccentTrackBrush", new SolidColorBrush(soft));
                SetBrush("AccentBackgroundTintBrush", new SolidColorBrush(bgTint));
                SetBrush("AccentAmbientGlowBrush", new SolidColorBrush(ambientGlow));

                SetBrush("PrimaryBrush", new SolidColorBrush(primary));
                SetBrush("AccentBrushDark", new SolidColorBrush(dark));
                SetBrush("AccentBrushLight", new SolidColorBrush(light));
                SetBrush("GlowBrush", new SolidColorBrush(glow));

                SetColor("AccentColor", primary);
                SetColor("AccentBaseColor", primary);
                SetColor("AccentBrightColor", bright);
                SetColor("AccentLightColor", light);
                SetColor("AccentDarkColor", dark);
                SetColor("AccentGlowColor", glow);
                SetColor("BrandPrimaryColor", primary);
                SetColor("BrandAccentColor", bright);

                var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                grad.GradientStops.Add(new GradientStop(bright, 0));
                grad.GradientStops.Add(new GradientStop(dark, 1));
                grad.Freeze();
                SetBrush("PrimaryGradientBrush", grad);
                SetBrush("AccentGradientBrush", grad);
                CurrentGradientBrush = grad;

                var borderGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                borderGrad.GradientStops.Add(new GradientStop(border, 0));
                borderGrad.GradientStops.Add(new GradientStop(borderSubtle, 1));
                borderGrad.Freeze();
                SetBrush("AccentBorderGradientBrush", borderGrad);

                var bgGrad = new RadialGradientBrush { Center = new Point(0.85, 0.15), GradientOrigin = new Point(0.85, 0.15), RadiusX = 0.8, RadiusY = 0.8 };
                bgGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x20, primary.R, primary.G, primary.B), 0));
                bgGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, primary.R, primary.G, primary.B), 1));
                bgGrad.Freeze();
                SetBrush("AccentBackgroundGradientBrush", bgGrad);

                EnforceSemanticBrushes();
            });

            RunOnDispatcher(action);
            ApplyWindowGlassTransparency(CurrentWindowGlassTransparency);
            ApplyGlow(CurrentGlowEnabled);
            AccentColorChanged?.Invoke(primary);
            ThemeChanged?.Invoke();
        }

        public void ApplyGradient(IEnumerable<GradientColorStop> stops, double angle = 90.0, string direction = "LeftToRight", int animationMode = 1)
        {
            var stopList = stops.OrderBy(s => s.Offset).ToList();
            if (stopList.Count < 2)
            {
                stopList = new List<GradientColorStop>
                {
                    new("#00D4FF", 0.0),
                    new("#7A00FF", 1.0)
                };
            }

            CurrentGradientStops = stopList;
            CurrentGradientAngle = angle;
            CurrentGradientDirection = direction;
            CurrentGradientAnimationMode = animationMode;

            Color primary = stopList[0].Color;
            CurrentAccentColor = primary;

            if (Application.Current == null) return;

            var action = new Action(() =>
            {
                var (startPt, endPt) = ComputeGradientPoints(angle, direction);

                var grad = new LinearGradientBrush { StartPoint = startPt, EndPoint = endPt };
                var borderGrad = new LinearGradientBrush { StartPoint = startPt, EndPoint = endPt };
                var glowGrad = new LinearGradientBrush { StartPoint = startPt, EndPoint = endPt };
                var softGrad = new LinearGradientBrush { StartPoint = startPt, EndPoint = endPt };

                foreach (var s in stopList)
                {
                    grad.GradientStops.Add(new GradientStop(s.Color, s.Offset));

                    byte borderAlpha = (byte)Math.Clamp(s.Color.A * 0.55, 30, 200);
                    borderGrad.GradientStops.Add(new GradientStop(Color.FromArgb(borderAlpha, s.Color.R, s.Color.G, s.Color.B), s.Offset));

                    byte glowAlpha = (byte)Math.Clamp(s.Color.A * 0.65, 20, 220);
                    glowGrad.GradientStops.Add(new GradientStop(Color.FromArgb(glowAlpha, s.Color.R, s.Color.G, s.Color.B), s.Offset));

                    byte softAlpha = (byte)Math.Clamp(s.Color.A * 0.22, 10, 80);
                    softGrad.GradientStops.Add(new GradientStop(Color.FromArgb(softAlpha, s.Color.R, s.Color.G, s.Color.B), s.Offset));
                }

                grad.Freeze();
                borderGrad.Freeze();
                glowGrad.Freeze();
                softGrad.Freeze();

                CurrentGradientBrush = grad;

                SetBrush("PrimaryGradientBrush", grad);
                SetBrush("AccentGradientBrush", grad);
                SetBrush("AccentBorderGradientBrush", borderGrad);
                SetBrush("AccentGlowGradientBrush", glowGrad);
                SetBrush("AccentSoftGradientBrush", softGrad);

                Color lastColor = stopList.Last().Color;
                Color bright = AdjustBrightness(primary, 1.35);
                Color light = AdjustBrightness(primary, 1.18);
                Color dark = AdjustBrightness(primary, 0.68);

                SetColor("AccentColor", primary);
                SetColor("AccentBaseColor", primary);
                SetColor("AccentBrightColor", bright);
                SetColor("AccentLightColor", light);
                SetColor("AccentDarkColor", dark);
                SetColor("AccentGlowColor", Color.FromArgb(0x60, primary.R, primary.G, primary.B));
                SetColor("BrandPrimaryColor", primary);
                SetColor("BrandAccentColor", lastColor);

                // Set Primary and Border brushes for seamless gradient integration
                SetBrush("AccentBorderBrush", borderGrad);
                SetBrush("AccentBorderSubtleBrush", borderGrad);
                SetBrush("AccentGlowBrush", glowGrad);
                SetBrush("AccentGlowSoftBrush", softGrad);
                SetBrush("AccentGlowStrongBrush", glowGrad);
                SetBrush("AccentSoftBrush", softGrad);
                SetBrush("AccentBrush", new SolidColorBrush(primary));
                SetBrush("AccentBaseBrush", new SolidColorBrush(primary));
                SetBrush("AccentPrimaryBrush", new SolidColorBrush(primary));
                SetBrush("AccentBrightBrush", new SolidColorBrush(lastColor));
                SetBrush("AccentLightBrush", new SolidColorBrush(light));
                SetBrush("AccentDarkBrush", new SolidColorBrush(dark));
                SetBrush("AccentHoverBrush", grad);
                SetBrush("AccentPressedBrush", new SolidColorBrush(dark));
                SetBrush("AccentFocusBrush", new SolidColorBrush(bright));
                SetBrush("AccentProgressBrush", grad);
                SetBrush("AccentSelectionBrush", softGrad);
                SetBrush("AccentTextBrush", new SolidColorBrush(primary));
                SetBrush("AccentSliderBrush", grad);
                SetBrush("AccentToggleBrush", new SolidColorBrush(primary));
                SetBrush("AccentTrackBrush", softGrad);
                SetBrush("AccentOverlayBrush", softGrad);
                SetBrush("AccentBackgroundTintBrush", softGrad);
                SetBrush("AccentAmbientGlowBrush", glowGrad);

                SetBrush("PrimaryBrush", new SolidColorBrush(primary));
                SetBrush("AccentBrushDark", new SolidColorBrush(dark));
                SetBrush("AccentBrushLight", new SolidColorBrush(light));
                SetBrush("GlowBrush", glowGrad);

                Color contrastText = Colors.White;
                SetBrush("AccentContrastTextBrush", new SolidColorBrush(contrastText));

                var bgGrad = new RadialGradientBrush { Center = new Point(0.85, 0.15), GradientOrigin = new Point(0.85, 0.15), RadiusX = 0.8, RadiusY = 0.8 };
                bgGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, primary.R, primary.G, primary.B), 0));
                bgGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, lastColor.R, lastColor.G, lastColor.B), 1));
                bgGrad.Freeze();
                SetBrush("AccentBackgroundGradientBrush", bgGrad);

                EnforceSemanticBrushes();
            });

            RunOnDispatcher(action);
            ApplyWindowGlassTransparency(CurrentWindowGlassTransparency);
            ApplyGlow(CurrentGlowEnabled);
            AccentColorChanged?.Invoke(primary);
            ThemeChanged?.Invoke();
        }

        private static (Point Start, Point End) ComputeGradientPoints(double angle, string direction)
        {
            switch (direction.ToLowerInvariant())
            {
                case "topbottom" or "top_to_bottom" or "top to bottom":
                    return (new Point(0.5, 0.0), new Point(0.5, 1.0));
                case "diagonal":
                    return (new Point(0.0, 0.0), new Point(1.0, 1.0));
                case "diagonalreverse" or "diagonal_reverse":
                    return (new Point(1.0, 0.0), new Point(0.0, 1.0));
                case "radial":
                    return (new Point(0.5, 0.5), new Point(1.0, 1.0));
                case "leftright" or "left_to_right" or "left to right":
                default:
                    // Use angle if custom
                    if (Math.Abs(angle - 90.0) < 0.1) return (new Point(0.0, 0.5), new Point(1.0, 0.5));
                    double rad = (angle - 90.0) * (Math.PI / 180.0);
                    double x = Math.Cos(rad) * 0.5;
                    double y = Math.Sin(rad) * 0.5;
                    return (new Point(0.5 - x, 0.5 - y), new Point(0.5 + x, 0.5 + y));
            }
        }

                /// <summary>
        /// Derives a single, solid, high-luminance focal color for the Siri intelligence visualizer center point.
        /// In Solid Theme: returns CurrentAccentColor.
        /// In Gradient Theme: returns the selected/active single focal stop with accessibility contrast protection.
        /// </summary>
        public Color GetSiriFocalColor()
        {
            if (CurrentThemeMode == ThemeMode.Solid)
            {
                return CurrentAccentColor;
            }

            var stops = CurrentGradientStops;
            if (stops == null || stops.Count == 0)
            {
                return CurrentAccentColor;
            }

            string mode = AppSettingsService.Instance.SiriFocalColorMode;
            Color focal;
            switch (mode?.ToUpperInvariant())
            {
                case "COLOR 1" or "COLOR1" or "STOP 1":
                    focal = stops[0].Color;
                    break;
                case "COLOR 2" or "COLOR2" or "STOP 2":
                    focal = stops.Count > 1 ? stops[1].Color : stops[0].Color;
                    break;
                case "COLOR 3" or "COLOR3" or "STOP 3":
                    focal = stops.Count > 2 ? stops[2].Color : stops[^1].Color;
                    break;
                case "CUSTOM":
                    focal = ParseColor(AppSettingsService.Instance.SiriFocalCustomColor, stops[0].Color);
                    break;
                case "AUTO":
                default:
                    // AUTO: Select the most vibrant / high-contrast stop from the gradient palette
                    // If 3+ stops, the center/middle stop is visually dominant
                    if (stops.Count >= 3)
                    {
                        int mid = stops.Count / 2;
                        focal = stops[mid].Color;
                    }
                    else
                    {
                        focal = stops[0].Color;
                    }
                    break;
            }

            // Accessibility: guarantee sufficient brightness and contrast against dark backgrounds
            // If the selected stop is dark (e.g. deep blue/purple), boost its brightness within the exact same hue.
            double luminance = (0.299 * focal.R + 0.587 * focal.G + 0.114 * focal.B) / 255.0;
            if (luminance < 0.35)
            {
                focal = AdjustBrightness(focal, 1.45);
            }

            return focal;
        }

public Color GetColorAtOffset(double offset)
        {
            if (CurrentThemeMode == ThemeMode.Solid || CurrentGradientStops.Count == 0)
            {
                return CurrentAccentColor;
            }

            double t = Math.Clamp(offset, 0.0, 1.0);
            if (CurrentGradientStops.Count == 1) return CurrentGradientStops[0].Color;

            var stops = CurrentGradientStops;
            for (int i = 0; i < stops.Count - 1; i++)
            {
                var s1 = stops[i];
                var s2 = stops[i + 1];
                if (t >= s1.Offset && t <= s2.Offset)
                {
                    double range = s2.Offset - s1.Offset;
                    double localT = range > 0.0001 ? (t - s1.Offset) / range : 0.0;
                    byte r = (byte)(s1.Color.R + (s2.Color.R - s1.Color.R) * localT);
                    byte g = (byte)(s1.Color.G + (s2.Color.G - s1.Color.G) * localT);
                    byte b = (byte)(s1.Color.B + (s2.Color.B - s1.Color.B) * localT);
                    byte a = (byte)(s1.Color.A + (s2.Color.A - s1.Color.A) * localT);
                    return Color.FromArgb(a, r, g, b);
                }
            }

            if (t < stops[0].Offset) return stops[0].Color;
            return stops[^1].Color;
        }

        public void ApplyWindowGlassTransparency(double transparency)
        {
            CurrentWindowGlassTransparency = Math.Clamp(transparency, 0.0, 1.0);
            if (Application.Current == null) return;
            var action = new Action(() =>
            {
                byte alpha = (byte)((1.0 - CurrentWindowGlassTransparency) * 255);
                byte r = (byte)Math.Clamp(0x0A + CurrentAccentColor.R * 0.03, 0, 255);
                byte g = (byte)Math.Clamp(0x0A + CurrentAccentColor.G * 0.03, 0, 255);
                byte b = (byte)Math.Clamp(0x0C + CurrentAccentColor.B * 0.03, 0, 255);

                var rootColor = Color.FromArgb(alpha, r, g, b);
                var rootBrush = new SolidColorBrush(rootColor);
                rootBrush.Freeze();

                SetBrush("BgRootBrush", rootBrush);
                SetBrush("BgWindowBrush", rootBrush);
                SetBrush("AppBackgroundBrush", rootBrush);
                SetColor("AppBackgroundColor", rootColor);
            });

            RunOnDispatcher(action);
        }

        public void ApplyOpacity(double opacity)
        {
            CurrentCardOpacity = Math.Clamp(opacity, 0.0, 1.0);
            if (Application.Current == null) return;
            var action = new Action(() =>
            {
                byte alphaTop = (byte)(CurrentCardOpacity * 230);
                byte alphaBottom = (byte)(CurrentCardOpacity * 170);

                var cardGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                cardGrad.GradientStops.Add(new GradientStop(Color.FromArgb(alphaTop, 0x18, 0x1C, 0x26), 0));
                cardGrad.GradientStops.Add(new GradientStop(Color.FromArgb(alphaBottom, 0x0E, 0x10, 0x17), 1));
                cardGrad.Freeze();
                SetBrush("BgCardBrush", cardGrad);

                var solidCard = new SolidColorBrush(Color.FromArgb(alphaTop, 0x14, 0x17, 0x22));
                solidCard.Freeze();
                SetBrush("BgCardSolidBrush", solidCard);

                var sidebarGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                sidebarGrad.GradientStops.Add(new GradientStop(Color.FromArgb(alphaTop, 0x10, 0x13, 0x1C), 0));
                sidebarGrad.GradientStops.Add(new GradientStop(Color.FromArgb(alphaBottom, 0x0B, 0x0D, 0x13), 1));
                sidebarGrad.Freeze();
                SetBrush("BgSidebarBrush", sidebarGrad);
            });

            RunOnDispatcher(action);
        }

        public void ApplyGlow(bool enabled)
        {
            CurrentGlowEnabled = enabled;
            if (Application.Current == null) return;
            var action = new Action(() =>
            {
                if (enabled)
                {
                    if (CurrentThemeMode == ThemeMode.Gradient)
                    {
                        var glowGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                        foreach (var s in CurrentGradientStops)
                        {
                            byte glowAlpha = (byte)Math.Clamp(s.Color.A * 0.60, 20, 200);
                            glowGrad.GradientStops.Add(new GradientStop(Color.FromArgb(glowAlpha, s.Color.R, s.Color.G, s.Color.B), s.Offset));
                        }
                        glowGrad.Freeze();
                        SetBrush("AccentGlowBrush", glowGrad);
                    }
                    else
                    {
                        Color primary = CurrentAccentColor;
                        Color glow = Color.FromArgb(0x60, primary.R, primary.G, primary.B);
                        SetBrush("AccentGlowBrush", new SolidColorBrush(glow));
                    }
                }
                else
                {
                    SetBrush("AccentGlowBrush", new SolidColorBrush(Colors.Transparent));
                }
            });

            RunOnDispatcher(action);
        }

        private static void EnforceSemanticBrushes()
        {
            SetBrush("SuccessBrush", new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)));
            SetBrush("WarningBrush", new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
            SetBrush("DangerBrush", new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
            SetBrush("ErrorBrush", new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
            SetBrush("StatusGreenBrush", new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)));
            SetBrush("StatusAmberBrush", new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
            SetBrush("StatusRedBrush", new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
            SetBrush("StatusBlueBrush", new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)));
        }

        public static List<GradientColorStop> ParseGradientStopsJson(string? json)
        {
            var list = new List<GradientColorStop>();
            if (string.IsNullOrWhiteSpace(json))
            {
                list.Add(new GradientColorStop("#00D4FF", 0.0));
                list.Add(new GradientColorStop("#0051FF", 0.5));
                list.Add(new GradientColorStop("#7A00FF", 1.0));
                return list;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string hex = el.TryGetProperty("Hex", out var h) ? h.GetString() ?? "#00D4FF" : "#00D4FF";
                    double offset = el.TryGetProperty("Offset", out var o) ? o.GetDouble() : 0.0;
                    list.Add(new GradientColorStop(hex, offset));
                }
            }
            catch
            {
                list.Add(new GradientColorStop("#00D4FF", 0.0));
                list.Add(new GradientColorStop("#7A00FF", 1.0));
            }

            if (list.Count < 2)
            {
                list.Add(new GradientColorStop("#00D4FF", 0.0));
                list.Add(new GradientColorStop("#7A00FF", 1.0));
            }

            return list.OrderBy(s => s.Offset).ToList();
        }

        private static void RunOnDispatcher(Action action)
        {
            if (Application.Current == null) return;
            if (Application.Current.Dispatcher.CheckAccess()) action();
            else Application.Current.Dispatcher.Invoke(action);
        }

        private static void SetBrush(string resourceKey, SolidColorBrush brush)
        {
            brush.Freeze();
            if (Application.Current != null)
            {
                Application.Current.Resources[resourceKey] = brush;
            }
        }

        private static void SetBrush(string resourceKey, Brush brush)
        {
            if (Application.Current != null)
            {
                Application.Current.Resources[resourceKey] = brush;
            }
        }

        private static void SetColor(string resourceKey, Color color)
        {
            if (Application.Current != null)
            {
                Application.Current.Resources[resourceKey] = color;
            }
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex.Trim().TrimStart('#');

            if (hex.Length == 6)
            {
                if (byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                {
                    return Color.FromRgb(r, g, b);
                }
            }
            else if (hex.Length == 8)
            {
                if (byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                    byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                {
                    return Color.FromArgb(a, r, g, b);
                }
            }
            return fallback;
        }

        public static Color AdjustBrightness(Color c, double factor)
        {
            ColorToHsv(c, out double h, out double s, out double v);
            v = Math.Clamp(v * factor, 0.0, 1.0);
            return ColorFromHsv(h, s, v, c.A);
        }

        public static Color ColorFromHsv(double hue, double saturation, double value, byte alpha = 255)
        {
            hue = Math.Clamp(hue, 0.0, 360.0);
            saturation = Math.Clamp(saturation, 0.0, 1.0);
            value = Math.Clamp(value, 0.0, 1.0);

            if (hue >= 360.0) hue = 0.0;

            double c = value * saturation;
            double x = c * (1.0 - Math.Abs((hue / 60.0) % 2.0 - 1.0));
            double m = value - c;

            double r1 = 0, g1 = 0, b1 = 0;

            if (hue < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (hue < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (hue < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (hue < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (hue < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            byte r = (byte)Math.Clamp(Math.Round((r1 + m) * 255.0), 0, 255);
            byte g = (byte)Math.Clamp(Math.Round((g1 + m) * 255.0), 0, 255);
            byte b = (byte)Math.Clamp(Math.Round((b1 + m) * 255.0), 0, 255);

            return Color.FromArgb(alpha, r, g, b);
        }

        public static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            value = max;

            if (max > 0.00001)
                saturation = delta / max;
            else
                saturation = 0.0;

            if (delta < 0.00001)
            {
                hue = 0.0;
            }
            else
            {
                if (Math.Abs(max - r) < 0.00001)
                    hue = 60.0 * (((g - b) / delta) % 6.0);
                else if (Math.Abs(max - g) < 0.00001)
                    hue = 60.0 * (((b - r) / delta) + 2.0);
                else
                    hue = 60.0 * (((r - g) / delta) + 4.0);

                if (hue < 0.0) hue += 360.0;
            }
        }
    }
}
