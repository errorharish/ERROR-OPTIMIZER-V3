using BiosOptimizer.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations;

public class ProcessReductionEngine : IProcessReductionEngine
{
    private static readonly HashSet<string> SystemCritical = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "wininit", "services", "smss", "lsass", "svchost", "System",
        "System Idle Process", "Secure System", "Memory Compression", "Registry",
        "securityhealthservice", "MsMpEng", "spoolsv", "LsaIso", "fontdrvhost"
    };

    private static readonly HashSet<string> OsRequired = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "rundll32", "taskhostw", "ctfmon", "RuntimeBroker",
        "ShellExperienceHost", "ApplicationFrameHost", "StartMenuExperienceHost",
        "searchapp", "SearchIndexer", "Widgets", "TextInputHost", "sihost",
        "dwm", "SearchHost", "winlogon"
    };

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "iexplore"
    };

    private static readonly HashSet<string> SafeToKillExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "OneDrive", "SkypeBackgroundHost", "Spotify", "Teams", "Discord",
        "Dropbox", "GoogleDrive", "AdobeIPCBroker", "AdobeARM",
        "GoogleUpdate", "FacebookUpdate", "ApplePushService", "iTunesHelper",
        "jusched", "EOSServer", "OVRServer_x64", "EpicGamesLauncher", "Steam",
        "Origin", "upc", "GalaxyClient"
    };

    private readonly IBackupManager _backupManager;

    public ProcessReductionEngine(IBackupManager backupManager)
    {
        _backupManager = backupManager;
    }

    public async Task<List<ProcessCandidate>> ScanProcessesAsync()
    {
        Console.WriteLine("[PROCESS REDUCTION] Enumerating Windows processes...");
        var candidates = new List<ProcessCandidate>();
        var processes = Process.GetProcesses();

        // Pass 1: Gather active window & foreground PIDs
        var activeWindowPids = GetPidsWithVisibleWindows();
        IntPtr fgHwnd = GetForegroundWindow();
        int fgPid = 0;
        if (fgHwnd != IntPtr.Zero)
        {
            GetWindowThreadProcessId(fgHwnd, out uint fPid);
            fgPid = (int)fPid;
        }
        int currentAppPid = Process.GetCurrentProcess().Id;
        string nowTime = DateTime.Now.ToString("HH:mm:ss");

        foreach (var p in processes)
        {
            if (p.Id == 0 || p.Id == 4) continue; // Idle, System

            var cand = new ProcessCandidate
            {
                ProcessId = p.Id,
                ProcessName = p.ProcessName,
                DisplayName = p.ProcessName,
                DetectionTime = nowTime
            };
            
            try { cand.ProcessPath = p.MainModule?.FileName ?? ""; } catch { }
            try { cand.RamUsageMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1); } catch { cand.RamUsageMb = 0; }

            // Extract Publisher and File Description if accessible
            if (!string.IsNullOrEmpty(cand.ProcessPath) && System.IO.File.Exists(cand.ProcessPath))
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(cand.ProcessPath);
                    cand.Publisher = info.CompanyName ?? "";
                    if (!string.IsNullOrWhiteSpace(info.FileDescription))
                    {
                        cand.DisplayName = info.FileDescription;
                    }
                }
                catch { }
            }

            cand.HasVisibleWindow = activeWindowPids.Contains(p.Id);
            cand.IsForeground = (p.Id == fgPid);

            // Strict Self & Foreground Protection
            if (p.Id == currentAppPid)
            {
                cand.Category = ProcessCategory.SystemCritical;
                cand.SafetyLevel = "PROTECTED";
                cand.Reason = "Error Optimizer application process";
                candidates.Add(cand);
                continue;
            }

            // Category classification
            cand.Category = ClassifyProcess(cand.ProcessName, cand.ProcessPath);

            if (cand.IsForeground)
            {
                cand.Category = ProcessCategory.ActiveApp;
            }
            else if (cand.HasVisibleWindow && cand.Category != ProcessCategory.SystemCritical && cand.Category != ProcessCategory.OsRequired)
            {
                cand.Category = ProcessCategory.ActiveApp;
            }

            // Assign Safety Level & Contextual Reason
            switch (cand.Category)
            {
                case ProcessCategory.SafeToKill:
                    cand.SafetyLevel = "SAFE TO TERMINATE";
                    if (cand.ProcessName.Contains("Update", StringComparison.OrdinalIgnoreCase))
                        cand.Reason = "Background updater — parent application is not active";
                    else if (cand.ProcessName.Contains("Helper", StringComparison.OrdinalIgnoreCase) || cand.ProcessName.Contains("Broker", StringComparison.OrdinalIgnoreCase))
                        cand.Reason = "Idle background helper service — no foreground interaction";
                    else if (cand.ProcessName.Contains("Tray", StringComparison.OrdinalIgnoreCase))
                        cand.Reason = "Background system tray monitor";
                    else if (cand.RamUsageMb > 150)
                        cand.Reason = $"High-memory background app ({cand.RamUsageMb:F0} MB) with no active window";
                    else
                        cand.Reason = "Non-essential background process — safe to terminate";
                    break;
                case ProcessCategory.ActiveApp:
                    cand.SafetyLevel = "CAUTION";
                    cand.Reason = cand.IsForeground 
                        ? "Active foreground application — currently in use" 
                        : "Active application with visible desktop window";
                    break;
                case ProcessCategory.Browser:
                    cand.SafetyLevel = "CAUTION";
                    cand.Reason = "Web browser process — may contain active tabs or user sessions";
                    break;
                case ProcessCategory.OsRequired:
                    cand.SafetyLevel = "PROTECTED";
                    cand.Reason = "Essential Windows OS component — required for desktop stability";
                    break;
                case ProcessCategory.SystemCritical:
                    cand.SafetyLevel = "SYSTEM";
                    cand.Reason = "Critical Windows kernel/system process — termination blocked";
                    break;
                default:
                    cand.SafetyLevel = "PROTECTED";
                    cand.Reason = "Unclassified system or background process";
                    break;
            }

            candidates.Add(cand);
        }

        // Pass 2: CPU check over 1.5 seconds for candidate processes
        var potentialKills = candidates.Where(c => c.Category == ProcessCategory.SafeToKill).Take(30).ToList();
        
        var cpuSnap1 = GetCpuTimes(potentialKills.Select(p => p.ProcessId));
        await Task.Delay(1500);
        var cpuSnap2 = GetCpuTimes(potentialKills.Select(p => p.ProcessId));

        foreach (var p in potentialKills)
        {
            if (cpuSnap1.TryGetValue(p.ProcessId, out var t1) && cpuSnap2.TryGetValue(p.ProcessId, out var t2))
            {
                var processDeltaMs = (t2 - t1).TotalMilliseconds;
                var cpuUsageApprox = (processDeltaMs / 1500.0) * 100.0 / Environment.ProcessorCount;
                
                p.CpuUsagePercent = Math.Round(cpuUsageApprox, 1);

                if (cpuUsageApprox > 3.0)
                {
                    p.Category = ProcessCategory.ActiveApp;
                    p.SafetyLevel = "CAUTION";
                    p.Reason = $"Active background CPU consumption ({p.CpuUsagePercent:F1}%)";
                }
            }
        }

        // Cleanup disposed
        foreach (var p in processes) p.Dispose();

        return candidates;
    }

    public int TerminateProcesses(IEnumerable<int> processIdsToKill)
    {
        int killed = 0;
        var killedPaths = new List<string>();
        int currentAppPid = Process.GetCurrentProcess().Id;

        foreach (var pid in processIdsToKill)
        {
            if (pid <= 4 || pid == currentAppPid) continue;

            try
            {
                var p = Process.GetProcessById(pid);
                if (p.HasExited) continue;

                // Re-validate that the PID belongs to an allowed process and not a reused critical system process
                if (SystemCritical.Contains(p.ProcessName) || OsRequired.Contains(p.ProcessName))
                {
                    p.Dispose();
                    continue; // Skip dangerous PID
                }

                var path = "";
                try { path = p.MainModule?.FileName ?? ""; } catch { }
                
                if (!string.IsNullOrEmpty(path)) killedPaths.Add(path);

                p.Kill();
                killed++;
                p.Dispose();
            }
            catch
            {
                // Access denied or already exited
            }
        }

        if (killedPaths.Count > 0)
        {
            _backupManager.LogKilledProcesses(killedPaths);
        }

        return killed;
    }

    private ProcessCategory ClassifyProcess(string name, string path)
    {
        if (SystemCritical.Contains(name)) return ProcessCategory.SystemCritical;
        if (OsRequired.Contains(name)) return ProcessCategory.OsRequired;
        if (Browsers.Contains(name)) return ProcessCategory.Browser;
        
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
            return ProcessCategory.SystemCritical;

        if (SafeToKillExact.Contains(name)) return ProcessCategory.SafeToKill;
        
        if (name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Helper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tray", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Background", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessCategory.SafeToKill;
        }

        return ProcessCategory.Unknown;
    }

    // --- Win32 P/Invoke for strict window checks ---

    private HashSet<int> GetPidsWithVisibleWindows()
    {
        var pids = new HashSet<int>();
        EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowVisible(hWnd))
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                pids.Add((int)pid);
            }
            return true;
        }, IntPtr.Zero);
        return pids;
    }

    private Dictionary<int, TimeSpan> GetCpuTimes(IEnumerable<int> pids)
    {
        var dict = new Dictionary<int, TimeSpan>();
        foreach (var pid in pids)
        {
            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    if (GetProcessTimes(hProcess, out _, out _, out var kernel, out var user))
                    {
                        long totalTicks = (((long)kernel.dwHighDateTime << 32) + kernel.dwLowDateTime) + 
                                          (((long)user.dwHighDateTime << 32) + user.dwLowDateTime);
                        dict[pid] = TimeSpan.FromTicks(totalTicks);
                    }
                    CloseHandle(hProcess);
                }
            }
            catch { }
        }
        return dict;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME lpCreationTime, out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
}
