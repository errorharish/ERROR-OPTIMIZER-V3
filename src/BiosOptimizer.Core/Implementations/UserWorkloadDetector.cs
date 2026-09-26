#pragma warning disable CA1416

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations
{
    public enum ExtendedWorkloadCategory
    {
        Idle,
        Gaming,
        CreativeRendering,
        CreativeEditing,
        VideoPlayback,
        WebBrowsing,
        OfficeProductivity,
        Compilation,
        Virtualization,
        FileTransfer,
        Unknown
    }

    public class WorkloadClassificationResult
    {
        public ExtendedWorkloadCategory Category { get; set; } = ExtendedWorkloadCategory.Idle;
        public string PrimaryProcessName { get; set; } = string.Empty;
        public int PrimaryPid { get; set; }
        public string WindowTitle { get; set; } = string.Empty;
        public bool IsFullscreen { get; set; }
        public int ConfidenceScore { get; set; } // 0 to 100
        public double CpuUsagePercent { get; set; }
        public double RamUsagePercent { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class MachineWorkloadHistoryItem
    {
        public string ProcessName { get; set; } = string.Empty;
        public ExtendedWorkloadCategory Category { get; set; }
        public int ObservationCount { get; set; }
        public double AvgCpuUsage { get; set; }
        public double AvgRamMb { get; set; }
        public int SuccessCount { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    public class UserWorkloadDetector : IDisposable
    {
        private static readonly Lazy<UserWorkloadDetector> _instance = new(() => new UserWorkloadDetector());
        public static UserWorkloadDetector Instance => _instance.Value;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        #region Heuristic Process Categorization Dictionaries

        private static readonly HashSet<string> GamingProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "VALORANT", "VALORANT-Win64-Shipping", "cs2", "csgo", "Cyberpunk2077", "FortniteClient-Win64-Shipping",
            "GTA5", "Overwatch", "ApexLegends", "R5Apex", "LeagueClientUx", "League of Legends",
            "RetroArch", "rpcs3", "pcsx2-qt", "yuzu", "ryujinx", "HD-Player", "dnplayer", "Nox",
            "RobloxPlayerBeta", "GenshinImpact", "StarRail", "Minecraft.Windows", "javaw"
        };

        private static readonly HashSet<string> CreativeRenderingProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "blender", "3dsmax", "maya", "HandBrake", "ffmpeg", "Cinebench", "keyshot", "vray", "arnold"
        };

        private static readonly HashSet<string> CreativeEditingProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "AfterFX", "Adobe Premiere Pro", "premiere", "Resolve", "photoshop", "illustrator",
            "audition", "lightroom", "FL64", "Ableton Live 11 Suite", "Cubase12"
        };

        private static readonly HashSet<string> VideoPlaybackProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "vlc", "mpc-hc64", "mpc-be64", "potplayer64", "wmplayer", "Netflix"
        };

        private static readonly HashSet<string> WebBrowsingProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi"
        };

        private static readonly HashSet<string> OfficeProductivityProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "ONENOTE", "notepad++", "Code", "slack", "teams"
        };

        private static readonly HashSet<string> CompilationProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "devenv", "MSBuild", "cl", "gcc", "g++", "rustc", "cargo", "dotnet", "javac", "kotlinc", "ninja", "link"
        };

        private static readonly HashSet<string> VirtualizationProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "vmware", "vmware-vmx", "VirtualBox", "VirtualBoxVM", "wsl", "wslhost", "vmmem", "Docker Desktop"
        };

        private static readonly HashSet<string> FileTransferProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "7zFM", "7z", "WinRAR", "robocopy", "Totalcmd64", "TeraCopy"
        };

        #endregion

        private readonly CancellationTokenSource _cts = new();
        private Task? _monitorTask;
        private readonly object _lock = new();
        private readonly string _historyFilePath;
        private Dictionary<string, MachineWorkloadHistoryItem> _history = new(StringComparer.OrdinalIgnoreCase);

        private WorkloadClassificationResult _currentClassification = new();

        public WorkloadClassificationResult CurrentClassification
        {
            get { lock (_lock) return _currentClassification; }
        }

        public UserWorkloadDetector()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer");
            Directory.CreateDirectory(dir);
            _historyFilePath = Path.Combine(dir, "machine_workload_history.json");
            LoadHistory();
        }

        public void StartMonitoring(int intervalMs = 2000)
        {
            if (_monitorTask != null) return;
            var ct = _cts.Token;

            _monitorTask = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var res = ClassifyCurrentWorkload();
                        lock (_lock)
                        {
                            _currentClassification = res;
                        }
                        AdaptiveResourceGovernor.Instance.ReportSystemBusy(
                            res.Category != ExtendedWorkloadCategory.Idle && res.Category != ExtendedWorkloadCategory.OfficeProductivity,
                            res.Description);
                    }
                    catch { }

                    try { await Task.Delay(intervalMs, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }, ct);
        }

        public static (bool isBusy, string reason) EvaluateCurrentWorkload()
        {
            var res = Instance.ClassifyCurrentWorkload();
            bool busy = res.Category != ExtendedWorkloadCategory.Idle && res.Category != ExtendedWorkloadCategory.OfficeProductivity;
            return (busy, res.Description);
        }

        public WorkloadClassificationResult ClassifyCurrentWorkload()
        {
            var result = new WorkloadClassificationResult();
            var hwProfile = HardwareProfiler.GetQuickProfile(forceRefresh: false);
            result.RamUsagePercent = hwProfile.RamUsagePercent;

            // 1. Check Foreground Window & Process
            IntPtr fgHwnd = GetForegroundWindow();
            uint fgPid = 0;
            string fgProcessName = string.Empty;

            if (fgHwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fgHwnd, out fgPid);
                if (fgPid > 0)
                {
                    try
                    {
                        using var proc = Process.GetProcessById((int)fgPid);
                        fgProcessName = proc.ProcessName;
                        result.PrimaryProcessName = fgProcessName;
                        result.PrimaryPid = (int)fgPid;
                    }
                    catch { }
                }

                // Check Fullscreen
                result.IsFullscreen = IsWindowFullscreen(fgHwnd);
            }

            // 2. Classify by Category and Confidence
            if (!string.IsNullOrEmpty(fgProcessName))
            {
                if (GamingProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.Gaming;
                    result.ConfidenceScore = result.IsFullscreen ? 100 : 95;
                    result.Description = $"Active 3D Gaming: {fgProcessName}";
                }
                else if (CreativeRenderingProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.CreativeRendering;
                    result.ConfidenceScore = 95;
                    result.Description = $"3D Rendering & Export: {fgProcessName}";
                }
                else if (CreativeEditingProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.CreativeEditing;
                    result.ConfidenceScore = 95;
                    result.Description = $"Media Editing: {fgProcessName}";
                }
                else if (CompilationProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.Compilation;
                    result.ConfidenceScore = 90;
                    result.Description = $"Build & Compilation: {fgProcessName}";
                }
                else if (VirtualizationProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.Virtualization;
                    result.ConfidenceScore = 90;
                    result.Description = $"Virtual Machine / Container: {fgProcessName}";
                }
                else if (VideoPlaybackProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.VideoPlayback;
                    result.ConfidenceScore = 85;
                    result.Description = $"Video Playback: {fgProcessName}";
                }
                else if (WebBrowsingProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.WebBrowsing;
                    result.ConfidenceScore = 85;
                    result.Description = $"Web Browsing: {fgProcessName}";
                }
                else if (OfficeProductivityProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.OfficeProductivity;
                    result.ConfidenceScore = 80;
                    result.Description = $"Office / Productivity: {fgProcessName}";
                }
                else if (FileTransferProcesses.Contains(fgProcessName))
                {
                    result.Category = ExtendedWorkloadCategory.FileTransfer;
                    result.ConfidenceScore = 85;
                    result.Description = $"File Archiving / Transfer: {fgProcessName}";
                }
            }

            // 3. Fallback scan for heavy background workloads if foreground is unknown or general
            if (result.Category == ExtendedWorkloadCategory.Idle || result.Category == ExtendedWorkloadCategory.Unknown)
            {
                var allProcs = Process.GetProcesses();
                foreach (var p in allProcs)
                {
                    try
                    {
                        string pName = p.ProcessName;
                        if (GamingProcesses.Contains(pName))
                        {
                            result.Category = ExtendedWorkloadCategory.Gaming;
                            result.PrimaryProcessName = pName;
                            result.ConfidenceScore = 80;
                            result.Description = $"Background Game: {pName}";
                            break;
                        }
                        if (CreativeRenderingProcesses.Contains(pName))
                        {
                            result.Category = ExtendedWorkloadCategory.CreativeRendering;
                            result.PrimaryProcessName = pName;
                            result.ConfidenceScore = 85;
                            result.Description = $"Background Rendering: {pName}";
                            break;
                        }
                        if (CompilationProcesses.Contains(pName))
                        {
                            result.Category = ExtendedWorkloadCategory.Compilation;
                            result.PrimaryProcessName = pName;
                            result.ConfidenceScore = 85;
                            result.Description = $"Background Compilation: {pName}";
                            break;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }

            // 4. If nothing heavy, check if Idle or Memory Pressure
            if (result.Category == ExtendedWorkloadCategory.Idle || result.Category == ExtendedWorkloadCategory.Unknown)
            {
                if (result.RamUsagePercent >= 88.0)
                {
                    result.Description = $"High Memory Pressure ({result.RamUsagePercent:F0}% RAM used)";
                }
                else
                {
                    result.Category = ExtendedWorkloadCategory.Idle;
                    result.ConfidenceScore = 95;
                    result.Description = "System Idle (Low Resource Demand)";
                }
            }

            // Update Machine History
            if (!string.IsNullOrEmpty(result.PrimaryProcessName))
            {
                RecordHistory(result.PrimaryProcessName, result.Category);
            }

            return result;
        }

        private static bool IsWindowFullscreen(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            if (GetWindowRect(hWnd, out RECT rect))
            {
                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);
                return rect.Left <= 0 && rect.Top <= 0 && rect.Right >= screenWidth && rect.Bottom >= screenHeight;
            }
            return false;
        }

        #region Machine History Learning

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    var items = JsonSerializer.Deserialize<List<MachineWorkloadHistoryItem>>(json);
                    if (items != null)
                    {
                        lock (_lock)
                        {
                            _history = items.ToDictionary(i => i.ProcessName, i => i, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveHistory()
        {
            try
            {
                List<MachineWorkloadHistoryItem> items;
                lock (_lock)
                {
                    items = _history.Values.ToList();
                }
                var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFilePath, json);
            }
            catch { }
        }

        private void RecordHistory(string procName, ExtendedWorkloadCategory category)
        {
            lock (_lock)
            {
                if (!_history.TryGetValue(procName, out var item))
                {
                    item = new MachineWorkloadHistoryItem
                    {
                        ProcessName = procName,
                        Category = category,
                        ObservationCount = 1,
                        LastSeen = DateTime.UtcNow
                    };
                    _history[procName] = item;
                }
                else
                {
                    item.ObservationCount++;
                    item.LastSeen = DateTime.UtcNow;
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            SaveHistory();
        }
    }
}
