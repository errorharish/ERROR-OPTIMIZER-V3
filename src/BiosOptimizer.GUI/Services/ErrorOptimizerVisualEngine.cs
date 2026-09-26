#nullable enable
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BiosOptimizer.GUI.Services
{
    /// <summary>
    /// Central Global Visual & Spatial Material Authority for Error Optimizer V3.
    /// Unifies 3D Spatial Physics, Glassmorphic Materials, Dynamic Background Atmosphere,
    /// Multi-Layer Particle Simulation, Custom Interactive Cursor, Precision Sliders,
    /// 3D Raised Buttons, Glass Dropdowns, and Global Smooth Page Transitions.
    /// </summary>
    public sealed class ErrorOptimizerVisualEngine
    {
        private static readonly Lazy<ErrorOptimizerVisualEngine> _instance =
            new(() => new ErrorOptimizerVisualEngine());
        public static ErrorOptimizerVisualEngine Instance => _instance.Value;

        // Subsystem references
        public IAppSettingsService Settings => AppSettingsService.Instance;
        public ThemeBrushService Theme => ThemeBrushService.Instance;
        public UI3DMotionEngine Motion => UI3DMotionEngine.Instance;

        /// <summary>Event raised whenever any visual, material, or motion property updates.</summary>
        public event Action? VisualSystemUpdated;

        private ErrorOptimizerVisualEngine()
        {
            AppSettingsService.Instance.SettingsChanged += OnSettingsChanged;
            ThemeBrushService.Instance.AccentColorChanged += OnAccentColorChanged;
            UI3DMotionEngine.Instance.MotionSettingsChanged += OnMotionSettingsChanged;
        }

        private void OnSettingsChanged()
        {
            NotifyVisualUpdate();
        }

        private void OnAccentColorChanged(Color newAccent)
        {
            NotifyVisualUpdate();
        }

        private void OnMotionSettingsChanged()
        {
            NotifyVisualUpdate();
        }

        public void NotifyVisualUpdate()
        {
            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                VisualSystemUpdated?.Invoke();
            }
            else
            {
                Application.Current?.Dispatcher.InvokeAsync(() => VisualSystemUpdated?.Invoke());
            }
        }

        // ── Global Visual Material Queries ────────────────────────────────

        public bool IsVisualEffectsActive =>
            !App.IsSafeMode && !App.IsNoEffectsMode && !Settings.ReducedMotion;

        public bool Is3DDepthActive =>
            IsVisualEffectsActive && Settings.CardDepthEnabled && Settings.ThreeDDepthMode > 0;

        public bool IsParticleSystemActive =>
            IsVisualEffectsActive && Settings.BackgroundMode > 0 && Settings.ParticleDensity > 0;

        public bool IsCursorOverlayActive =>
            !App.IsSafeMode && !App.IsNoEffectsMode && Settings.CursorEffect;

        public bool IsCursorTrailActive =>
            IsCursorOverlayActive && Settings.CursorTrail && !Settings.ReducedMotion;

        public double CurrentTiltAngle => Motion.MaxTiltAngle;

        public double CurrentGlowIntensity => Motion.GlowIntensity;

        public double CurrentSceneIntensity => Motion.SceneIntensity;

        public int CurrentParticleCount => Motion.MaxParticleCount;

        public TimeSpan GetTransitionDuration(DurationCategory category) =>
            Motion.GetDuration(category);

        public IEasingFunction? GetEasing(EasingCategory category) =>
            Motion.GetEasing(category);
    }
}
