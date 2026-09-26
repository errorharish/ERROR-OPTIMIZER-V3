using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations
{
    public enum PerformanceProfileMode
    {
        Auto,
        LowResource,
        Balanced,
        HighTelemetry
    }

    public interface IResourceGovernor
    {
        HardwareProfile Profile { get; }
        HardwareTier ActiveTier { get; }
        PerformanceProfileMode Mode { get; set; }
        int MaxWorkerConcurrency { get; }
        int TelemetryIntervalMs { get; }
        int FileBatchSize { get; }
        bool IsThrottled { get; }
        string ThrottlingReason { get; }
        bool IsLowResourceMode { get; }
        event Action<bool, string>? ThrottlingChanged;
        event Action<PerformanceProfileMode>? ModeChanged;
        int? UserConfiguredIntervalMs { get; set; }
        Task YieldIfThrottledAsync(CancellationToken ct = default);
        void SetManualOverride(HardwareTier? tierOverride);
        void SetPerformanceMode(PerformanceProfileMode mode);
        (double WorkingSetMb, double PrivateBytesMb, double GcHeapMb) GetAppMemoryFootprint();
        void TrimAppMemory();
    }

    public class AdaptiveResourceGovernor : IResourceGovernor
    {
        private static readonly Lazy<AdaptiveResourceGovernor> _instance = new(() => new AdaptiveResourceGovernor());
        public static AdaptiveResourceGovernor Instance => _instance.Value;

        private HardwareProfile _profile;
        private HardwareTier? _manualTierOverride;
        private PerformanceProfileMode _mode = PerformanceProfileMode.Auto;
        private int? _userConfiguredIntervalMs;
        private bool _isThrottled;
        private string _throttlingReason = string.Empty;
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly object _stateLock = new();
        private DateTime _lastMemoryTrim = DateTime.MinValue;
        private bool _isWindowVisible = true;

        public bool IsWindowVisible => _isWindowVisible;

        public void SetWindowVisibility(bool isVisible)
        {
            lock (_stateLock)
            {
                _isWindowVisible = isVisible;
                if (!isVisible)
                {
                    _isThrottled = true;
                    _throttlingReason = "Background Window Hidden Throttling";
                }
                else if (_throttlingReason == "Background Window Hidden Throttling")
                {
                    _isThrottled = false;
                    _throttlingReason = string.Empty;
                }
            }
            if (!isVisible)
            {
                TrimAppMemory();
            }
            ThrottlingChanged?.Invoke(_isThrottled, _throttlingReason);
        }

        public event Action<bool, string>? ThrottlingChanged;
        public event Action<PerformanceProfileMode>? ModeChanged;

        public HardwareProfile Profile
        {
            get
            {
                lock (_stateLock) return _profile;
            }
        }

        public PerformanceProfileMode Mode
        {
            get { lock (_stateLock) return _mode; }
            set { SetPerformanceMode(value); }
        }

        public HardwareTier ActiveTier
        {
            get
            {
                lock (_stateLock)
                {
                    if (_manualTierOverride.HasValue)
                        return _manualTierOverride.Value;

                    return _mode switch
                    {
                        PerformanceProfileMode.LowResource => HardwareTier.LowResource,
                        PerformanceProfileMode.Balanced => HardwareTier.Normal,
                        PerformanceProfileMode.HighTelemetry => HardwareTier.HighPerformance,
                        _ => _profile.Tier
                    };
                }
            }
        }

        public bool IsLowResourceMode => ActiveTier == HardwareTier.LowResource || _isThrottled;

        public int MaxWorkerConcurrency => ActiveTier switch
        {
            HardwareTier.LowResource => 1,
            HardwareTier.HighPerformance => (_isThrottled ? 2 : 4),
            _ => (_isThrottled ? 1 : 2)
        };

        public int? UserConfiguredIntervalMs
        {
            get { lock (_stateLock) return _userConfiguredIntervalMs; }
            set { lock (_stateLock) _userConfiguredIntervalMs = value; }
        }

        public int TelemetryIntervalMs
        {
            get
            {
                lock (_stateLock)
                {
                    if (!_isWindowVisible) return 10000; // Ultra-low 10s polling when minimized/background
                    if (_userConfiguredIntervalMs.HasValue) return _userConfiguredIntervalMs.Value;
                    return ActiveTier switch
                    {
                        HardwareTier.LowResource => (_isThrottled ? 4000 : 2500),
                        HardwareTier.HighPerformance => (_isThrottled ? 1500 : 800),
                        _ => (_isThrottled ? 2500 : 1200)
                    };
                }
            }
        }

        private int _legacyTelemetryIntervalMs
        {
            get
            {
                if (_isThrottled) return Math.Max(_userConfiguredIntervalMs ?? 5000, 5000);
                if (_userConfiguredIntervalMs.HasValue) return _userConfiguredIntervalMs.Value;
                
                return ActiveTier switch
                {
                    HardwareTier.LowResource => 4500,
                    HardwareTier.HighPerformance => 1200,
                    _ => 2500
                };
            }
        }

        public int FileBatchSize => ActiveTier switch
        {
            HardwareTier.LowResource => 80,
            HardwareTier.HighPerformance => (_isThrottled ? 250 : 800),
            _ => (_isThrottled ? 120 : 400)
        };

        public bool IsThrottled
        {
            get { lock (_stateLock) return _isThrottled; }
        }

        public string ThrottlingReason
        {
            get { lock (_stateLock) return _throttlingReason; }
        }

        public SemaphoreSlim ConcurrencyLimiter => _concurrencyLimiter;

        private AdaptiveResourceGovernor()
        {
            _profile = HardwareProfiler.GetQuickProfile();
            _concurrencyLimiter = new SemaphoreSlim(MaxWorkerConcurrency, 4);
        }

        public void RefreshProfile()
        {
            lock (_stateLock)
            {
                _profile = HardwareProfiler.GetQuickProfile(forceRefresh: true);
            }
            EvaluateSystemMemoryPressure();
        }

        public void SetPerformanceMode(PerformanceProfileMode mode)
        {
            lock (_stateLock)
            {
                _mode = mode;
            }
            ModeChanged?.Invoke(mode);
            ThrottlingChanged?.Invoke(_isThrottled, _throttlingReason);
        }

        public void SetManualOverride(HardwareTier? tierOverride)
        {
            lock (_stateLock)
            {
                _manualTierOverride = tierOverride;
            }
            ThrottlingChanged?.Invoke(_isThrottled, _throttlingReason);
        }

        public void ReportSystemBusy(bool isBusy, string reason)
        {
            bool changed = false;
            lock (_stateLock)
            {
                if (_isThrottled != isBusy || _throttlingReason != reason)
                {
                    _isThrottled = isBusy;
                    _throttlingReason = isBusy ? reason : string.Empty;
                    changed = true;
                }
            }

            if (changed)
            {
                ThrottlingChanged?.Invoke(isBusy, reason);
            }
        }

        public void EvaluateSystemMemoryPressure()
        {
            try
            {
                var snap = Memory.WindowsMemoryListProvider.Instance.GetCurrentSnapshot();
                if (snap.TotalPhysicalBytes > 0)
                {
                    double availMb = snap.AvailableBytes / (1024.0 * 1024.0);
                    double availPct = (double)snap.AvailableBytes / snap.TotalPhysicalBytes * 100.0;

                    if (availPct < 15.0 || availMb < 1200)
                    {
                        ReportSystemBusy(true, $"Critically Low System Memory ({availMb:F0} MB Available)");
                    }
                    else if (_isThrottled && _throttlingReason.Contains("Low System Memory"))
                    {
                        ReportSystemBusy(false, string.Empty);
                    }
                }
            }
            catch { }
        }

        public async Task YieldIfThrottledAsync(CancellationToken ct = default)
        {
            if (_isThrottled || ActiveTier == HardwareTier.LowResource)
            {
                // Give back CPU slices to user foreground tasks
                await Task.Delay(50, ct);
            }
        }

        public (double WorkingSetMb, double PrivateBytesMb, double GcHeapMb) GetAppMemoryFootprint()
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                proc.Refresh();
                double wsMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                double pvtMb = proc.PrivateMemorySize64 / (1024.0 * 1024.0);
                double gcMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                return (wsMb, pvtMb, gcMb);
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        public void TrimAppMemory()
        {
            try
            {
                if ((DateTime.UtcNow - _lastMemoryTrim).TotalSeconds < 30.0) return;
                _lastMemoryTrim = DateTime.UtcNow;

                GC.Collect(2, GCCollectionMode.Optimized, false, false);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Optimized, false, false);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    NativeSetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch { }
        }

        [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize", SetLastError = true)]
        private static extern bool NativeSetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);
    }
}
