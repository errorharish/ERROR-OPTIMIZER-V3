#nullable enable
using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace BiosOptimizer.GUI.Services
{
    /// <summary>
    /// Central 3D Spatial Motion Authority for Error Optimizer V3.
    /// Governs perspective projection, camera parallax, 3D card tilt, Z-depth elevation,
    /// dynamic spotlight tracking, 3D floating geometry, and spatial transitions.
    /// </summary>
    public sealed class UI3DMotionEngine
    {
        private static readonly Lazy<UI3DMotionEngine> _instance =
            new(() => new UI3DMotionEngine());
        public static UI3DMotionEngine Instance => _instance.Value;

        /// <summary>Fired whenever any 3D motion setting changes.</summary>
        public event Action? MotionSettingsChanged;

        private UI3DMotionEngine() { }

        // ── Public State Accessors ────────────────────────────────────────

        public bool IsAnimationEnabled
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode) return false;
                return AppSettingsService.Instance.AnimationsEnabled && !AppSettingsService.Instance.ReducedMotion;
            }
        }

        public bool IsTransitionsEnabled
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode) return false;
                return AppSettingsService.Instance.AnimationsEnabled
                    && AppSettingsService.Instance.TransitionsEnabled
                    && !AppSettingsService.Instance.ReducedMotion;
            }
        }

        public bool IsHoverEnabled
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode) return false;
                return AppSettingsService.Instance.AnimationsEnabled
                    && AppSettingsService.Instance.HoverEffectsEnabled
                    && !AppSettingsService.Instance.ReducedMotion;
            }
        }

        public bool IsCardDepthEnabled
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode) return false;
                return AppSettingsService.Instance.AnimationsEnabled
                    && AppSettingsService.Instance.CardDepthEnabled
                    && !AppSettingsService.Instance.ReducedMotion
                    && AppSettingsService.Instance.ThreeDDepthMode != 0;
            }
        }

        /// <summary>3D Depth Mode: 0=Low (flat), 1=Medium (standard 3D), 2=High (deep 3D + tilt).</summary>
        public int ThreeDDepthMode
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode || AppSettingsService.Instance.ReducedMotion)
                    return 0;
                return AppSettingsService.Instance.ThreeDDepthMode;
            }
        }

        /// <summary>Maximum card tilt angle in degrees based on depth mode.</summary>
        public double MaxTiltAngle
        {
            get
            {
                double budgetMax = EffectBudgetManager.Instance.ThreeD.MaxTiltAngle;
                return ThreeDDepthMode switch
                {
                    0 => 0.0,
                    1 => budgetMax * 0.65,
                    2 => budgetMax,
                    _ => budgetMax * 0.65
                };
            }
        }

        /// <summary>3D Scene & lighting intensity multiplier (0.0 to 1.0).</summary>
        public double SceneIntensity
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode || AppSettingsService.Instance.ReducedMotion)
                    return 0.0;
                return AppSettingsService.Instance.ThreeDSceneIntensity;
            }
        }

        /// <summary>Background motion mode: 0=Static, 1=Subtle, 2=Dynamic.</summary>
        public int BackgroundMode
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode || AppSettingsService.Instance.ReducedMotion)
                    return 0;
                return AppSettingsService.Instance.BackgroundMode;
            }
        }

        public bool IsParallaxEnabled
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode || AppSettingsService.Instance.ReducedMotion)
                    return false;
                return BackgroundMode != 0 && ThreeDDepthMode != 0;
            }
        }

        public int MaxParticleCount
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode) return 0;
                if (AppSettingsService.Instance.ReducedMotion) return 0;
                if (BackgroundMode == 0) return 0;

                int userDensity = Math.Clamp(AppSettingsService.Instance.ParticleDensity, 0, 200);
                int budgetCount = EffectBudgetManager.Instance.Particle.ParticleCount;
                
                // Proportionally scale user density by budget
                return Math.Max(15, (int)(userDensity * (budgetCount / 80.0)));
            }
        }

        public double GlowIntensity => App.IsSafeMode ? 0.0 : AppSettingsService.Instance.GlowIntensity;

        public double BackgroundBlurRadius
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode || AppSettingsService.Instance.ReducedMotion)
                    return 0.0;
                double blur = AppSettingsService.Instance.BackgroundBlur;
                if (AppSettingsService.Instance.PerformanceMode ==
                    BiosOptimizer.Core.Implementations.PerformanceProfileMode.LowResource)
                    blur = Math.Min(blur, 10.0);
                return blur;
            }
        }

                public bool IsParallaxStarEnabled
        {
            get
            {
                if (App.IsSafeMode || App.IsNoEffectsMode || AppSettingsService.Instance.ReducedMotion) return false;
                return BackgroundMode != 0 && AppSettingsService.Instance.ParallaxBackground;
            }
        }

        public double StarSpeedMultiplier =>
            App.IsSafeMode ? 0.0 : AppSettingsService.Instance.StarSpeed;

        public double ParallaxStrengthMultiplier =>
            App.IsSafeMode ? 0.0 : AppSettingsService.Instance.ParallaxStrength;

        public double StarGlowStrength =>
            App.IsSafeMode ? 0.0 : AppSettingsService.Instance.StarGlow;

        public double StarOpacityMultiplier =>
            App.IsSafeMode ? 0.0 : AppSettingsService.Instance.StarOpacity;

        public double BlurIntensity => App.IsSafeMode ? 0.0 : AppSettingsService.Instance.BlurIntensity;

        // ── 3D Tilt & Spatial Calculations ────────────────────────────────

        /// <summary>
        /// Calculates pitch (X-axis rotation) and yaw (Y-axis rotation) in degrees
        /// based on normalized cursor position on card (-1.0 to +1.0).
        /// </summary>
        public void CalculateCardTilt(double normX, double normY, out double pitch, out double yaw)
        {
            if (!IsCardDepthEnabled || MaxTiltAngle <= 0.01)
            {
                pitch = 0;
                yaw = 0;
                return;
            }

            normX = Math.Clamp(normX, -1.0, 1.0);
            normY = Math.Clamp(normY, -1.0, 1.0);

            // Cursor at bottom tilts card forward (pitch down = positive X rotation)
            pitch = -normY * MaxTiltAngle * SceneIntensity;
            // Cursor at right tilts card right (yaw right = positive Y rotation)
            yaw = normX * MaxTiltAngle * SceneIntensity;
        }

        /// <summary>
        /// Returns visual Z elevation offset for a given depth level.
        /// </summary>
        public double GetDepthElevation(int depthLevel)
        {
            if (!IsCardDepthEnabled) return 0.0;
            return depthLevel switch
            {
                0 => 0.0,
                1 => 8.0 * SceneIntensity,
                2 => 18.0 * SceneIntensity,
                3 => 28.0 * SceneIntensity,
                4 => 60.0 * SceneIntensity,
                _ => 12.0
            };
        }

        // ── Duration Factory ──────────────────────────────────────────────

        public TimeSpan GetDuration(DurationCategory category)
        {
            if (!IsAnimationEnabled) return TimeSpan.Zero;

            double baseMs = category switch
            {
                DurationCategory.PageTransitionOut   => 80,
                DurationCategory.PageTransitionIn    => 180,
                DurationCategory.CardEntrance        => 200,
                DurationCategory.CardEntranceStagger => 35,
                DurationCategory.ButtonHover         => 120,
                DurationCategory.ButtonPress         => 70,
                DurationCategory.ButtonHoverExit     => 160,
                DurationCategory.SidebarActiveIn     => 160,
                DurationCategory.SidebarActiveOut    => 140,
                DurationCategory.NumberInterpolation => 320,
                DurationCategory.TooltipFadeIn       => 120,
                DurationCategory.TooltipFadeOut      => 80,
                DurationCategory.ModalOpen           => 220,
                DurationCategory.ModalClose          => 150,
                DurationCategory.SuccessReveal       => 300,
                DurationCategory.AmbientMotion       => 8000,
                DurationCategory.ToggleSwitch        => 140,
                DurationCategory.LightSweep          => 35000,
                _                                    => 180
            };

            double speed = Math.Clamp(AppSettingsService.Instance.AnimationSpeed, 0.25, 3.0);
            if (category == DurationCategory.CardEntranceStagger) return TimeSpan.FromMilliseconds(baseMs);

            return TimeSpan.FromMilliseconds(baseMs * speed);
        }

        // ── Easing Factory ────────────────────────────────────────────────

        public IEasingFunction? GetEasing(EasingCategory category)
        {
            return category switch
            {
                EasingCategory.StandardOut  => new CubicEase { EasingMode = EasingMode.EaseOut },
                EasingCategory.StandardIn   => new CubicEase { EasingMode = EasingMode.EaseIn },
                EasingCategory.Decelerate   => new QuarticEase { EasingMode = EasingMode.EaseOut },
                EasingCategory.Accelerate   => new QuarticEase { EasingMode = EasingMode.EaseIn },
                EasingCategory.Spring       => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
                EasingCategory.Bounce       => new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 1, Bounciness = 2 },
                EasingCategory.Linear       => null,
                EasingCategory.SinuOut      => new SineEase { EasingMode = EasingMode.EaseOut },
                _                           => new CubicEase { EasingMode = EasingMode.EaseOut }
            };
        }

        public void NotifySettingsChanged()
        {
            if (Application.Current?.Dispatcher.CheckAccess() == true)
                MotionSettingsChanged?.Invoke();
            else
                Application.Current?.Dispatcher.InvokeAsync(() => MotionSettingsChanged?.Invoke());
        }
    }

    public enum DurationCategory
    {
        PageTransitionOut,
        PageTransitionIn,
        CardEntrance,
        CardEntranceStagger,
        ButtonHover,
        ButtonPress,
        ButtonHoverExit,
        SidebarActiveIn,
        SidebarActiveOut,
        NumberInterpolation,
        TooltipFadeIn,
        TooltipFadeOut,
        ModalOpen,
        ModalClose,
        SuccessReveal,
        AmbientMotion,
        ToggleSwitch,
        LightSweep
    }

    public enum EasingCategory
    {
        StandardOut,
        StandardIn,
        Decelerate,
        Accelerate,
        Spring,
        Bounce,
        Linear,
        SinuOut
    }
}
