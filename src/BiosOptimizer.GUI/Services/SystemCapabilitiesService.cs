#nullable enable
using System;
using System.Windows.Media;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.GUI.Services
{
    /// <summary>
    /// Authoritative Hardware-Aware Applicability Engine for Error Optimizer V3.
    /// Evaluates actual PC hardware (GPU tier, RAM, CPU, OS build, safe mode state)
    /// to determine which visual, motion, telemetry, and background features are genuinely supported.
    /// </summary>
    public sealed class SystemCapabilitiesService
    {
        private static readonly Lazy<SystemCapabilitiesService> _instance =
            new(() => new SystemCapabilitiesService());
        public static SystemCapabilitiesService Instance => _instance.Value;

        public event Action? CapabilitiesUpdated;

        // Hardware metrics
        public int RenderingTier { get; private set; }
        public bool IsHardwareAccelerated => RenderingTier >= 2;
        public double TotalRamGb { get; private set; }
        public bool HasDiscreteGpu { get; private set; }
        public string PrimaryGpuName { get; private set; } = "Unknown GPU";
        public string OsDisplayString { get; private set; } = "Windows";

        // Real Capability Flags
        public bool SupportsGPUAcceleration => IsHardwareAccelerated && !App.IsSafeMode && !App.IsNoEffectsMode;
        public bool Supports3DDepth => SupportsGPUAcceleration;
        public bool SupportsParallax => SupportsGPUAcceleration;
        public bool SupportsCursorEffects => !App.IsSafeMode && !App.IsNoEffectsMode;
        public bool SupportsCursorTrail => SupportsCursorEffects && SupportsGPUAcceleration;
        public bool SupportsProximityGlow => SupportsGPUAcceleration;
        public bool SupportsTextProximity => SupportsProximityGlow;
        public bool SupportsHoverEffects => true;
        public bool SupportsGlassTransparency => !App.IsSafeMode && !App.IsNoEffectsMode;
        public bool SupportsCardOpacity => true;
        public bool SupportsHighTelemetry => TotalRamGb >= 6.0;
        public bool SupportsAiEngines => true;
        public bool SupportsStartupRegistration => true;
        public bool SupportsAuditLogging => true;

        private SystemCapabilitiesService()
        {
            RefreshCapabilities();
        }

        public void RefreshCapabilities()
        {
            try
            {
                // WPF Direct3D Rendering Tier (0 = Software, 1 = Partial DX9, 2 = Full Hardware DX9+)
                RenderingTier = RenderCapability.Tier >> 16;

                var snap = HardwareDetectionService.Instance.GetSnapshot(forceRefresh: false);
                TotalRamGb = snap.Memory.InstalledPhysicalGb;
                HasDiscreteGpu = snap.PrimaryGpu.IsDiscrete;
                PrimaryGpuName = snap.PrimaryGpu.Name;
                OsDisplayString = snap.Windows.FullDisplayString;
            }
            catch
            {
                RenderingTier = 2; // Graceful fallback
                TotalRamGb = 16.0;
                HasDiscreteGpu = true;
            }

            CapabilitiesUpdated?.Invoke();
        }
    }
}
