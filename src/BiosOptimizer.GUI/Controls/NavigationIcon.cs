#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Master Unified Futuristic Neon Navigation Icon Control for Error Optimizer V3.
    /// 
    /// Architecture:
    /// - SINGLE AUTHORITATIVE ICON REGISTRY: Guarantees 100% icon identity between Sidebar and Page Headers.
    /// - VECTOR-SHARP DIRECT2D RENDERING: 100% crisp geometry with zero rasterization, blur, or pixelation.
    /// - DUAL-LAYER NEON ARCHITECTURE: Sharp foreground vector path + theme-synchronized specular glow overlay.
    /// - THEME-AWARE DYNAMICS: Instantly responds to GlobalThemeService.CurrentAccentColor changes.
    /// </summary>
    public class NavigationIcon : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavigationIcon),
                new PropertyMetadata("Dashboard", OnIconOrStateChanged));

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.Register(nameof(Size), typeof(double), typeof(NavigationIcon),
                new PropertyMetadata(20.0, OnSizeChanged));

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(NavigationIcon),
                new PropertyMetadata(false, OnIconOrStateChanged));

        public static readonly DependencyProperty GlowEnabledProperty =
            DependencyProperty.Register(nameof(GlowEnabled), typeof(bool), typeof(NavigationIcon),
                new PropertyMetadata(true, OnIconOrStateChanged));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public bool GlowEnabled
        {
            get => (bool)GetValue(GlowEnabledProperty);
            set => SetValue(GlowEnabledProperty, value);
        }

        #endregion

        private Grid _rootGrid;
        private Path _glowPath;
        private Path _foregroundPath;
        private DropShadowEffect _neonGlowEffect;

        static NavigationIcon()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NavigationIcon), new FrameworkPropertyMetadata(typeof(NavigationIcon)));
        }

        public NavigationIcon()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            IsHitTestVisible = false;
            Background = Brushes.Transparent;

            _rootGrid = new Grid
            {
                Width = Size,
                Height = Size,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            // Glow Layer (Separate underlying visual layer for soft ambient neon aura)
            _neonGlowEffect = new DropShadowEffect
            {
                Color = ThemeBrushService.Instance.CurrentAccentColor,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.0
            };

            _glowPath = new Path
            {
                Stretch = Stretch.Uniform,
                StrokeThickness = 2.2,
                Fill = Brushes.Transparent,
                Effect = _neonGlowEffect,
                Opacity = 0.0,
                SnapsToDevicePixels = true
            };

            // Foreground Layer (100% Sharp vector path - NEVER blurred)
            _foregroundPath = new Path
            {
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent,
                SnapsToDevicePixels = true
            };

            _rootGrid.Children.Add(_glowPath);
            _rootGrid.Children.Add(_foregroundPath);
            Content = _rootGrid;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            UpdateGeometryAndVisuals();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ThemeBrushService.Instance.AccentColorChanged += OnAccentColorChanged;
            UpdateGeometryAndVisuals();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeBrushService.Instance.AccentColorChanged -= OnAccentColorChanged;
        }

        private void OnAccentColorChanged(Color accent)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _neonGlowEffect.Color = accent;
                UpdateGeometryAndVisuals();
            });
        }

        private static void OnIconOrStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NavigationIcon navIcon)
            {
                navIcon.UpdateGeometryAndVisuals();
            }
        }

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NavigationIcon navIcon && e.NewValue is double size)
            {
                navIcon._rootGrid.Width = size;
                navIcon._rootGrid.Height = size;
                navIcon.Width = size;
                navIcon.Height = size;
            }
        }

        public void UpdateGeometryAndVisuals()
        {
            var geom = GetIconGeometry(Icon);
            _glowPath.Data = geom;
            _foregroundPath.Data = geom;

            var accent = ThemeBrushService.Instance.CurrentAccentColor;
            _neonGlowEffect.Color = accent;

            if (IsActive)
            {
                // Active State: Razor-sharp theme accent foreground with soft ambient glow
                Brush strokeBrush = ThemeBrushService.Instance.CurrentThemeMode == ThemeMode.Gradient
                    ? ThemeBrushService.Instance.CurrentGradientBrush
                    : new SolidColorBrush(accent);

                Color glowColor = ThemeBrushService.Instance.CurrentThemeMode == ThemeMode.Gradient
                    ? ThemeBrushService.Instance.GetColorAtOffset(0.5)
                    : accent;

                _neonGlowEffect.Color = glowColor;
                _foregroundPath.Stroke = strokeBrush;
                _glowPath.Stroke = strokeBrush;
                _glowPath.Opacity = GlowEnabled ? 0.75 : 0.0;
                _neonGlowEffect.Opacity = GlowEnabled ? 0.85 : 0.0;
                _neonGlowEffect.BlurRadius = Size > 22 ? 14 : 10;
            }
            else
            {
                // Inactive State: Neutral metallic silver/slate with zero blur
                var inactiveBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x92, 0xB0));
                _foregroundPath.Stroke = inactiveBrush;
                _glowPath.Stroke = Brushes.Transparent;
                _glowPath.Opacity = 0.0;
                _neonGlowEffect.Opacity = 0.0;
            }
        }

        #region Authoritative Futuristic Icon Geometry Registry (24x24 Uniform ViewBox)

        private static readonly Dictionary<string, Geometry> _geometryCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _geomLock = new();

        public static Geometry GetIconGeometry(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) key = "Dashboard";

            lock (_geomLock)
            {
                if (_geometryCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                string pathData = GetPathDataForKey(key);
                var geom = Geometry.Parse(pathData);
                geom.Freeze();
                _geometryCache[key] = geom;
                return geom;
            }
        }

        private static string GetPathDataForKey(string key)
        {
            return key.Trim().ToLowerInvariant() switch
            {
                // 1. Dashboard / Command Center
                "dashboard" or "dash" or "\ue909" =>
                    "M3,3 H10 V11 H3 Z M14,3 H21 V8 H14 Z M14,12 H21 V21 H14 Z M3,15 H10 V21 H3 Z M6,7 H7 M17,6 H18 M17,16 H18 M6,18 H7",

                // 2. Normal Optimization (Rocket / Turbo Core)
                "normal" or "\ue8b9" =>
                    "M12,2 L15,8 L21,9 L16.5,14 L18,20 L12,17 L6,20 L7.5,14 L3,9 L9,8 Z",

                // 3. Pro Optimization (Advanced Shield Hexagon)
                "pro" or "\ue730" =>
                    "M12,2 L20,6 V12 C20,17 16,20.5 12,22 C8,20.5 4,17 4,12 V6 Z M12,6 V18 M8,10 L12,13 L16,10",

                // 4. Ultimate Optimization (Crown Lightning Supercharger)
                "ultimate" or "\ue943" =>
                    "M4,18 L3,7 L8,11 L12,4 L16,11 L21,7 L20,18 Z M4,20 H20 M12,10 L10,14 H14 L12,18",

                // 5. Debloat (System Trimmer / Clean Matrix)
                "debloat" or "\ue74d" =>
                    "M4,4 L20,20 M20,4 L4,20 M12,2 V6 M12,18 V22 M2,12 H6 M18,12 H22 M8,8 H16 V16 H8 Z",

                // 6. BIOS Safe (Hardware Lock & Safe Microcontroller)
                "biossafe" or "bios_safe" or "bios" or "\ue9a1" =>
                    "M6,4 H18 V20 H6 Z M9,4 V2 M15,4 V2 M9,20 V22 M15,20 V22 M4,8 H2 M4,12 H2 M4,16 H2 M20,8 H22 M20,12 H22 M20,16 H22 M10,11 H14 V15 H10 Z",

                // 7. Max Performance (Turbo Tachometer / Speedometer)
                "maxperformance" or "max_performance" or "maxperf" or "\ue7a7" =>
                    "M4,18 A9,9 0 1,1 20,18 M12,13 L16,9 M12,11 A2,2 0 1,0 12,15 A2,2 0 1,0 12,11 M7,15 L9,13 M17,15 L15,13 M12,5 V7",

                // 8. AI Optimization (Neural Synapse / Neural Processor Unit)
                "aioptimization" or "ai_optimization" or "ai" or "\ue945" =>
                    "M12,2 A3,3 0 0,1 15,5 C15,6.5 13.5,7.5 12,8.5 C10.5,7.5 9,6.5 9,5 A3,3 0 0,1 12,2 Z M5,10 A3,3 0 0,1 8,13 C8,14.5 6.5,15.5 5,16.5 C3.5,15.5 2,14.5 2,13 A3,3 0 0,1 5,10 Z M19,10 A3,3 0 0,1 22,13 C22,14.5 20.5,15.5 19,16.5 C17.5,15.5 16,14.5 16,13 A3,3 0 0,1 19,10 Z M12,15 A3,3 0 0,1 15,18 C15,19.5 13.5,20.5 12,21.5 C10.5,20.5 9,19.5 9,18 A3,3 0 0,1 12,15 Z M12,8.5 V15 M8,13 L12,15 M16,13 L12,15 M12,8.5 L8,13 M12,8.5 L16,13",

                // 9. Process Intelligence / Reduction (CPU Task Intelligence Matrix)
                "processreduction" or "process_reduction" or "processes" or "process" or "\ue8bc" =>
                    "M4,4 H20 V20 H4 Z M4,9 H20 M9,9 V20 M14,13 H17 M14,16 H17 M6,13 H7 M6,16 H7",

                // 10. Input Optimizer (Low Latency Gaming Mouse & Keyboard Sensor)
                "inputoptimizer" or "input_optimizer" or "input" or "\ue926" =>
                    "M7,3 H17 C19.2,3 21,4.8 21,7 V17 C21,19.2 19.2,21 17,21 H7 C4.8,21 3,19.2 3,17 V7 C3,4.8 4.8,3 7,3 Z M12,3 V9 M8,7 H16 M7,13 H9 M11,13 H13 M15,13 H17 M7,16 H17",

                // 11. System Telemetry / Diagnostic Radar
                "systeminfo" or "system_info" or "telemetry" or "\ue9d9" =>
                    "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M12,6 A6,6 0 1,0 18,12 M12,12 L16,8 M12,2 V4 M12,20 V22 M2,12 H4 M20,12 H22",

                // 12. Registry Tweaks (Database Matrix & System Tuning)
                "registrytweak" or "registry_tweak" or "registry" or "\xec8f" =>
                    "M4,4 H20 V8 H4 Z M4,10 H20 V14 H4 Z M4,16 H20 V20 H4 Z M8,6 H9 M8,12 H9 M8,18 H9 M14,6 H17 M14,12 H17 M14,18 H17",

                // 13. Registry Values / RAM / GPU
                "registryvalues" or "registry_values" or "registryval" =>
                    "M3,5 H21 V19 H3 Z M3,10 H21 M8,5 V19 M12,14 L15,14 M12,16 L17,16",

                "registryvaluesram" or "ram" or "memory" =>
                    "M3,7 H21 V17 H3 Z M6,7 V5 M10,7 V5 M14,7 V5 M18,7 V5 M6,17 V19 M10,17 V19 M14,17 V19 M18,17 V19 M7,10 H10 V14 H7 Z M13,10 H17 V14 H13 Z",

                "gpuregistryvalues" or "gpu" or "graphics" =>
                    "M3,4 H21 V18 H3 Z M7,8 H17 V14 H7 Z M5,18 V20 M9,18 V20 M15,18 V20 M19,18 V20 M10,11 H14",

                // 14. Backup & Restore (Disaster Recovery Vault & Shield)
                "backuprestore" or "backup_restore" or "backup" or "backups" or "\ue777" =>
                    "M12,3 L20,7 V13 C20,17.5 16.5,20.5 12,22 C7.5,20.5 4,17.5 4,13 V7 Z M12,8 V14 M9,11 L12,8 L15,11 M8,16 H16",

                // 15. Settings / Preferences
                "settings" or "preferences" or "\ue713" =>
                    "M12,15 A3,3 0 1,0 12,9 A3,3 0 0,0 12,15 Z M19.4,13 C19.4,12.7 19.5,12.3 19.5,12 C19.5,11.7 19.4,11.3 19.4,11 L21.5,9.3 C21.7,9.1 21.8,8.8 21.6,8.6 L19.6,5.1 C19.5,4.9 19.2,4.8 19,4.9 L16.5,5.9 C16,5.5 15.4,5.2 14.8,5 L14.4,2.4 C14.4,2.2 14.2,2 13.9,2 H10 C9.8,2 9.6,2.2 9.5,2.4 L9.2,5 C8.6,5.2 8,5.5 7.5,5.9 L5,4.9 C4.8,4.8 4.5,4.9 4.4,5.1 L2.4,8.6 C2.2,8.8 2.3,9.1 2.5,9.3 L4.6,11 C4.6,11.3 4.5,11.7 4.5,12 C4.5,12.3 4.6,12.7 4.6,13 L2.5,14.7 C2.3,14.9 2.2,15.2 2.4,15.4 L4.4,18.9 C4.5,19.1 4.8,19.2 5,19.1 L7.5,18.1 C8,18.5 8.6,18.8 9.2,19 L9.5,21.6 C9.6,21.8 9.8,22 10,22 H14 C14.2,22 14.4,21.8 14.5,21.6 L14.8,19 C15.4,18.8 16,18.5 16.5,18.1 L19,19.1 C19.2,19.2 19.5,19.1 19.6,18.9 L21.6,15.4 C21.8,15.2 21.7,14.9 21.5,14.7 Z",

                // 16. Startup Manager
                "startupmanager" or "startup_manager" or "startup" or "\ue7b5" =>
                    "M12,2 L19,6 V18 L12,22 L5,18 V6 Z M12,6 V12 L16,14 M9,2 H15",

                // 17. Service Manager
                "servicemanager" or "service_manager" or "services" or "service" =>
                    "M4,6 H20 V10 H4 Z M4,14 H20 V18 H4 Z M8,8 H9 M8,16 H9 M14,8 H18 M14,16 H18 M2,4 V20 M22,4 V20",

                // 18. Network Optimizer
                "network" or "net" or "\ue839" =>
                    "M12,3 A9,9 0 1,0 21,12 A9,9 0 0,0 12,3 Z M3,12 H21 M12,3 C14.5,6 15.5,9 15.5,12 C15.5,15 14.5,18 12,21 C9.5,18 8.5,15 8.5,12 C8.5,9 9.5,6 12,3 Z",

                // 19. Storage Optimizer
                "storage" or "disk" or "nvme" or "\ue7f4" =>
                    "M4,4 H20 V10 H4 Z M4,14 H20 V20 H4 Z M6,7 H8 M6,17 H8 M16,7 H18 M16,17 H18 M12,7 H13 M12,17 H13",

                // 20. System Tools
                "tools" or "toolkit" or "toolbox" =>
                    "M14.7,6.3 L17.7,3.3 C18.1,2.9 18.7,2.9 19.1,3.3 L20.7,4.9 C21.1,5.3 21.1,5.9 20.7,6.3 L17.7,9.3 Z M13.3,7.7 L3.3,17.7 V20.7 H6.3 L16.3,10.7 Z M4.5,19.5 L5.5,18.5",

                // 21. About / Knowledge Center (Futuristic Neon Intelligence / Documentation Portal)
                "about" or "knowledge" or "knowledgecenter" or "info" or "\ue946" =>
                    "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M12,6 A1.5,1.5 0 1,1 12,9 A1.5,1.5 0 1,1 12,6 Z M10.5,11 H13.5 V17 H10.5 Z M9,17 H15 M9.5,11 H12",

                // Fallback Default
                _ => "M3,3 H10 V11 H3 Z M14,3 H21 V8 H14 Z M14,12 H21 V21 H14 Z M3,15 H10 V21 H3 Z"
            };
        }

        #endregion
    }
}
