#nullable enable
using System;

namespace BiosOptimizer.GUI.Services
{
    public class ParallaxBudget
    {
        public int StarCount { get; set; } = 120;
        public int DepthLayers { get; set; } = 3;
        public int UpdateIntervalMs { get; set; } = 16;
        public double GlowOpacityMultiplier { get; set; } = 1.0;
        public double SpeedMultiplier { get; set; } = 1.0;
    }

    public class ParticleBudget
    {
        public int ParticleCount { get; set; } = 80;
        public double SizeScale { get; set; } = 1.0;
        public int UpdateIntervalMs { get; set; } = 16;
        public bool BatchUpdates { get; set; } = true;
    }

    public class GlowBudget
    {
        public double MaxBlurRadius { get; set; } = 35.0;
        public int PassCount { get; set; } = 2;
        public double AlphaMultiplier { get; set; } = 1.0;
    }

    public class ThreeDBudget
    {
        public double MaxTiltAngle { get; set; } = 6.0;
        public double ShadowBlurRadius { get; set; } = 28.0;
        public double ShadowOpacity { get; set; } = 0.55;
        public bool EnableLightTracking { get; set; } = true;
    }

    public class CursorBudget
    {
        public int HitTestThrottleMs { get; set; } = 8;
        public double HaloBaseRadius { get; set; } = 12.5;
        public double GlowBloomRadius { get; set; } = 38.0;
        public int CachedPathSegments { get; set; } = 24;
    }

    public class AnimationBudget
    {
        public double DurationMultiplier { get; set; } = 1.0;
        public bool AllowComplexTransitions { get; set; } = true;
        public bool FrameSkipOnHighLoad { get; set; } = false;
    }

    public class EffectBudgetManager
    {
        private static readonly Lazy<EffectBudgetManager> _instance = new(() => new EffectBudgetManager());
        public static EffectBudgetManager Instance => _instance.Value;

        public event Action? BudgetsUpdated;

        public ParallaxBudget Parallax { get; } = new();
        public ParticleBudget Particle { get; } = new();
        public GlowBudget Glow { get; } = new();
        public ThreeDBudget ThreeD { get; } = new();
        public CursorBudget Cursor { get; } = new();
        public AnimationBudget Animation { get; } = new();

        private EffectBudgetManager()
        {
            RenderingQualityManager.Instance.TierChanged += OnTierChanged;
            RecomputeBudgets(RenderingQualityManager.Instance.CurrentTier);
        }

        private void OnTierChanged(RenderingQualityTier tier)
        {
            RecomputeBudgets(tier);
        }

        public void RecomputeBudgets(RenderingQualityTier tier)
        {
            switch (tier)
            {
                case RenderingQualityTier.UltraLow:
                    // NEVER REMOVE EFFECTS - Scale computational density
                    Parallax.StarCount = 40;
                    Parallax.DepthLayers = 2;
                    Parallax.UpdateIntervalMs = 33; // 30fps update
                    Parallax.GlowOpacityMultiplier = 0.55;
                    Parallax.SpeedMultiplier = 0.85;

                    Particle.ParticleCount = 25;
                    Particle.SizeScale = 0.85;
                    Particle.UpdateIntervalMs = 33;
                    Particle.BatchUpdates = true;

                    Glow.MaxBlurRadius = 16.0;
                    Glow.PassCount = 1;
                    Glow.AlphaMultiplier = 0.65;

                    ThreeD.MaxTiltAngle = 2.5;
                    ThreeD.ShadowBlurRadius = 12.0;
                    ThreeD.ShadowOpacity = 0.35;
                    ThreeD.EnableLightTracking = false;

                    Cursor.HitTestThrottleMs = 32;
                    Cursor.HaloBaseRadius = 10.5;
                    Cursor.GlowBloomRadius = 26.0;
                    Cursor.CachedPathSegments = 12;

                    Animation.DurationMultiplier = 0.75;
                    Animation.AllowComplexTransitions = true;
                    Animation.FrameSkipOnHighLoad = true;
                    break;

                case RenderingQualityTier.Low:
                    Parallax.StarCount = 75;
                    Parallax.DepthLayers = 2;
                    Parallax.UpdateIntervalMs = 20; // 50fps update
                    Parallax.GlowOpacityMultiplier = 0.75;
                    Parallax.SpeedMultiplier = 0.95;

                    Particle.ParticleCount = 45;
                    Particle.SizeScale = 0.90;
                    Particle.UpdateIntervalMs = 20;
                    Particle.BatchUpdates = true;

                    Glow.MaxBlurRadius = 24.0;
                    Glow.PassCount = 1;
                    Glow.AlphaMultiplier = 0.80;

                    ThreeD.MaxTiltAngle = 4.0;
                    ThreeD.ShadowBlurRadius = 20.0;
                    ThreeD.ShadowOpacity = 0.45;
                    ThreeD.EnableLightTracking = true;

                    Cursor.HitTestThrottleMs = 16;
                    Cursor.HaloBaseRadius = 11.5;
                    Cursor.GlowBloomRadius = 32.0;
                    Cursor.CachedPathSegments = 16;

                    Animation.DurationMultiplier = 0.90;
                    Animation.AllowComplexTransitions = true;
                    Animation.FrameSkipOnHighLoad = true;
                    break;

                case RenderingQualityTier.Balanced:
                    Parallax.StarCount = 120;
                    Parallax.DepthLayers = 3;
                    Parallax.UpdateIntervalMs = 16; // 60fps update
                    Parallax.GlowOpacityMultiplier = 1.0;
                    Parallax.SpeedMultiplier = 1.0;

                    Particle.ParticleCount = 80;
                    Particle.SizeScale = 1.0;
                    Particle.UpdateIntervalMs = 16;
                    Particle.BatchUpdates = true;

                    Glow.MaxBlurRadius = 35.0;
                    Glow.PassCount = 2;
                    Glow.AlphaMultiplier = 1.0;

                    ThreeD.MaxTiltAngle = 6.0;
                    ThreeD.ShadowBlurRadius = 28.0;
                    ThreeD.ShadowOpacity = 0.55;
                    ThreeD.EnableLightTracking = true;

                    Cursor.HitTestThrottleMs = 8;
                    Cursor.HaloBaseRadius = 12.5;
                    Cursor.GlowBloomRadius = 38.0;
                    Cursor.CachedPathSegments = 24;

                    Animation.DurationMultiplier = 1.0;
                    Animation.AllowComplexTransitions = true;
                    Animation.FrameSkipOnHighLoad = false;
                    break;

                case RenderingQualityTier.High:
                    Parallax.StarCount = 180;
                    Parallax.DepthLayers = 3;
                    Parallax.UpdateIntervalMs = 16;
                    Parallax.GlowOpacityMultiplier = 1.15;
                    Parallax.SpeedMultiplier = 1.05;

                    Particle.ParticleCount = 120;
                    Particle.SizeScale = 1.1;
                    Particle.UpdateIntervalMs = 16;
                    Particle.BatchUpdates = true;

                    Glow.MaxBlurRadius = 48.0;
                    Glow.PassCount = 2;
                    Glow.AlphaMultiplier = 1.15;

                    ThreeD.MaxTiltAngle = 8.0;
                    ThreeD.ShadowBlurRadius = 38.0;
                    ThreeD.ShadowOpacity = 0.65;
                    ThreeD.EnableLightTracking = true;

                    Cursor.HitTestThrottleMs = 0;
                    Cursor.HaloBaseRadius = 13.5;
                    Cursor.GlowBloomRadius = 44.0;
                    Cursor.CachedPathSegments = 32;

                    Animation.DurationMultiplier = 1.0;
                    Animation.AllowComplexTransitions = true;
                    Animation.FrameSkipOnHighLoad = false;
                    break;

                case RenderingQualityTier.Ultra:
                    Parallax.StarCount = 250;
                    Parallax.DepthLayers = 4;
                    Parallax.UpdateIntervalMs = 16;
                    Parallax.GlowOpacityMultiplier = 1.30;
                    Parallax.SpeedMultiplier = 1.10;

                    Particle.ParticleCount = 160;
                    Particle.SizeScale = 1.2;
                    Particle.UpdateIntervalMs = 16;
                    Particle.BatchUpdates = true;

                    Glow.MaxBlurRadius = 60.0;
                    Glow.PassCount = 3;
                    Glow.AlphaMultiplier = 1.25;

                    ThreeD.MaxTiltAngle = 10.0;
                    ThreeD.ShadowBlurRadius = 48.0;
                    ThreeD.ShadowOpacity = 0.75;
                    ThreeD.EnableLightTracking = true;

                    Cursor.HitTestThrottleMs = 0;
                    Cursor.HaloBaseRadius = 14.5;
                    Cursor.GlowBloomRadius = 50.0;
                    Cursor.CachedPathSegments = 36;

                    Animation.DurationMultiplier = 1.0;
                    Animation.AllowComplexTransitions = true;
                    Animation.FrameSkipOnHighLoad = false;
                    break;
            }

            BudgetsUpdated?.Invoke();
        }
    }
}
