#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.Diagnostics;

namespace BiosOptimizer.GUI.Services
{
    /// <summary>
    /// Centralized authoritative application settings service for Error Optimizer V3.
    /// Manages real application state with live propagation and JSON persistence.
    /// Persisted to %LOCALAPPDATA%\ErrorOptimizer\settings.json.
    /// </summary>
    public sealed class AppSettingsService : IAppSettingsService
    {
        private static readonly Lazy<AppSettingsService> _lazy =
            new(() => new AppSettingsService());

        public static AppSettingsService Instance => _lazy.Value;

        private readonly string _settingsFilePath;
        private readonly object _lock = new();
        private readonly System.Threading.Timer _saveDebounceTimer;

                // ── Visual / Theme / Material ─────────────────────────────────────
        private int _themeMode = 0; // 0=Solid, 1=Gradient
        private string _gradientStops = "[{\"Hex\":\"#00D4FF\",\"Offset\":0.0},{\"Hex\":\"#0051FF\",\"Offset\":0.5},{\"Hex\":\"#7A00FF\",\"Offset\":1.0}]";
        private double _gradientAngle = 90.0;
        private string _gradientDirection = "LeftToRight";
        private int _gradientAnimationMode = 1; // 0=Off, 1=Subtle, 2=Dynamic
        private string _siriFocalColorMode = "Auto"; // Auto, Color 1, Color 2, Color 3, Custom
        private string _siriFocalCustomColor = "#00D4FF";
        private string _accentColor = "#E53935";
        private double _windowGlassTransparency = 0.0; // 0.0=Opaque, 0.40=Max Translucent
        private double _cardOpacity = 0.90;            // 0.60=Max Translucent, 0.98=Solid
        private bool _animationsEnabled = true;
        private bool _transitionsEnabled = true;
        private bool _reducedMotion = false;
        private bool _glowEnabled = true;
        private bool _cursorEffect = true;
        private bool _cursorTrail = true;
        private bool _threeDEffect = true;
        private double _hueOffset = 0.0;

        // ── Motion / Visual Intensity (Flagship UI Motion System) ─────────
        private double _animationSpeed = 1.0;
        private double _glowIntensity = 0.70;
        private double _backgroundBlur = 30.0;
        private double _blurIntensity = 0.60;
        private int _particleDensity = 80;
        private int _particleIntensity = 2;
        private int _backgroundMode = 1; // 0=Static, 1=Subtle (default), 2=Dynamic
        private bool _hoverEffectsEnabled = true;
        private bool _backgroundMotionEnabled = true;
        private bool _cardDepthEnabled = true;
        private int _threeDDepthMode = 1; // 0=Low, 1=Medium (default), 2=High
        private double _threeDSceneIntensity = 0.75;

        // ── Cursor Proximity Card Glow System ─────────────────────────────
        private bool _cardProximityGlow = true;
        private double _proximityGlowStrength = 0.85;
        private double _proximityRange = 140.0;
        private bool _textProximityGlow = true;

        // ── Parallax Star Background Engine (WebDevSHORTS Parallax-Star-Background) ──
        private bool _parallaxBackground = true;
        private int _starDensity = 1; // 0=Low (100), 1=Medium (250), 2=High (450)
        private double _starOpacity = 0.65;
        private double _parallaxStrength = 0.60;
        private double _starSpeed = 1.0;
        private double _starGlow = 0.40;
        private int _starDepth = 1; // 0=Low, 1=Medium, 2=High
        private bool _starTwinkle = true;

        // ── Live Monitor / Performance ────────────────────────────────────
        private PerformanceProfileMode _performanceMode = PerformanceProfileMode.Auto;
        private int _liveMonitorInterval = 3;    // seconds (1, 2, 3, 5, 10)

        // ── Safety ───────────────────────────────────────────────────────
        private bool _confirmMediumRisk = false;
        private bool _confirmHighRisk = true;
        private bool _autoBackup = true;

        // ── Diagnostics ──────────────────────────────────────────────────
        private bool _detailedLogs = false;
        private int _logRetentionDays = 7;

        // ── Windows Startup & Background ──────────────────────────────────
        private bool _startWithWindows = false;
        private bool _startMinimized = false;

        // ── AI Engine Auto-Start ──────────────────────────────────────────
        private bool _aiPowerPlanAutoEnable = false;
        private bool _aiWorkloadAutoStart = true;
        private bool _aiRamLimiterAutoStart = true;
        private bool _smartAutoOptimizeAutoStart = true;

        public event Action<string, object, object>? SettingChanged;
        public event Action? SettingsChanged;

        public string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer");

        public string SettingsFilePath => _settingsFilePath;

                // ── Visual Properties ─────────────────────────────────────────────

        public int ThemeMode
        {
            get => _themeMode;
            set
            {
                if (_themeMode == value) return;
                var old = _themeMode;
                _themeMode = value;
                ThemeBrushService.Instance.SetThemeMode((ThemeMode)value);
                ScheduleSave();
                NotifyChanged(nameof(ThemeMode), old, value);
            }
        }

        public string GradientStops
        {
            get => _gradientStops;
            set
            {
                if (_gradientStops == value) return;
                var old = _gradientStops;
                _gradientStops = value;
                if (_themeMode == 1)
                {
                    var stops = ThemeBrushService.ParseGradientStopsJson(value);
                    ThemeBrushService.Instance.ApplyGradient(stops, _gradientAngle, _gradientDirection, _gradientAnimationMode);
                }
                ScheduleSave();
                NotifyChanged(nameof(GradientStops), old, value);
            }
        }

        public double GradientAngle
        {
            get => _gradientAngle;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 360.0);
                if (Math.Abs(_gradientAngle - clamped) < 0.01) return;
                var old = _gradientAngle;
                _gradientAngle = clamped;
                if (_themeMode == 1)
                {
                    var stops = ThemeBrushService.ParseGradientStopsJson(_gradientStops);
                    ThemeBrushService.Instance.ApplyGradient(stops, clamped, _gradientDirection, _gradientAnimationMode);
                }
                ScheduleSave();
                NotifyChanged(nameof(GradientAngle), old, clamped);
            }
        }

        public string GradientDirection
        {
            get => _gradientDirection;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _gradientDirection == value) return;
                var old = _gradientDirection;
                _gradientDirection = value;
                if (_themeMode == 1)
                {
                    var stops = ThemeBrushService.ParseGradientStopsJson(_gradientStops);
                    ThemeBrushService.Instance.ApplyGradient(stops, _gradientAngle, value, _gradientAnimationMode);
                }
                ScheduleSave();
                NotifyChanged(nameof(GradientDirection), old, value);
            }
        }

        public string SiriFocalColorMode
        {
            get => _siriFocalColorMode;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _siriFocalColorMode == value) return;
                var old = _siriFocalColorMode;
                _siriFocalColorMode = value;
                ScheduleSave();
                NotifyChanged(nameof(SiriFocalColorMode), old, value);
            }
        }

        public string SiriFocalCustomColor
        {
            get => _siriFocalCustomColor;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _siriFocalCustomColor == value) return;
                var old = _siriFocalCustomColor;
                _siriFocalCustomColor = value;
                ScheduleSave();
                NotifyChanged(nameof(SiriFocalCustomColor), old, value);
            }
        }

        public int GradientAnimationMode
        {
            get => _gradientAnimationMode;
            set
            {
                if (_gradientAnimationMode == value) return;
                var old = _gradientAnimationMode;
                _gradientAnimationMode = value;
                if (_themeMode == 1)
                {
                    var stops = ThemeBrushService.ParseGradientStopsJson(_gradientStops);
                    ThemeBrushService.Instance.ApplyGradient(stops, _gradientAngle, _gradientDirection, value);
                }
                ScheduleSave();
                NotifyChanged(nameof(GradientAnimationMode), old, value);
            }
        }

        public string AccentColor
        {
            get => _accentColor;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _accentColor == value) return;
                var old = _accentColor;
                _accentColor = value;
                ThemeBrushService.Instance.ApplyAccentColor(value);
                ScheduleSave();
                NotifyChanged(nameof(AccentColor), old, value);
            }
        }

        public double WindowGlassTransparency
        {
            get => _windowGlassTransparency;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_windowGlassTransparency - clamped) < 0.001) return;
                var old = _windowGlassTransparency;
                _windowGlassTransparency = clamped;
                ThemeBrushService.Instance.ApplyWindowGlassTransparency(clamped);
                ScheduleSave();
                NotifyChanged(nameof(WindowGlassTransparency), old, clamped);
            }
        }

        public double CardOpacity
        {
            get => _cardOpacity;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_cardOpacity - clamped) < 0.001) return;
                var old = _cardOpacity;
                _cardOpacity = clamped;
                ThemeBrushService.Instance.ApplyOpacity(clamped);
                ScheduleSave();
                NotifyChanged(nameof(CardOpacity), old, clamped);
            }
        }

        public bool AnimationsEnabled
        {
            get => _animationsEnabled;
            set
            {
                if (_animationsEnabled == value) return;
                var old = _animationsEnabled;
                _animationsEnabled = value;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(AnimationsEnabled), old, value);
            }
        }

        public bool TransitionsEnabled
        {
            get => _transitionsEnabled;
            set
            {
                if (_transitionsEnabled == value) return;
                var old = _transitionsEnabled;
                _transitionsEnabled = value;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(TransitionsEnabled), old, value);
            }
        }

        public bool ReducedMotion
        {
            get => _reducedMotion;
            set
            {
                if (_reducedMotion == value) return;
                var old = _reducedMotion;
                _reducedMotion = value;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(ReducedMotion), old, value);
            }
        }

        public bool GlowEnabled
        {
            get => _glowEnabled;
            set
            {
                if (_glowEnabled == value) return;
                var old = _glowEnabled;
                _glowEnabled = value;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(GlowEnabled), old, value);
            }
        }

                private double _cursorHaloSize = 1.0;
        private double _cursorGlowIntensity = 0.85;

        public double CursorHaloSize
        {
            get => _cursorHaloSize;
            set
            {
                var clamped = Math.Clamp(value, 0.1, 2.5);
                if (Math.Abs(_cursorHaloSize - clamped) > 0.001)
                {
                    var old = _cursorHaloSize;
                    _cursorHaloSize = clamped;
                    ScheduleSave();
                    NotifyChanged(nameof(CursorHaloSize), old, clamped);
                }
            }
        }

        public double CursorGlowIntensity
        {
            get => _cursorGlowIntensity;
            set
            {
                var clamped = Math.Clamp(value, 0.1, 2.0);
                if (Math.Abs(_cursorGlowIntensity - clamped) > 0.001)
                {
                    var old = _cursorGlowIntensity;
                    _cursorGlowIntensity = clamped;
                    NotifyChanged(nameof(CursorGlowIntensity), old, clamped);
                }
            }
        }

        public bool CursorEffect
        {
            get => _cursorEffect;
            set
            {
                if (_cursorEffect == value) return;
                var old = _cursorEffect;
                _cursorEffect = value;
                ScheduleSave();
                NotifyChanged(nameof(CursorEffect), old, value);
            }
        }

        public bool CursorTrail
        {
            get => _cursorTrail;
            set
            {
                if (_cursorTrail == value) return;
                var old = _cursorTrail;
                _cursorTrail = value;
                ScheduleSave();
                NotifyChanged(nameof(CursorTrail), old, value);
            }
        }

        public bool ThreeDEffect
        {
            get => _threeDEffect;
            set
            {
                if (_threeDEffect == value) return;
                var old = _threeDEffect;
                _threeDEffect = value;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(ThreeDEffect), old, value);
            }
        }

        public double HueOffset
        {
            get => _hueOffset;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 360.0);
                if (Math.Abs(_hueOffset - clamped) < 0.01) return;
                var old = _hueOffset;
                _hueOffset = clamped;
                ScheduleSave();
                NotifyChanged(nameof(HueOffset), old, clamped);
            }
        }

        public double AnimationSpeed
        {
            get => _animationSpeed;
            set
            {
                double clamped = Math.Clamp(value, 0.25, 3.0);
                if (Math.Abs(_animationSpeed - clamped) < 0.01) return;
                var old = _animationSpeed;
                _animationSpeed = clamped;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(AnimationSpeed), old, clamped);
            }
        }

        public double GlowIntensity
        {
            get => _glowIntensity;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 2.0);
                if (Math.Abs(_glowIntensity - clamped) < 0.01) return;
                var old = _glowIntensity;
                _glowIntensity = clamped;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(GlowIntensity), old, clamped);
            }
        }

        public double BackgroundBlur
        {
            get => _backgroundBlur;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 60.0);
                if (Math.Abs(_backgroundBlur - clamped) < 0.1) return;
                var old = _backgroundBlur;
                _backgroundBlur = clamped;
                _blurIntensity = clamped / 60.0;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(BackgroundBlur), old, clamped);
                NotifyChanged(nameof(BlurIntensity), old / 60.0, _blurIntensity);
            }
        }

        public double BlurIntensity
        {
            get => _blurIntensity;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_blurIntensity - clamped) < 0.01) return;
                var old = _blurIntensity;
                _blurIntensity = clamped;
                _backgroundBlur = clamped * 60.0;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(BlurIntensity), old, clamped);
                NotifyChanged(nameof(BackgroundBlur), old * 60.0, _backgroundBlur);
            }
        }

        public int ParticleDensity
        {
            get => _particleDensity;
            set
            {
                int clamped = Math.Clamp(value, 0, 200);
                if (_particleDensity == clamped) return;
                var old = _particleDensity;
                _particleDensity = clamped;
                _particleIntensity = clamped switch
                {
                    <= 0 => 0,
                    <= 40 => 1,
                    <= 100 => 2,
                    _ => 3
                };
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(ParticleDensity), old, clamped);
                NotifyChanged(nameof(ParticleIntensity), old, _particleIntensity);
            }
        }

        public int ParticleIntensity
        {
            get => _particleIntensity;
            set
            {
                int clamped = Math.Clamp(value, 0, 3);
                if (_particleIntensity == clamped) return;
                var old = _particleIntensity;
                _particleIntensity = clamped;
                _particleDensity = clamped switch
                {
                    0 => 0,
                    1 => 30,
                    2 => 80,
                    _ => 150
                };
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(ParticleIntensity), old, clamped);
                NotifyChanged(nameof(ParticleDensity), old, _particleDensity);
            }
        }

        public int BackgroundMode
        {
            get => _backgroundMode;
            set
            {
                int clamped = Math.Clamp(value, 0, 2);
                if (_backgroundMode == clamped) return;
                var old = _backgroundMode;
                _backgroundMode = clamped;
                _backgroundMotionEnabled = clamped != 0;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(BackgroundMode), old, clamped);
                NotifyChanged(nameof(BackgroundMotionEnabled), old != 0, _backgroundMotionEnabled);
            }
        }

        public bool HoverEffectsEnabled
        {
            get => _hoverEffectsEnabled;
            set
            {
                if (_hoverEffectsEnabled == value) return;
                var old = _hoverEffectsEnabled;
                _hoverEffectsEnabled = value;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(HoverEffectsEnabled), old, value);
            }
        }

        public bool BackgroundMotionEnabled
        {
            get => _backgroundMotionEnabled;
            set
            {
                if (_backgroundMotionEnabled == value) return;
                var old = _backgroundMotionEnabled;
                _backgroundMotionEnabled = value;
                _backgroundMode = value ? 1 : 0;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(BackgroundMotionEnabled), old, value);
                NotifyChanged(nameof(BackgroundMode), old ? 1 : 0, _backgroundMode);
            }
        }

        public bool CardDepthEnabled
        {
            get => _cardDepthEnabled;
            set
            {
                if (_cardDepthEnabled == value) return;
                var old = _cardDepthEnabled;
                _cardDepthEnabled = value;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(CardDepthEnabled), old, value);
            }
        }

        public int ThreeDDepthMode
        {
            get => _threeDDepthMode;
            set
            {
                int clamped = Math.Clamp(value, 0, 2);
                if (_threeDDepthMode == clamped) return;
                var old = _threeDDepthMode;
                _threeDDepthMode = clamped;
                _cardDepthEnabled = clamped != 0;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(ThreeDDepthMode), old, clamped);
            }
        }

        public double ThreeDSceneIntensity
        {
            get => _threeDSceneIntensity;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_threeDSceneIntensity - clamped) < 0.01) return;
                var old = _threeDSceneIntensity;
                _threeDSceneIntensity = clamped;
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                ScheduleSave();
                NotifyChanged(nameof(ThreeDSceneIntensity), old, clamped);
            }
        }

        // ── Cursor Proximity Card Glow Properties ─────────────────────────

        public bool CardProximityGlow
        {
            get => _cardProximityGlow;
            set
            {
                if (_cardProximityGlow == value) return;
                var old = _cardProximityGlow;
                _cardProximityGlow = value;
                ScheduleSave();
                NotifyChanged(nameof(CardProximityGlow), old, value);
            }
        }

        public double ProximityGlowStrength
        {
            get => _proximityGlowStrength;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_proximityGlowStrength - clamped) < 0.01) return;
                var old = _proximityGlowStrength;
                _proximityGlowStrength = clamped;
                ScheduleSave();
                NotifyChanged(nameof(ProximityGlowStrength), old, clamped);
            }
        }

        public double ProximityRange
        {
            get => _proximityRange;
            set
            {
                double clamped = Math.Clamp(value, 40.0, 300.0);
                if (Math.Abs(_proximityRange - clamped) < 0.1) return;
                var old = _proximityRange;
                _proximityRange = clamped;
                ScheduleSave();
                NotifyChanged(nameof(ProximityRange), old, clamped);
            }
        }

        public bool TextProximityGlow
        {
            get => _textProximityGlow;
            set
            {
                if (_textProximityGlow == value) return;
                var old = _textProximityGlow;
                _textProximityGlow = value;
                ScheduleSave();
                NotifyChanged(nameof(TextProximityGlow), old, value);
            }
        }

        // ── Parallax Star Background Properties ───────────────────────────

        public bool ParallaxBackground
        {
            get => _parallaxBackground;
            set
            {
                if (_parallaxBackground == value) return;
                var old = _parallaxBackground;
                _parallaxBackground = value;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(ParallaxBackground), old, value);
            }
        }

        public int StarDensity
        {
            get => _starDensity;
            set
            {
                int clamped = Math.Clamp(value, 0, 2);
                if (_starDensity == clamped) return;
                var old = _starDensity;
                _starDensity = clamped;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(StarDensity), old, clamped);
            }
        }

        public double StarOpacity
        {
            get => _starOpacity;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_starOpacity - clamped) < 0.01) return;
                var old = _starOpacity;
                _starOpacity = clamped;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(StarOpacity), old, clamped);
            }
        }

        public double ParallaxStrength
        {
            get => _parallaxStrength;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_parallaxStrength - clamped) < 0.01) return;
                var old = _parallaxStrength;
                _parallaxStrength = clamped;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(ParallaxStrength), old, clamped);
            }
        }

        public double StarSpeed
        {
            get => _starSpeed;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 2.0);
                if (Math.Abs(_starSpeed - clamped) < 0.01) return;
                var old = _starSpeed;
                _starSpeed = clamped;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(StarSpeed), old, clamped);
            }
        }

        public double StarGlow
        {
            get => _starGlow;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_starGlow - clamped) < 0.01) return;
                var old = _starGlow;
                _starGlow = clamped;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(StarGlow), old, clamped);
            }
        }

        public int StarDepth
        {
            get => _starDepth;
            set
            {
                int clamped = Math.Clamp(value, 0, 2);
                if (_starDepth == clamped) return;
                var old = _starDepth;
                _starDepth = clamped;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(StarDepth), old, clamped);
            }
        }

        public bool StarTwinkle
        {
            get => _starTwinkle;
            set
            {
                if (_starTwinkle == value) return;
                var old = _starTwinkle;
                _starTwinkle = value;
                ScheduleSave();
                UI3DMotionEngine.Instance.NotifySettingsChanged();
                NotifyChanged(nameof(StarTwinkle), old, value);
            }
        }

        // ── Performance & Safety Properties ───────────────────────────────

        public PerformanceProfileMode PerformanceMode
        {
            get => _performanceMode;
            set
            {
                if (_performanceMode == value) return;
                var old = _performanceMode;
                _performanceMode = value;
                AdaptiveResourceGovernor.Instance.SetPerformanceMode(value);
                ScheduleSave();
                NotifyChanged(nameof(PerformanceMode), old, value);
            }
        }

        public int LiveMonitorInterval
        {
            get => _liveMonitorInterval;
            set
            {
                int clamped = Math.Clamp(value, 1, 30);
                if (_liveMonitorInterval == clamped) return;
                var old = _liveMonitorInterval;
                _liveMonitorInterval = clamped;
                AdaptiveResourceGovernor.Instance.UserConfiguredIntervalMs = _liveMonitorInterval * 1000;
                ScheduleSave();
                NotifyChanged(nameof(LiveMonitorInterval), old, clamped);
            }
        }

        public bool ConfirmMediumRisk
        {
            get => _confirmMediumRisk;
            set
            {
                if (_confirmMediumRisk == value) return;
                var old = _confirmMediumRisk;
                _confirmMediumRisk = value;
                ScheduleSave();
                NotifyChanged(nameof(ConfirmMediumRisk), old, value);
            }
        }

        public bool ConfirmHighRisk
        {
            get => _confirmHighRisk;
            set
            {
                if (_confirmHighRisk == value) return;
                var old = _confirmHighRisk;
                _confirmHighRisk = value;
                ScheduleSave();
                NotifyChanged(nameof(ConfirmHighRisk), old, value);
            }
        }

        public bool AutoBackup
        {
            get => _autoBackup;
            set
            {
                if (_autoBackup == value) return;
                var old = _autoBackup;
                _autoBackup = value;
                ScheduleSave();
                NotifyChanged(nameof(AutoBackup), old, value);
            }
        }

        public bool DetailedLogs
        {
            get => _detailedLogs;
            set
            {
                if (_detailedLogs == value) return;
                var old = _detailedLogs;
                _detailedLogs = value;
                // Set verbose logging
                ScheduleSave();
                NotifyChanged(nameof(DetailedLogs), old, value);
            }
        }

        public int LogRetentionDays
        {
            get => _logRetentionDays;
            set
            {
                int clamped = Math.Clamp(value, 1, 90);
                if (_logRetentionDays == clamped) return;
                var old = _logRetentionDays;
                _logRetentionDays = clamped;
                ScheduleSave();
                PruneOldLogs();
                NotifyChanged(nameof(LogRetentionDays), old, clamped);
            }
        }

                public void SynchronizeWithRealWindowsStartup()
        {
            try
            {
                var startupInfo = BiosOptimizer.Core.Implementations.Startup.WindowsStartupRegistrar.Instance.GetStartupInfo();
                bool realEnabled = startupInfo.IsEnabled && startupInfo.Status == BiosOptimizer.Core.Interfaces.WindowsStartupRegistrationStatus.VerifiedEnabled;
                bool realMinimized = startupInfo.Arguments.Contains("--startup-background", StringComparison.OrdinalIgnoreCase) || 
                                     startupInfo.Arguments.Contains("--minimized", StringComparison.OrdinalIgnoreCase);

                if (_startWithWindows != realEnabled)
                {
                    var old = _startWithWindows;
                    _startWithWindows = realEnabled;
                    NotifyChanged(nameof(StartWithWindows), old, realEnabled);
                }

                if (realEnabled && _startMinimized != realMinimized)
                {
                    var oldMin = _startMinimized;
                    _startMinimized = realMinimized;
                    NotifyChanged(nameof(StartMinimized), oldMin, realMinimized);
                }
            }
            catch { }
        }

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (_startWithWindows == value) return;
                var old = _startWithWindows;
                _startWithWindows = value;
                BiosOptimizer.Core.Implementations.Startup.WindowsStartupRegistrar.Instance.ConfigureStartup(value, _startMinimized);
                ScheduleSave();
                NotifyChanged(nameof(StartWithWindows), old, value);
            }
        }

        public bool StartMinimized
        {
            get => _startMinimized;
            set
            {
                if (_startMinimized == value) return;
                var old = _startMinimized;
                _startMinimized = value;
                if (_startWithWindows)
                {
                    BiosOptimizer.Core.Implementations.Startup.WindowsStartupRegistrar.Instance.ConfigureStartup(true, value);
                }
                ScheduleSave();
                NotifyChanged(nameof(StartMinimized), old, value);
            }
        }

        public bool AiPowerPlanAutoEnable
        {
            get => _aiPowerPlanAutoEnable;
            set
            {
                if (_aiPowerPlanAutoEnable == value) return;
                var old = _aiPowerPlanAutoEnable;
                _aiPowerPlanAutoEnable = value;
                try
                {
                    var pConfig = BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.GetStartupConfig();
                    pConfig.AutoEnableOnStartup = value;
                    if (value && string.IsNullOrWhiteSpace(pConfig.StartupPowerPlanGuid))
                    {
                        var (curGuid, curName) = BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.GetActiveSchemeNative();
                        pConfig.StartupPowerPlanGuid = curGuid.ToString();
                        pConfig.StartupPowerPlanName = curName;
                    }
                    BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.SaveStartupConfig(pConfig);
                    if (value)
                    {
                        BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.StartBackgroundEnforcement();
                    }
                    else
                    {
                        BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.StopBackgroundEnforcement();
                    }
                }
                catch { }
                ScheduleSave();
                NotifyChanged(nameof(AiPowerPlanAutoEnable), old, value);
            }
        }

        public bool AiWorkloadAutoStart
        {
            get => _aiWorkloadAutoStart;
            set
            {
                if (_aiWorkloadAutoStart == value) return;
                var old = _aiWorkloadAutoStart;
                _aiWorkloadAutoStart = value;
                try
                {
                    if (value)
                        BiosOptimizer.Core.Implementations.WorkloadOptimizationEngine.Instance.Start();
                    else
                        BiosOptimizer.Core.Implementations.WorkloadOptimizationEngine.Instance.Stop();
                }
                catch { }
                ScheduleSave();
                NotifyChanged(nameof(AiWorkloadAutoStart), old, value);
            }
        }

        public bool AiRamLimiterAutoStart
        {
            get => _aiRamLimiterAutoStart;
            set
            {
                if (_aiRamLimiterAutoStart == value) return;
                var old = _aiRamLimiterAutoStart;
                _aiRamLimiterAutoStart = value;
                try
                {
                    if (value)
                        BiosOptimizer.Core.Implementations.RamLimiterEngine.Instance.Start();
                    else
                        BiosOptimizer.Core.Implementations.RamLimiterEngine.Instance.Stop();
                }
                catch { }
                ScheduleSave();
                NotifyChanged(nameof(AiRamLimiterAutoStart), old, value);
            }
        }

        public bool SmartAutoOptimizeAutoStart
        {
            get => _smartAutoOptimizeAutoStart;
            set
            {
                if (_smartAutoOptimizeAutoStart == value) return;
                var old = _smartAutoOptimizeAutoStart;
                _smartAutoOptimizeAutoStart = value;
                ScheduleSave();
                NotifyChanged(nameof(SmartAutoOptimizeAutoStart), old, value);
            }
        }

        public AppSettingsService()
        {
            _settingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
            _saveDebounceTimer = new System.Threading.Timer(_ => SaveInternal(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            Load();
        }

        public void ResetToDefaults()
        {
            _accentColor = "#E53935";
            _windowGlassTransparency = 0.0;
            _cardOpacity = 0.90;
            _animationsEnabled = true;
            _transitionsEnabled = true;
            _reducedMotion = false;
            _glowEnabled = true;
            _cursorEffect = true;
            _cursorTrail = true;
            _threeDEffect = true;
            _hueOffset = 0.0;

            _animationSpeed = 1.0;
            _glowIntensity = 0.70;
            _backgroundBlur = 30.0;
            _blurIntensity = 0.60;
            _particleDensity = 80;
            _particleIntensity = 2;
            _backgroundMode = 1;
            _hoverEffectsEnabled = true;
            _backgroundMotionEnabled = true;
            _cardDepthEnabled = true;
            _threeDDepthMode = 1;
            _threeDSceneIntensity = 0.75;
            _cardProximityGlow = true;
            _proximityGlowStrength = 0.85;
            _proximityRange = 140.0;
            _textProximityGlow = true;
            _parallaxBackground = true;
            _starDensity = 1;
            _starOpacity = 0.65;
            _parallaxStrength = 0.60;
            _starSpeed = 1.0;
            _starGlow = 0.40;
            _starDepth = 1;
            _starTwinkle = true;

            _performanceMode = PerformanceProfileMode.Auto;
            _liveMonitorInterval = 3;
            _confirmMediumRisk = false;
            _confirmHighRisk = true;
            _autoBackup = true;
            _detailedLogs = false;
            _logRetentionDays = 7;
            _startWithWindows = false;
            _startMinimized = false;
            _aiPowerPlanAutoEnable = false;
            _aiWorkloadAutoStart = true;
            _aiRamLimiterAutoStart = true;
            _smartAutoOptimizeAutoStart = true;

            SynchronizeWithRealWindowsStartup();
            ThemeBrushService.Instance.ApplyAccentColor(_accentColor);
            ThemeBrushService.Instance.ApplyWindowGlassTransparency(_windowGlassTransparency);
            ThemeBrushService.Instance.ApplyOpacity(_cardOpacity);
            UI3DMotionEngine.Instance.NotifySettingsChanged();
            Save();
            SettingsChanged?.Invoke();
        }

        public void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        string json = File.ReadAllText(_settingsFilePath);
                        var dto = JsonSerializer.Deserialize<SettingsDto>(json);
                        if (dto != null)
                        {
                            _accentColor = !string.IsNullOrWhiteSpace(dto.AccentColor) ? dto.AccentColor : "#E53935";
                            _windowGlassTransparency = Math.Clamp(dto.WindowGlassTransparency, 0.0, 0.40);
                            _cardOpacity = Math.Clamp(dto.CardOpacity > 0 ? dto.CardOpacity : 0.90, 0.60, 0.98);
                            _animationsEnabled = dto.AnimationsEnabled;
                            _transitionsEnabled = dto.TransitionsEnabled;
                            _reducedMotion = dto.ReducedMotion;
                            _glowEnabled = dto.GlowEnabled;
                            _cursorEffect = dto.CursorEffect;
                            _cursorTrail = dto.CursorTrail;
                            _threeDEffect = dto.ThreeDEffect;
                            _hueOffset = Math.Clamp(dto.HueOffset, 0.0, 360.0);

                            _animationSpeed = Math.Clamp(dto.AnimationSpeed > 0 ? dto.AnimationSpeed : 1.0, 0.25, 3.0);
                            _glowIntensity = Math.Clamp(dto.GlowIntensity >= 0 ? dto.GlowIntensity : 0.70, 0.0, 2.0);
                            _backgroundBlur = Math.Clamp(dto.BackgroundBlur >= 0 ? dto.BackgroundBlur : 30.0, 0.0, 60.0);
                            _blurIntensity = Math.Clamp(dto.BlurIntensity >= 0 ? dto.BlurIntensity : 0.60, 0.0, 1.0);
                            _particleDensity = Math.Clamp(dto.ParticleDensity >= 0 ? dto.ParticleDensity : 80, 0, 200);
                            _particleIntensity = Math.Clamp(dto.ParticleIntensity >= 0 ? dto.ParticleIntensity : 2, 0, 3);
                            _backgroundMode = Math.Clamp(dto.BackgroundMode >= 0 ? dto.BackgroundMode : 1, 0, 2);
                            _hoverEffectsEnabled = dto.HoverEffectsEnabled;
                            _backgroundMotionEnabled = dto.BackgroundMotionEnabled;
                            _cardDepthEnabled = dto.CardDepthEnabled;
                            _threeDDepthMode = Math.Clamp(dto.ThreeDDepthMode >= 0 ? dto.ThreeDDepthMode : 1, 0, 2);
                            _threeDSceneIntensity = Math.Clamp(dto.ThreeDSceneIntensity >= 0 ? dto.ThreeDSceneIntensity : 0.75, 0.0, 1.0);
                            _cardProximityGlow = dto.CardProximityGlow;
                            _proximityGlowStrength = Math.Clamp(dto.ProximityGlowStrength > 0 ? dto.ProximityGlowStrength : 0.85, 0.0, 1.0);
                            _proximityRange = Math.Clamp(dto.ProximityRange > 0 ? dto.ProximityRange : 140.0, 40.0, 300.0);
                            _textProximityGlow = dto.TextProximityGlow;
                            _parallaxBackground = dto.ParallaxBackground;
                            _starDensity = Math.Clamp(dto.StarDensity >= 0 ? dto.StarDensity : 1, 0, 2);
                            _starOpacity = Math.Clamp(dto.StarOpacity >= 0 ? dto.StarOpacity : 0.65, 0.0, 1.0);
                            _parallaxStrength = Math.Clamp(dto.ParallaxStrength >= 0 ? dto.ParallaxStrength : 0.60, 0.0, 1.0);
                            _starSpeed = Math.Clamp(dto.StarSpeed >= 0 ? dto.StarSpeed : 1.0, 0.0, 2.0);
                            _starGlow = Math.Clamp(dto.StarGlow >= 0 ? dto.StarGlow : 0.40, 0.0, 1.0);
                            _starDepth = Math.Clamp(dto.StarDepth >= 0 ? dto.StarDepth : 1, 0, 2);
                            _starTwinkle = dto.StarTwinkle;

                            _performanceMode = dto.PerformanceMode;
                            _liveMonitorInterval = Math.Clamp(dto.LiveMonitorInterval > 0 ? dto.LiveMonitorInterval : 3, 1, 30);
                            _confirmMediumRisk = dto.ConfirmMediumRisk;
                            _confirmHighRisk = dto.ConfirmHighRisk;
                            _autoBackup = dto.AutoBackup;
                            _detailedLogs = dto.DetailedLogs;
                            _logRetentionDays = Math.Clamp(dto.LogRetentionDays > 0 ? dto.LogRetentionDays : 7, 1, 90);
                            _startWithWindows = dto.StartWithWindows;
                            _startMinimized = dto.StartMinimized;
                            _aiPowerPlanAutoEnable = dto.AiPowerPlanAutoEnable;
                            _aiWorkloadAutoStart = dto.AiWorkloadAutoStart;
                            _aiRamLimiterAutoStart = dto.AiRamLimiterAutoStart;
                            _smartAutoOptimizeAutoStart = dto.SmartAutoOptimizeAutoStart;
                            _themeMode = dto.ThemeMode;
                            if (!string.IsNullOrWhiteSpace(dto.GradientStops)) _gradientStops = dto.GradientStops;
                            _gradientAngle = dto.GradientAngle;
                            if (!string.IsNullOrWhiteSpace(dto.GradientDirection)) _gradientDirection = dto.GradientDirection;
                            _gradientAnimationMode = dto.GradientAnimationMode;
                            if (!string.IsNullOrWhiteSpace(dto.SiriFocalColorMode)) _siriFocalColorMode = dto.SiriFocalColorMode;
                            if (!string.IsNullOrWhiteSpace(dto.SiriFocalCustomColor)) _siriFocalCustomColor = dto.SiriFocalCustomColor;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error
                }
            }

            ThemeBrushService.Instance.ApplyAccentColor(_accentColor);
            ThemeBrushService.Instance.ApplyWindowGlassTransparency(_windowGlassTransparency);
            ThemeBrushService.Instance.ApplyOpacity(_cardOpacity);
            UI3DMotionEngine.Instance.NotifySettingsChanged();
            SettingsChanged?.Invoke();
        }

        public void ScheduleSave()
        {
            _saveDebounceTimer.Change(250, System.Threading.Timeout.Infinite);
        }

        private void SaveInternal()
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(SettingsDirectory)) Directory.CreateDirectory(SettingsDirectory);
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(this, options);
                    File.WriteAllText(_settingsFilePath, json);
                }
                catch { }
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(SettingsDirectory))
                    {
                        Directory.CreateDirectory(SettingsDirectory);
                    }

                    var dto = new SettingsDto
                    {
                        AccentColor = _accentColor,
                        WindowGlassTransparency = _windowGlassTransparency,
                        CardOpacity = _cardOpacity,
                        AnimationsEnabled = _animationsEnabled,
                        TransitionsEnabled = _transitionsEnabled,
                        ReducedMotion = _reducedMotion,
                        GlowEnabled = _glowEnabled,
                        CursorEffect = _cursorEffect,
                        CursorTrail = _cursorTrail,
                        ThreeDEffect = _threeDEffect,
                        HueOffset = _hueOffset,
                        AnimationSpeed = _animationSpeed,
                        GlowIntensity = _glowIntensity,
                        BackgroundBlur = _backgroundBlur,
                        BlurIntensity = _blurIntensity,
                        ParticleDensity = _particleDensity,
                        ParticleIntensity = _particleIntensity,
                        BackgroundMode = _backgroundMode,
                        HoverEffectsEnabled = _hoverEffectsEnabled,
                        BackgroundMotionEnabled = _backgroundMotionEnabled,
                        CardDepthEnabled = _cardDepthEnabled,
                        ThreeDDepthMode = _threeDDepthMode,
                        ThreeDSceneIntensity = _threeDSceneIntensity,
                        CardProximityGlow = _cardProximityGlow,
                        ProximityGlowStrength = _proximityGlowStrength,
                        ProximityRange = _proximityRange,
                        TextProximityGlow = _textProximityGlow,
                        ParallaxBackground = _parallaxBackground,
                        StarDensity = _starDensity,
                        StarOpacity = _starOpacity,
                        ParallaxStrength = _parallaxStrength,
                        StarSpeed = _starSpeed,
                        StarGlow = _starGlow,
                        StarDepth = _starDepth,
                        StarTwinkle = _starTwinkle,
                        PerformanceMode = _performanceMode,
                        LiveMonitorInterval = _liveMonitorInterval,
                        ConfirmMediumRisk = _confirmMediumRisk,
                        ConfirmHighRisk = _confirmHighRisk,
                        AutoBackup = _autoBackup,
                        DetailedLogs = _detailedLogs,
                        LogRetentionDays = _logRetentionDays,
                        StartWithWindows = _startWithWindows,
                        StartMinimized = _startMinimized,
                        AiPowerPlanAutoEnable = _aiPowerPlanAutoEnable,
                        AiWorkloadAutoStart = _aiWorkloadAutoStart,
                        AiRamLimiterAutoStart = _aiRamLimiterAutoStart,
                        SmartAutoOptimizeAutoStart = _smartAutoOptimizeAutoStart,
                        ThemeMode = _themeMode,
                        GradientStops = _gradientStops,
                        GradientAngle = _gradientAngle,
                        GradientDirection = _gradientDirection,
                        GradientAnimationMode = _gradientAnimationMode,
                        SiriFocalColorMode = _siriFocalColorMode,
                        SiriFocalCustomColor = _siriFocalCustomColor
                    };

                    string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_settingsFilePath, json);
                }
                catch (Exception ex)
                {
                    // Log error
                }
            }
        }

        private void NotifyChanged(string propertyName, object oldValue, object newValue)
        {
            SettingChanged?.Invoke(propertyName, oldValue, newValue);
            SettingsChanged?.Invoke();
        }

        private void PruneOldLogs()
        {
            try
            {
                string logDir = Path.Combine(SettingsDirectory, "Logs");
                if (Directory.Exists(logDir))
                {
                    var cutoff = DateTime.Now.AddDays(-_logRetentionDays);
                    foreach (var file in Directory.GetFiles(logDir, "*.log"))
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTime < cutoff)
                        {
                            fi.Delete();
                        }
                    }
                }
            }
            catch { }
        }

        private sealed class SettingsDto
        {
            public string AccentColor { get; set; } = "#E53935";
            public double WindowGlassTransparency { get; set; } = 0.0;
            public double CardOpacity { get; set; } = 0.90;
            public bool AnimationsEnabled { get; set; } = true;
            public bool TransitionsEnabled { get; set; } = true;
            public bool ReducedMotion { get; set; } = false;
            public bool GlowEnabled { get; set; } = true;
                    public bool CursorEffect { get; set; } = true;
        public double CursorHaloSize { get; set; } = 1.0;
        public double CursorGlowIntensity { get; set; } = 0.85;
            public bool CursorTrail { get; set; } = true;
            public bool ThreeDEffect { get; set; } = true;
            public double HueOffset { get; set; } = 0.0;
            public PerformanceProfileMode PerformanceMode { get; set; } = PerformanceProfileMode.Auto;
            public int LiveMonitorInterval { get; set; } = 3;
            public bool ConfirmMediumRisk { get; set; } = false;
            public bool ConfirmHighRisk { get; set; } = true;
            public bool AutoBackup { get; set; } = true;
            public bool DetailedLogs { get; set; } = false;
            public int LogRetentionDays { get; set; } = 7;
            public bool StartWithWindows { get; set; } = false;
            public bool StartMinimized { get; set; } = false;
            public bool AiPowerPlanAutoEnable { get; set; } = false;
            public bool AiWorkloadAutoStart { get; set; } = true;
            public bool AiRamLimiterAutoStart { get; set; } = true;
            public bool SmartAutoOptimizeAutoStart { get; set; } = true;
            public double AnimationSpeed { get; set; } = 1.0;
            public double GlowIntensity { get; set; } = 0.70;
            public double BackgroundBlur { get; set; } = 30.0;
            public double BlurIntensity { get; set; } = 0.60;
            public int ParticleDensity { get; set; } = 80;
            public int ParticleIntensity { get; set; } = 2;
            public int BackgroundMode { get; set; } = 1;
            public bool HoverEffectsEnabled { get; set; } = true;
            public bool BackgroundMotionEnabled { get; set; } = true;
            public bool CardDepthEnabled { get; set; } = true;
            public int ThreeDDepthMode { get; set; } = 1;
            public double ThreeDSceneIntensity { get; set; } = 0.75;
            public bool CardProximityGlow { get; set; } = true;
            public double ProximityGlowStrength { get; set; } = 0.85;
            public double ProximityRange { get; set; } = 140.0;
            public bool TextProximityGlow { get; set; } = true;
            public bool ParallaxBackground { get; set; } = true;
            public int StarDensity { get; set; } = 1;
            public double StarOpacity { get; set; } = 0.65;
            public double ParallaxStrength { get; set; } = 0.60;
            public double StarSpeed { get; set; } = 1.0;
            public double StarGlow { get; set; } = 0.40;
            public int StarDepth { get; set; } = 1;
            public bool StarTwinkle { get; set; } = true;
            public int ThemeMode { get; set; } = 0;
            public string GradientStops { get; set; } = "";
            public double GradientAngle { get; set; } = 90.0;
            public string GradientDirection { get; set; } = "LeftToRight";
            public int GradientAnimationMode { get; set; } = 1;
            public string SiriFocalColorMode { get; set; } = "Auto";
            public string SiriFocalCustomColor { get; set; } = "#00D4FF";
        }
    }
}