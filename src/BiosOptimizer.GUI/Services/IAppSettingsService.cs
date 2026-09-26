#nullable enable
using System;
using BiosOptimizer.Core.Implementations;

namespace BiosOptimizer.GUI.Services
{
    public interface IAppSettingsService
    {
        // ── Appearance & Theme Mode ──────────────────────────────────────
        int ThemeMode { get; set; }
        string GradientStops { get; set; }
        double GradientAngle { get; set; }
        string GradientDirection { get; set; }
        int GradientAnimationMode { get; set; }
        string AccentColor { get; set; }
        double WindowGlassTransparency { get; set; }
        double CardOpacity { get; set; }
        bool AnimationsEnabled { get; set; }
        bool TransitionsEnabled { get; set; }
        bool ReducedMotion { get; set; }
        bool GlowEnabled { get; set; }
        bool CursorEffect { get; set; }
        double CursorHaloSize { get; set; }
        double CursorGlowIntensity { get; set; }
        bool CursorTrail { get; set; }
        bool ThreeDEffect { get; set; }
        double HueOffset { get; set; }

        // ── Motion & Spatial 3D System (Flagship Control Center) ───────────
        /// <summary>Animation speed multiplier: 0.25x – 3.0x. Default 1.0x.</summary>
        double AnimationSpeed { get; set; }
        /// <summary>Glow intensity: 0.0 (off) to 1.0 (100%). Controls alpha of glow brushes. Default 0.70.</summary>
        double GlowIntensity { get; set; }
        /// <summary>Background & glass blur radius in pixels (0.0 – 50.0 px). Default 30.0 px.</summary>
        double BackgroundBlur { get; set; }
        /// <summary>Legacy alias for blur intensity 0.0 - 1.0.</summary>
        double BlurIntensity { get; set; }
        /// <summary>Particle density: 0 – 200 particles. Default 80.</summary>
        int ParticleDensity { get; set; }
        /// <summary>Legacy alias for particle intensity 0-3.</summary>
        int ParticleIntensity { get; set; }
        /// <summary>Background motion mode: 0=Static, 1=Subtle (default), 2=Dynamic.</summary>
        int BackgroundMode { get; set; }
        /// <summary>Whether hover effects (scale, border glow) are active. Default true.</summary>
        bool HoverEffectsEnabled { get; set; }
        /// <summary>Whether background ambient motion is active. Default true.</summary>
        bool BackgroundMotionEnabled { get; set; }
        /// <summary>Whether 3D card depth & hover tilt are active. Default true.</summary>
        bool CardDepthEnabled { get; set; }
        /// <summary>3D Depth mode: 0=Low, 1=Medium (default), 2=High.</summary>
        int ThreeDDepthMode { get; set; }
        /// <summary>3D Scene & lighting intensity: 0.0 to 1.0 (default 0.75).</summary>
        double ThreeDSceneIntensity { get; set; }

        // ── Cursor Proximity Card Glow System (SanketSS84 Proximity-Reactive Glow) ──
        bool CardProximityGlow { get; set; }
        double ProximityGlowStrength { get; set; }
        double ProximityRange { get; set; }
        bool TextProximityGlow { get; set; }

        // ── Parallax Star Background Engine (WebDevSHORTS Parallax-Star-Background) ──
        bool ParallaxBackground { get; set; }
        int StarDensity { get; set; }
        double StarOpacity { get; set; }
        double ParallaxStrength { get; set; }
        double StarSpeed { get; set; }
        double StarGlow { get; set; }
        int StarDepth { get; set; }
        bool StarTwinkle { get; set; }

        // ── Performance & Polling ─────────────────────────────────────────
        PerformanceProfileMode PerformanceMode { get; set; }
        int LiveMonitorInterval { get; set; }

        // ── Safety ───────────────────────────────────────────────────────
        bool ConfirmMediumRisk { get; set; }
        bool ConfirmHighRisk { get; set; }
        bool AutoBackup { get; set; }

        // ── Logging ──────────────────────────────────────────────────────
        bool DetailedLogs { get; set; }
        int LogRetentionDays { get; set; }

        // ── Startup & Background ──────────────────────────────────────────
        bool StartWithWindows { get; set; }
        bool StartMinimized { get; set; }
        bool AiPowerPlanAutoEnable { get; set; }
        bool AiWorkloadAutoStart { get; set; }
        bool AiRamLimiterAutoStart { get; set; }
        bool SmartAutoOptimizeAutoStart { get; set; }

        event Action? SettingsChanged;
        event Action<string, object?, object?>? SettingChanged;
        void Save();
        void ResetToDefaults();
    }
}
