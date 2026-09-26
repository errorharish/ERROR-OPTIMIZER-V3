#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace BiosOptimizer.GUI.Services
{
    public enum RenderingQualityTier
    {
        UltraLow = 0,
        Low = 1,
        Balanced = 2,
        High = 3,
        Ultra = 4
    }

    public class RenderingQualityManager
    {
        private static readonly Lazy<RenderingQualityManager> _instance = new(() => new RenderingQualityManager());
        public static RenderingQualityManager Instance => _instance.Value;

        public event Action<RenderingQualityTier>? TierChanged;

        private RenderingQualityTier _currentTier = RenderingQualityTier.Balanced;
        private RenderingQualityTier _baselineTier = RenderingQualityTier.Balanced;
        private bool _isManualOverride = false;

        public RenderingQualityTier CurrentTier
        {
            get => _currentTier;
            private set
            {
                if (_currentTier != value)
                {
                    var old = _currentTier;
                    _currentTier = value;
                    TierChanged?.Invoke(value);
                }
            }
        }

        public RenderingQualityTier BaselineTier => _baselineTier;
        public bool IsManualOverride => _isManualOverride;

        public int WpfRenderTier { get; private set; } = 2;
        public int LogicalCoreCount { get; private set; } = 4;
        public double TotalMemoryGb { get; private set; } = 8.0;
        public bool IsDedicatedGpu { get; private set; } = false;
        public string GpuDescription { get; private set; } = "Hardware Renderer";
        public string HardwareSummary { get; private set; } = "";

        private RenderingQualityManager()
        {
            DetectHardwareCapabilities();
        }

        public void DetectHardwareCapabilities()
        {
            try
            {
                LogicalCoreCount = Environment.ProcessorCount;
                WpfRenderTier = RenderCapability.Tier >> 16;

                // Memory detection
                try
                {
                    var memStatus = new MEMORYSTATUSEX();
                    memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                    if (GlobalMemoryStatusEx(ref memStatus))
                    {
                        TotalMemoryGb = Math.Round(memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    }
                    else
                    {
                        TotalMemoryGb = 8.0;
                    }
                }
                catch
                {
                    TotalMemoryGb = 8.0;
                }

                // Screen resolution check
                double screenPixels = SystemParameters.PrimaryScreenWidth * SystemParameters.PrimaryScreenHeight;

                // Determine baseline tier based on hardware capabilities
                if (WpfRenderTier == 0 || LogicalCoreCount <= 2 || TotalMemoryGb < 3.8)
                {
                    _baselineTier = RenderingQualityTier.UltraLow;
                }
                else if (WpfRenderTier == 1 || LogicalCoreCount <= 4 || TotalMemoryGb < 7.5)
                {
                    _baselineTier = RenderingQualityTier.Low;
                }
                else if (LogicalCoreCount <= 8 && TotalMemoryGb < 15.5)
                {
                    _baselineTier = RenderingQualityTier.Balanced;
                }
                else if (LogicalCoreCount <= 16 || TotalMemoryGb < 31.5)
                {
                    _baselineTier = RenderingQualityTier.High;
                }
                else
                {
                    _baselineTier = RenderingQualityTier.Ultra;
                }

                if (!_isManualOverride)
                {
                    _currentTier = _baselineTier;
                }

                HardwareSummary = $"{LogicalCoreCount} Cores, {TotalMemoryGb:0.0} GB RAM, WPF Tier {WpfRenderTier} -> {_baselineTier}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hardware detection fallback: {ex.Message}");
                _baselineTier = RenderingQualityTier.Balanced;
                _currentTier = RenderingQualityTier.Balanced;
            }
        }

        public void SetTier(RenderingQualityTier tier, bool isManual = true)
        {
            _isManualOverride = isManual;
            CurrentTier = tier;
        }

        public void ResetToAuto()
        {
            _isManualOverride = false;
            CurrentTier = _baselineTier;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
