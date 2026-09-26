#nullable enable
using System;
using System.Diagnostics;
using System.Windows.Media;

namespace BiosOptimizer.GUI.Services
{
    public class PerformanceManager
    {
        private static readonly Lazy<PerformanceManager> _instance = new(() => new PerformanceManager());
        public static PerformanceManager Instance => _instance.Value;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _lastFrameTicks = 0;
        private double _smoothedFrameTimeMs = 16.6;
        private int _slowFrameCount = 0;
        private int _fastFrameCount = 0;
        private DateTime _lastTierAdjustment = DateTime.UtcNow;
        private readonly TimeSpan _minAdjustmentCooldown = TimeSpan.FromSeconds(10.0);

        public double CurrentFps => _smoothedFrameTimeMs > 0.1 ? Math.Round(1000.0 / _smoothedFrameTimeMs, 1) : 60.0;
        public double SmoothedFrameTimeMs => Math.Round(_smoothedFrameTimeMs, 2);

        private PerformanceManager()
        {
            CompositionTarget.Rendering += OnCompositionRendering;
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            long currentTicks = _stopwatch.ElapsedTicks;
            if (_lastFrameTicks > 0)
            {
                double deltaMs = (currentTicks - _lastFrameTicks) * 1000.0 / Stopwatch.Frequency;
                if (deltaMs > 1.0 && deltaMs < 200.0)
                {
                    // Exponential moving average (alpha = 0.05 for smooth tracking)
                    _smoothedFrameTimeMs = (_smoothedFrameTimeMs * 0.95) + (deltaMs * 0.05);

                    // Dynamic adaptation with hysteresis
                    if (!RenderingQualityManager.Instance.IsManualOverride)
                    {
                        EvaluateDynamicAdaptation(deltaMs);
                    }
                }
            }
            _lastFrameTicks = currentTicks;
        }

        private void EvaluateDynamicAdaptation(double frameDeltaMs)
        {
            if (DateTime.UtcNow - _lastTierAdjustment < _minAdjustmentCooldown)
            {
                return; // Enforce strict cooldown to prevent oscillation/popping
            }

            // If frame times are consistently exceeding 33ms (< 30 FPS)
            if (frameDeltaMs > 33.3)
            {
                _slowFrameCount++;
                _fastFrameCount = 0;

                if (_slowFrameCount > 90) // ~3 seconds of sustained frame drops
                {
                    _slowFrameCount = 0;
                    StepDownQuality();
                }
            }
            // If frame times are consistently under 17ms (60 FPS)
            else if (frameDeltaMs < 18.0)
            {
                _fastFrameCount++;
                _slowFrameCount = 0;

                if (_fastFrameCount > 900) // ~15 seconds of sustained flawless 60 FPS
                {
                    _fastFrameCount = 0;
                    StepUpQuality();
                }
            }
            else
            {
                _slowFrameCount = Math.Max(0, _slowFrameCount - 1);
                _fastFrameCount = Math.Max(0, _fastFrameCount - 1);
            }
        }

        private void StepDownQuality()
        {
            var current = RenderingQualityManager.Instance.CurrentTier;
            if (current > RenderingQualityTier.UltraLow)
            {
                var next = (RenderingQualityTier)((int)current - 1);
                RenderingQualityManager.Instance.SetTier(next, isManual: false);
                _lastTierAdjustment = DateTime.UtcNow;
                Debug.WriteLine($"[PerformanceManager] Stepped down quality tier to: {next}");
            }
        }

        private void StepUpQuality()
        {
            var current = RenderingQualityManager.Instance.CurrentTier;
            var baseline = RenderingQualityManager.Instance.BaselineTier;
            // Only step up if below hardware baseline
            if (current < baseline)
            {
                var next = (RenderingQualityTier)((int)current + 1);
                RenderingQualityManager.Instance.SetTier(next, isManual: false);
                _lastTierAdjustment = DateTime.UtcNow;
                Debug.WriteLine($"[PerformanceManager] Stepped up quality tier to: {next}");
            }
        }
    }
}
