using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations
{
    public enum RamLimiterViewMode
    {
        Normal = 0,
        Custom = 1
    }

    public enum RamLimiterMode
    {
        Auto = 0,
        Light = 1,
        Aggressive = 2
    }

    public enum MemoryPressureLevel
    {
        Normal,
        Elevated,
        High,
        Critical
    }

    public enum RamAppStatus
    {
        Normal,
        NearLimit,
        HighRam,
        LimitActive,
        Trimmed,
        Protected,
        Active,
        Disabled,
        AccessDenied,
        Unavailable
    }

    public class CustomTargetItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = string.Empty;
        public string ExecutableName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public double RamLimitMb { get; set; } = 1500;
        public bool IsEnabled { get; set; } = true;
        public bool ForegroundProtection { get; set; } = true;
        public bool IsForeground { get; set; }

        private double _currentRamMb;
        public double CurrentRamMb
        {
            get => _currentRamMb;
            set { if (Math.Abs(_currentRamMb - value) > 0.1) { _currentRamMb = value; OnPropertyChanged(nameof(CurrentRamMb)); OnPropertyChanged(nameof(CurrentRamDisplay)); } }
        }

        public string CurrentRamDisplay => CurrentRamMb >= 1024 ? $"{CurrentRamMb / 1024.0:F1} GB" : $"{CurrentRamMb:F0} MB";
        public string LimitDisplay => RamLimitMb >= 1024 ? $"{RamLimitMb / 1024.0:F1} GB" : $"{RamLimitMb:F0} MB";

        public int ProcessCount { get; set; }

        private RamAppStatus _status = RamAppStatus.Active;
        public RamAppStatus Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusBrush)); } }
        }

        public string StatusText => IsEnabled ? (Status switch
        {
            RamAppStatus.Trimmed => "TRIMMED",
            RamAppStatus.HighRam => "HIGH RAM",
            RamAppStatus.Protected => "PROTECTED",
            RamAppStatus.Normal => "ACTIVE (NORMAL)",
            _ => "ACTIVE"
        }) : "DISABLED";

        public string StatusBrush => IsEnabled ? (Status switch
        {
            RamAppStatus.Trimmed => "#8B5CF6",
            RamAppStatus.HighRam => "#EF4444",
            RamAppStatus.Protected => "#10B981",
            _ => "#10B981"
        }) : "#6B7280";

        public DateTime LastActionTime { get; set; } = DateTime.MinValue;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public class SearchableAppItem : INotifyPropertyChanged
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ExecutableName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public double CurrentRamMb { get; set; }
        public string CurrentRamDisplay => CurrentRamMb > 0 ? (CurrentRamMb >= 1024 ? $"{CurrentRamMb / 1024.0:F1} GB" : $"{CurrentRamMb:F0} MB") : "Not Running";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                    OnPropertyChanged(nameof(SelectionBorderBrush));
                    OnPropertyChanged(nameof(SelectButtonText));
                    OnPropertyChanged(nameof(SelectButtonBrush));
                }
            }
        }

        public string SelectionBorderBrush => IsSelected ? "#E53935" : "#1AFFFFFF";
        public string SelectButtonText => IsSelected ? "SELECTED" : "SELECT";
        public string SelectButtonBrush => IsSelected ? "#E53935" : "#11FFFFFF";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RamLimiterConfig
    {
        public bool IsEnabled { get; set; } = false;
        public RamLimiterViewMode ViewMode { get; set; } = RamLimiterViewMode.Normal;
        public RamLimiterMode NormalMode { get; set; } = RamLimiterMode.Auto;
        public bool ForegroundProtection { get; set; } = true;
        public List<CustomTargetItem> CustomTargets { get; set; } = new();
    }

    public class RamLimiterLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
        public string ApplicationName { get; set; } = string.Empty;
        public double BeforeRamMb { get; set; }
        public string BeforeDisplay => BeforeRamMb >= 1024 ? $"{BeforeRamMb / 1024.0:F1} GB" : $"{BeforeRamMb:F0} MB";
        public string Policy { get; set; } = string.Empty;
        public string ActionTaken { get; set; } = string.Empty;
        public double AfterRamMb { get; set; }
        public string AfterDisplay => AfterRamMb >= 1024 ? $"{AfterRamMb / 1024.0:F1} GB" : $"{AfterRamMb:F0} MB";
        public string Verification { get; set; } = "Verified";
        public string Status { get; set; } = "TRIMMED";
    }

    public class TargetProcessGroup : INotifyPropertyChanged
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string ExecutableName { get; set; } = string.Empty;
        public int PrimaryPid { get; set; }
        public List<int> ProcessIds { get; set; } = new();
        public int ProcessCount => ProcessIds.Count;

        private double _currentRamMb;
        public double CurrentRamMb
        {
            get => _currentRamMb;
            set
            {
                if (Math.Abs(_currentRamMb - value) > 0.1)
                {
                    _currentRamMb = value;
                    OnPropertyChanged(nameof(CurrentRamMb));
                    OnPropertyChanged(nameof(RamDisplay));
                }
            }
        }

        public string RamDisplay => CurrentRamMb >= 1024 ? $"{CurrentRamMb / 1024.0:F1} GB" : $"{CurrentRamMb:F0} MB";

        public double RecommendedLimitMb { get; set; }
        public string RecommendedLimitDisplay => RecommendedLimitMb >= 1024 ? $"{RecommendedLimitMb / 1024.0:F1} GB" : $"{RecommendedLimitMb:F0} MB";

        private bool _isForeground;
        public bool IsForeground
        {
            get => _isForeground;
            set
            {
                if (_isForeground != value)
                {
                    _isForeground = value;
                    OnPropertyChanged(nameof(IsForeground));
                    OnPropertyChanged(nameof(ForegroundDisplay));
                }
            }
        }

        public string ForegroundDisplay => IsForeground ? "Foreground" : "Background";

        private RamAppStatus _status = RamAppStatus.Normal;
        public RamAppStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBrush));
                }
            }
        }

        public string StatusText => Status switch
        {
            RamAppStatus.Normal => "NORMAL",
            RamAppStatus.NearLimit => "NEAR LIMIT",
            RamAppStatus.HighRam => "HIGH RAM",
            RamAppStatus.LimitActive => "LIMIT ACTIVE",
            RamAppStatus.Trimmed => "TRIMMED",
            RamAppStatus.Protected => "PROTECTED",
            RamAppStatus.AccessDenied => "ACCESS DENIED",
            _ => "UNAVAILABLE"
        };

        public string StatusBrush => Status switch
        {
            RamAppStatus.Normal => "#10B981",
            RamAppStatus.NearLimit => "#F59E0B",
            RamAppStatus.HighRam => "#EF4444",
            RamAppStatus.LimitActive => "#3B82F6",
            RamAppStatus.Trimmed => "#8B5CF6",
            RamAppStatus.Protected => "#10B981",
            RamAppStatus.AccessDenied => "#F97316",
            _ => "#6B7280"
        };

        public string LastAction { get; set; } = "None";
        public DateTime LastActionTime { get; set; } = DateTime.MinValue;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public class RamLimiterEngine
    {
        private static readonly Lazy<RamLimiterEngine> _instance = new(() => new RamLimiterEngine());
        public static RamLimiterEngine Instance => _instance.Value;

        private readonly string _configFilePath;
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private readonly object _lock = new();

        public RamLimiterConfig Config { get; private set; } = new();
        public List<TargetProcessGroup> AutoDetectedGroups { get; private set; } = new();
        public List<CustomTargetItem> ActiveCustomTargets { get; private set; } = new();
        public List<SearchableAppItem> CachedDiscoveredApps { get; private set; } = new();
        public List<RamLimiterLogEntry> ActionLogs { get; private set; } = new();

        public double SystemTotalRamMb { get; private set; }
        public double SystemUsedRamMb { get; private set; }
        public double SystemRamUsagePercent => SystemTotalRamMb > 0 ? (SystemUsedRamMb / SystemTotalRamMb) * 100.0 : 0.0;

        public MemoryPressureLevel PressureLevel
        {
            get
            {
                double pct = SystemRamUsagePercent;
                if (pct >= 90) return MemoryPressureLevel.Critical;
                if (pct >= 75) return MemoryPressureLevel.High;
                if (pct >= 60) return MemoryPressureLevel.Elevated;
                return MemoryPressureLevel.Normal;
            }
        }

        public string PressureLevelText => PressureLevel switch
        {
            MemoryPressureLevel.Critical => "CRITICAL",
            MemoryPressureLevel.High => "HIGH",
            MemoryPressureLevel.Elevated => "ELEVATED",
            _ => "NORMAL"
        };

        public string PressureLevelBrush => PressureLevel switch
        {
            MemoryPressureLevel.Critical => "#EF4444",
            MemoryPressureLevel.High => "#F97316",
            MemoryPressureLevel.Elevated => "#F59E0B",
            _ => "#10B981"
        };

        public int AutoActionCount { get; private set; } = 0;

        public event Action? TelemetryUpdated;

        private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass",
            "fontdrvhost", "svchost", "dwm", "sihost", "explorer", "taskhostw",
            "RuntimeBroker", "SearchHost", "StartMenuExperienceHost", "ErrorOptimizer",
            "BiosOptimizer.Service"
        };

        private static readonly List<(string Name, string Exe, double AutoMb, double LightMb, double AggressiveMb)> KnownApps = new()
        {
            ("Google Chrome", "chrome", 2500, 3200, 1500),
            ("Microsoft Edge", "msedge", 2000, 2600, 1200),
            ("Mozilla Firefox", "firefox", 2000, 2600, 1200),
            ("Discord", "Discord", 1200, 1600, 800),
            ("Spotify", "Spotify", 800, 1100, 500),
            ("BlueStacks", "HD-Player", 3500, 4200, 2400),
            ("BlueStacks Service", "BlueStacks", 2000, 2600, 1400),
            ("Yuzu Emulator", "yuzu", 4000, 5000, 2800),
            ("RPCS3 Emulator", "rpcs3", 4000, 5000, 2800),
            ("Steam Client", "steam", 1000, 1300, 600),
            ("Epic Games Launcher", "EpicGamesLauncher", 1200, 1500, 700),
            ("Slack", "slack", 1200, 1500, 800),
            ("Brave Browser", "brave", 2000, 2600, 1200),
            ("Opera Browser", "opera", 2000, 2600, 1200),
            ("Adobe Photoshop", "Photoshop", 4000, 5500, 2500),
            ("Visual Studio Code", "Code", 1500, 2000, 900)
        };

        public RamLimiterEngine()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "ErrorOptimizer");
            Directory.CreateDirectory(dir);
            _configFilePath = Path.Combine(dir, "ram_limiter_config.json");

            LoadConfiguration();
            DetectSystemRam();
            BuildDiscoveredAppsCache();

            // Automatic startup if enabled in persistent configuration
            if (Config.IsEnabled)
            {
                Start();
            }
        }

        private void DetectSystemRam()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus) && memStatus.ullTotalPhys > 0)
                {
                    SystemTotalRamMb = memStatus.ullTotalPhys / (1024.0 * 1024.0);
                    SystemUsedRamMb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0);
                }
                else
                {
                    var gcInfo = GC.GetGCMemoryInfo();
                    double totalMb = gcInfo.TotalAvailableMemoryBytes > 0 ? (gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0)) : 8192;
                    SystemTotalRamMb = totalMb;
                    SystemUsedRamMb = totalMb * 0.5;
                }
            }
            catch
            {
                var gcInfo = GC.GetGCMemoryInfo();
                double totalMb = gcInfo.TotalAvailableMemoryBytes > 0 ? (gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0)) : 8192;
                SystemTotalRamMb = totalMb;
                SystemUsedRamMb = totalMb * 0.5;
            }
        }

        public void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var cfg = JsonSerializer.Deserialize<RamLimiterConfig>(json);
                    if (cfg != null)
                    {
                        Config = cfg;
                        ActiveCustomTargets = Config.CustomTargets ?? new List<CustomTargetItem>();
                        return;
                    }
                }
            }
            catch { }

            Config = new RamLimiterConfig();
            ActiveCustomTargets = new List<CustomTargetItem>();
        }

        public void SaveConfiguration()
        {
            try
            {
                Config.CustomTargets = ActiveCustomTargets;
                var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_monitorTask != null && !_monitorTask.IsCompleted) return;

                Config.IsEnabled = true;
                SaveConfiguration();

                _cts = new CancellationTokenSource();
                _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                Config.IsEnabled = false;
                SaveConfiguration();

                _cts?.Cancel();
                _cts = null;
                _monitorTask = null;
            }
        }

        public void SetViewMode(RamLimiterViewMode mode)
        {
            Config.ViewMode = mode;
            SaveConfiguration();
        }

        public void SetNormalMode(RamLimiterMode mode)
        {
            Config.NormalMode = mode;
            SaveConfiguration();
        }

        public void SetForegroundProtection(bool enable)
        {
            Config.ForegroundProtection = enable;
            SaveConfiguration();
        }

        public void AddOrUpdateCustomTarget(CustomTargetItem target)
        {
            ActiveCustomTargets.RemoveAll(t => t.ExecutableName.Equals(target.ExecutableName, StringComparison.OrdinalIgnoreCase));
            ActiveCustomTargets.Add(target);
            SaveConfiguration();
            ScanAndEnforce();
        }

        public void RemoveCustomTarget(string id)
        {
            ActiveCustomTargets.RemoveAll(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || t.ExecutableName.Equals(id, StringComparison.OrdinalIgnoreCase));
            SaveConfiguration();
            ScanAndEnforce();
        }

        public void ToggleCustomTarget(string id)
        {
            var target = ActiveCustomTargets.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || t.ExecutableName.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.IsEnabled = !target.IsEnabled;
                SaveConfiguration();
                ScanAndEnforce();
            }
        }

        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    ScanAndEnforce();
                    TelemetryUpdated?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RamLimiter] Error in monitor loop: {ex.Message}");
                }

                try
                {
                    await Task.Delay(2500, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public (string appName, string exeName) ResolveProcessIdentity(Process process)
        {
            string exeName = process.ProcessName;
            string exePath = WorkloadOptimizationEngine.GetProcessExecutablePath(process);
            string lowPath = exePath.Replace('/', '\\').ToLowerInvariant();
            string lowExe = exeName.ToLowerInvariant();

            if (lowExe == "hd-player" || lowPath.Contains("hd-player"))
            {
                if (lowPath.Contains(@"\bluestacks_msi5\") || lowPath.Contains(@"\msi5\"))
                {
                    return ("MSI App Player / MSI 5", "HD-Player");
                }
                if (lowPath.Contains(@"\bluestacks_x_msi\"))
                {
                    return ("MSI App Player X", "HD-Player");
                }
                if (lowPath.Contains(@"\bluestacks_nxt\") || lowPath.Contains(@"\bluestacks5\"))
                {
                    return ("BlueStacks 5", "HD-Player");
                }
                if (lowPath.Contains(@"\bluestacks\"))
                {
                    return ("BlueStacks 4", "HD-Player");
                }

                // Fallback for custom path: check FileVersionInfo metadata
                try
                {
                    if (File.Exists(exePath))
                    {
                        var vi = FileVersionInfo.GetVersionInfo(exePath);
                        string prod = $"{vi.ProductName} {vi.CompanyName}".ToLowerInvariant();
                        if (prod.Contains("msi")) return ("MSI App Player / MSI 5", "HD-Player");
                    }
                }
                catch { }

                return ("BlueStacks 5", "HD-Player");
            }

            if (lowExe == "bluestacks")
            {
                if (lowPath.Contains(@"\bluestacks_msi5\") || lowPath.Contains(@"\msi5\"))
                {
                    return ("MSI App Player / MSI 5", "BlueStacks");
                }
                return ("BlueStacks 4", "BlueStacks");
            }

            var known = KnownApps.FirstOrDefault(k => k.Exe.Equals(exeName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(known.Exe))
            {
                return (known.Name, known.Exe);
            }

            return (exeName, exeName);
        }

        public void ScanAndEnforce()
        {
            DetectSystemRam();
            uint foregroundPid = GetForegroundProcessId();

            var processes = Process.GetProcesses();
            var runningByApp = new Dictionary<string, (string appName, string exeName, List<Process> procs)>(StringComparer.OrdinalIgnoreCase);
            var runningByName = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in processes)
            {
                try
                {
                    if (p.Id <= 4) continue;
                    string name = p.ProcessName;
                    if (ExcludedProcesses.Contains(name)) continue;

                    if (!runningByName.ContainsKey(name))
                        runningByName[name] = new List<Process>();
                    runningByName[name].Add(p);

                    var (appName, exeName) = ResolveProcessIdentity(p);
                    if (!runningByApp.TryGetValue(appName, out var entry))
                    {
                        entry = (appName, exeName, new List<Process>());
                        runningByApp[appName] = entry;
                    }
                    entry.procs.Add(p);
                }
                catch { }
            }

            // ── 1. NORMAL VIEW: Auto-Detect & Control All High-RAM Apps ──
            var normalGroups = new List<TargetProcessGroup>();

            foreach (var kvp in runningByApp)
            {
                var (appName, exeName, procs) = kvp.Value;
                if (procs.Count == 0) continue;

                double defaultAuto = 1500;
                double defaultLight = 2000;
                double defaultAggressive = 1000;

                var known = KnownApps.FirstOrDefault(k => k.Exe.Equals(exeName, StringComparison.OrdinalIgnoreCase) || k.Name.Equals(appName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(known.Exe))
                {
                    defaultAuto = known.AutoMb;
                    defaultLight = known.LightMb;
                    defaultAggressive = known.AggressiveMb;
                }
                else if (appName.Contains("MSI App Player", StringComparison.OrdinalIgnoreCase) || appName.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase))
                {
                    defaultAuto = 3500;
                    defaultLight = 4200;
                    defaultAggressive = 2400;
                }

                double limit = Config.NormalMode switch
                {
                    RamLimiterMode.Light => defaultLight,
                    RamLimiterMode.Aggressive => defaultAggressive,
                    _ => defaultAuto
                };

                double ramSum = 0;
                foreach (var p in procs)
                {
                    try { ramSum += p.WorkingSet64 / (1024.0 * 1024.0); } catch { }
                }

                // Surface if known, or emulator, or consuming > 200 MB
                if (!string.IsNullOrEmpty(known.Exe) || exeName.Equals("HD-Player", StringComparison.OrdinalIgnoreCase) || ramSum >= 200)
                {
                    var grp = CreateProcessGroup(appName, exeName, procs, limit, foregroundPid);
                    normalGroups.Add(grp);

                    if (Config.IsEnabled && Config.ViewMode == RamLimiterViewMode.Normal)
                    {
                        EvaluateAndEnforceNormal(grp, procs);
                    }
                }
            }

            AutoDetectedGroups = normalGroups.OrderByDescending(g => g.CurrentRamMb).ToList();

            // ── 2. CUSTOM VIEW: Monitor and Enforce ONLY Explicit Custom Targets ──
            foreach (var custom in ActiveCustomTargets)
            {
                var matchingProcs = new List<Process>();
                if (!string.IsNullOrEmpty(custom.ExecutablePath))
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (p.Id <= 4) continue;
                            string pPath = WorkloadOptimizationEngine.GetProcessExecutablePath(p);
                            if (pPath.Equals(custom.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                            {
                                matchingProcs.Add(p);
                            }
                        }
                        catch { }
                    }
                }
                
                if (matchingProcs.Count == 0 && runningByName.TryGetValue(custom.ExecutableName, out var byNameProcs))
                {
                    matchingProcs = byNameProcs;
                }

                if (matchingProcs.Count > 0)
                {
                    double totalRamMb = 0;
                    bool isForeground = false;
                    foreach (var p in matchingProcs)
                    {
                        try
                        {
                            totalRamMb += p.WorkingSet64 / (1024.0 * 1024.0);
                            if (p.Id == foregroundPid) isForeground = true;
                        }
                        catch { }
                    }

                    custom.CurrentRamMb = Math.Round(totalRamMb, 1);
                    custom.ProcessCount = matchingProcs.Count;
                    custom.IsForeground = isForeground;

                    if (!custom.IsEnabled)
                    {
                        custom.Status = RamAppStatus.Disabled;
                        continue;
                    }

                    bool isProtected = custom.ForegroundProtection && isForeground;
                    if (isProtected)
                    {
                        custom.Status = RamAppStatus.Protected;
                    }
                    else if (custom.CurrentRamMb > custom.RamLimitMb)
                    {
                        custom.Status = RamAppStatus.HighRam;

                        if (Config.IsEnabled && (Config.ViewMode == RamLimiterViewMode.Custom || Config.ViewMode == RamLimiterViewMode.Normal))
                        {
                            EnforceCustomTarget(custom, matchingProcs);
                        }
                    }
                    else
                    {
                        custom.Status = RamAppStatus.Active;
                    }
                }
                else
                {
                    custom.CurrentRamMb = 0;
                    custom.ProcessCount = 0;
                    custom.Status = custom.IsEnabled ? RamAppStatus.Active : RamAppStatus.Disabled;
                }
            }
        }

        private TargetProcessGroup CreateProcessGroup(string appName, string exeName, List<Process> procs, double limitMb, uint foregroundPid)
        {
            double totalRamMb = 0;
            bool isForeground = false;
            var pids = new List<int>();
            int primaryPid = procs[0].Id;

            foreach (var p in procs)
            {
                try
                {
                    pids.Add(p.Id);
                    totalRamMb += p.WorkingSet64 / (1024.0 * 1024.0);
                    if (p.Id == foregroundPid) isForeground = true;
                }
                catch { }
            }

            totalRamMb = Math.Round(totalRamMb, 1);
            var group = new TargetProcessGroup
            {
                ApplicationName = appName,
                ExecutableName = exeName,
                PrimaryPid = primaryPid,
                ProcessIds = pids,
                CurrentRamMb = totalRamMb,
                RecommendedLimitMb = limitMb,
                IsForeground = isForeground
            };

            bool isProtected = Config.ForegroundProtection && isForeground;
            if (isProtected)
            {
                group.Status = RamAppStatus.Protected;
            }
            else if (group.CurrentRamMb > group.RecommendedLimitMb)
            {
                group.Status = RamAppStatus.HighRam;
            }
            else if (group.CurrentRamMb > (group.RecommendedLimitMb * 0.85))
            {
                group.Status = RamAppStatus.NearLimit;
            }
            else
            {
                group.Status = RamAppStatus.Normal;
            }

            return group;
        }

        private void EvaluateAndEnforceNormal(TargetProcessGroup group, List<Process> processes)
        {
            if (group.IsForeground && Config.ForegroundProtection) return;

            // 1. Query Current Workload from UserWorkloadDetector
            var currentWorkload = UserWorkloadDetector.Instance.CurrentClassification;

            // 2. Workload-Specific Protection Policies
            if (currentWorkload.Category == ExtendedWorkloadCategory.Gaming)
            {
                // In Gaming mode, never trim game processes or audio/input background helpers
                if (group.ExecutableName.Equals(currentWorkload.PrimaryProcessName, StringComparison.OrdinalIgnoreCase) ||
                    group.ApplicationName.Contains("Game", StringComparison.OrdinalIgnoreCase))
                {
                    group.Status = RamAppStatus.Protected;
                    return;
                }
            }
            else if (currentWorkload.Category == ExtendedWorkloadCategory.CreativeRendering || currentWorkload.Category == ExtendedWorkloadCategory.CreativeEditing)
            {
                // In Creative mode, protect renderers and media editors from trimming
                if (group.ExecutableName.Equals(currentWorkload.PrimaryProcessName, StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Contains("AfterFX", StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Contains("blender", StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Contains("Resolve", StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Contains("Premiere", StringComparison.OrdinalIgnoreCase))
                {
                    group.Status = RamAppStatus.Protected;
                    return;
                }
            }
            else if (currentWorkload.Category == ExtendedWorkloadCategory.Compilation)
            {
                // Protect compilers & build systems
                if (group.ExecutableName.Equals(currentWorkload.PrimaryProcessName, StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Equals("MSBuild", StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    group.Status = RamAppStatus.Protected;
                    return;
                }
            }
            else if (currentWorkload.Category == ExtendedWorkloadCategory.Virtualization)
            {
                // Protect VMs and WSL2 memory pools
                if (group.ExecutableName.Contains("vmware", StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Contains("virtualbox", StringComparison.OrdinalIgnoreCase) ||
                    group.ExecutableName.Equals("vmmem", StringComparison.OrdinalIgnoreCase))
                {
                    group.Status = RamAppStatus.Protected;
                    return;
                }
            }

            // 3. Hardware-Aware Adaptive Pressure Thresholds
            var hw = HardwareProfiler.GetQuickProfile(forceRefresh: false);
            double adaptiveThresholdPct = hw.RamTotalGb switch
            {
                <= 8.0 => 75.0,
                <= 16.0 => 85.0,
                _ => 90.0
            };

            if (hw.IsBatteryPowered)
            {
                adaptiveThresholdPct = Math.Min(95.0, adaptiveThresholdPct + 5.0); // Conservative on battery
            }

            bool isPressureHigh = SystemRamUsagePercent >= adaptiveThresholdPct;

            bool shouldAct = Config.NormalMode switch
            {
                RamLimiterMode.Aggressive => group.CurrentRamMb > group.RecommendedLimitMb,
                RamLimiterMode.Light => group.CurrentRamMb > group.RecommendedLimitMb && isPressureHigh,
                _ => isPressureHigh || group.CurrentRamMb > (group.RecommendedLimitMb * 1.25)
            };

            if (shouldAct && group.CurrentRamMb > group.RecommendedLimitMb)
            {
                EnforceMemoryControl(group.ApplicationName, group.CurrentRamMb, processes, Config.NormalMode.ToString().ToUpper(), (after) =>
                {
                    group.CurrentRamMb = after;
                    group.Status = RamAppStatus.Trimmed;
                    group.LastAction = $"Auto Trimmed to {after:F0}MB";
                    group.LastActionTime = DateTime.Now;
                });
            }
        }

        private void EnforceCustomTarget(CustomTargetItem custom, List<Process> processes)
        {
            if (custom.IsForeground && custom.ForegroundProtection) return;

            if ((DateTime.Now - custom.LastActionTime).TotalSeconds < 15) return;

            EnforceMemoryControl(custom.DisplayName, custom.CurrentRamMb, processes, "CUSTOM", (after) =>
            {
                custom.CurrentRamMb = after;
                custom.Status = RamAppStatus.Trimmed;
                custom.LastActionTime = DateTime.Now;
            });
        }

        private void EnforceMemoryControl(string appName, double beforeRam, List<Process> processes, string policy, Action<double> onVerified)
        {
            int trimmedCount = 0;
            foreach (var p in processes)
            {
                try
                {
                    if (p.HasExited) continue;
                    if (EmptyWorkingSet(p.Handle)) trimmedCount++;
                }
                catch { }
            }

            if (trimmedCount > 0)
            {
                double afterRam = 0;
                foreach (var p in processes)
                {
                    try { if (!p.HasExited) { p.Refresh(); afterRam += p.WorkingSet64 / (1024.0 * 1024.0); } } catch { }
                }
                afterRam = Math.Round(afterRam, 1);
                onVerified(afterRam);
                AutoActionCount++;

                var log = new RamLimiterLogEntry
                {
                    Timestamp = DateTime.Now,
                    ApplicationName = appName,
                    BeforeRamMb = beforeRam,
                    Policy = policy,
                    ActionTaken = "Working-Set Trim",
                    AfterRamMb = afterRam,
                    Verification = "Verified",
                    Status = "TRIMMED"
                };

                lock (_lock)
                {
                    ActionLogs.Insert(0, log);
                    if (ActionLogs.Count > 50) ActionLogs.RemoveAt(ActionLogs.Count - 1);
                }
            }
        }

        public void BuildDiscoveredAppsCache()
        {
            var list = new List<SearchableAppItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Add known verified emulators
            if (Directory.Exists(@"C:\Program Files\BlueStacks_msi5") || File.Exists(@"C:\Program Files\BlueStacks_msi5\HD-Player.exe"))
            {
                seen.Add("MSI App Player / MSI 5");
                list.Add(new SearchableAppItem
                {
                    DisplayName = "MSI App Player / MSI 5",
                    ExecutableName = "HD-Player",
                    ExecutablePath = @"C:\Program Files\BlueStacks_msi5\HD-Player.exe"
                });
            }

            if (Directory.Exists(@"C:\Program Files\BlueStacks_nxt") || File.Exists(@"C:\Program Files\BlueStacks_nxt\HD-Player.exe"))
            {
                seen.Add("BlueStacks 5");
                list.Add(new SearchableAppItem
                {
                    DisplayName = "BlueStacks 5",
                    ExecutableName = "HD-Player",
                    ExecutablePath = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe"
                });
            }

            // 2. Add known applications
            foreach (var app in KnownApps)
            {
                if (app.Exe.Equals("HD-Player", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Contains(app.Name))
                {
                    seen.Add(app.Name);
                    list.Add(new SearchableAppItem
                    {
                        DisplayName = app.Name,
                        ExecutableName = app.Exe,
                        ExecutablePath = $@"C:\Program Files\{app.Name}\{app.Exe}.exe"
                    });
                }
            }

            // 3. Discover running processes
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id <= 4 || ExcludedProcesses.Contains(p.ProcessName)) continue;
                        var (appName, exeName) = ResolveProcessIdentity(p);
                        if (!seen.Contains(appName))
                        {
                            seen.Add(appName);
                            string path = WorkloadOptimizationEngine.GetProcessExecutablePath(p);
                            if (string.IsNullOrEmpty(path)) path = $@"{exeName}.exe";

                            list.Add(new SearchableAppItem
                            {
                                DisplayName = appName,
                                ExecutableName = exeName,
                                ExecutablePath = path,
                                IsRunning = true,
                                CurrentRamMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1)
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }

            CachedDiscoveredApps = list;
        }

        public List<SearchableAppItem> SearchApplications(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return CachedDiscoveredApps.Take(15).ToList();

            string q = query.Trim();
            return CachedDiscoveredApps
                .Where(a => a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            a.ExecutableName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            a.ExecutablePath.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(25)
                .ToList();
        }

        public static uint GetForegroundProcessId()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return 0;
                GetWindowThreadProcessId(hwnd, out uint pid);
                return pid;
            }
            catch { return 0; }
        }

        #region Native Win32
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
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

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }
        #endregion
    }
}
