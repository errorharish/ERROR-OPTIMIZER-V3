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

        // Pass 1: Gather initial info
        var activeWindowPids = GetPidsWithVisibleWindows();
        
        foreach (var p in processes)
        {
            if (p.Id == 0 || p.Id == 4) continue; // Idle, System

            var cand = new ProcessCandidate
            {
                ProcessId = p.Id,
                ProcessName = p.ProcessName
            };
            
            try { cand.ProcessPath = p.MainModule?.FileName ?? ""; } catch { }

            // Category classification
            cand.Category = ClassifyProcess(cand.ProcessName, cand.ProcessPath);
            cand.HasVisibleWindow = activeWindowPids.Contains(p.Id);

            if (cand.HasVisibleWindow && cand.Category != ProcessCategory.SystemCritical && cand.Category != ProcessCategory.OsRequired)
            {
                cand.Category = ProcessCategory.ActiveApp;
            }

            candidates.Add(cand);
        }

        // Pass 2: CPU check over 2 seconds (only for those we might actually kill to save time)
        var potentialKills = candidates.Where(c => c.Category == ProcessCategory.SafeToKill).ToList();
        
        var cpuSnap1 = GetCpuTimes(potentialKills.Select(p => p.ProcessId));
        await Task.Delay(2000);
        var cpuSnap2 = GetCpuTimes(potentialKills.Select(p => p.ProcessId));

        foreach (var p in potentialKills)
        {
            if (cpuSnap1.TryGetValue(p.ProcessId, out var t1) && cpuSnap2.TryGetValue(p.ProcessId, out var t2))
            {
                // Simple delta approximation. Real % depends on total system time delta, 
                // but checking if process time advanced significantly is enough.
                var processDeltaMs = (t2 - t1).TotalMilliseconds;
                var cpuUsageApprox = (processDeltaMs / 2000.0) * 100.0 / Environment.ProcessorCount;
                
                p.CpuUsagePercent = cpuUsageApprox;

                if (cpuUsageApprox > 2.0)
                {
                    p.Category = ProcessCategory.ActiveApp;
                    p.Reason = "Active CPU > 2%";
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

        foreach (var pid in processIdsToKill)
        {
            try
            {
                var p = Process.GetProcessById(pid);
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

        // Is it running as SYSTEM? We would check identity here, but we will rely on SafeToKill pattern matching for safety.

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
