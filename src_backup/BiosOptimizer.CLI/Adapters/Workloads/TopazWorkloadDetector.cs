using System.Diagnostics;
using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Workloads;

public class TopazWorkloadDetector : IWorkloadDetector
{
    public string WorkloadType => "Topaz Video AI";

    public InstalledWorkload? DetectInstallation()
    {
        var commonPaths = new[]
        {
            @"C:\Program Files\Topaz Labs LLC\Topaz Video AI\Topaz Video AI.exe",
            @"C:\Program Files\Topaz Labs LLC\Topaz Gigapixel AI\Topaz Gigapixel AI.exe"
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path);
                var isGigapixel = path.Contains("Gigapixel");
                
                return new InstalledWorkload
                {
                    WorkloadType = isGigapixel ? "Topaz Gigapixel AI" : "Topaz Video AI",
                    DisplayName = isGigapixel ? "Topaz Gigapixel AI" : "Topaz Video AI",
                    InstallationPath = dir ?? string.Empty,
                    ExecutablePath = path,
                    Detected = true
                };
            }
        }

        // Try registry
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key != null)
            {
                foreach (var subkeyName in key.GetSubKeyNames())
                {
                    using var subkey = key.OpenSubKey(subkeyName);
                    if (subkey != null)
                    {
                        var displayName = subkey.GetValue("DisplayName")?.ToString();
                        if (displayName != null && displayName.Contains("Topaz", StringComparison.OrdinalIgnoreCase))
                        {
                            var installLocation = subkey.GetValue("InstallLocation")?.ToString();
                            if (!string.IsNullOrEmpty(installLocation))
                            {
                                var exeName = displayName.Contains("Gigapixel") ? "Topaz Gigapixel AI.exe" : "Topaz Video AI.exe";
                                var exePath = Path.Combine(installLocation, exeName);
                                if (File.Exists(exePath))
                                {
                                    return new InstalledWorkload
                                    {
                                        WorkloadType = displayName.Contains("Gigapixel") ? "Topaz Gigapixel AI" : "Topaz Video AI",
                                        DisplayName = displayName,
                                        InstallationPath = installLocation,
                                        ExecutablePath = exePath,
                                        Version = subkey.GetValue("DisplayVersion")?.ToString() ?? "",
                                        Detected = true
                                    };
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public bool IsRunning()
    {
        var processes1 = Process.GetProcessesByName("Topaz Video AI");
        var processes2 = Process.GetProcessesByName("Topaz Gigapixel AI");
        return processes1.Length > 0 || processes2.Length > 0;
    }

    public string GetRunningProcessPath()
    {
        var processes1 = Process.GetProcessesByName("Topaz Video AI");
        if (processes1.Length > 0)
        {
            try { return processes1[0].MainModule?.FileName ?? string.Empty; } catch { }
        }
        
        var processes2 = Process.GetProcessesByName("Topaz Gigapixel AI");
        if (processes2.Length > 0)
        {
            try { return processes2[0].MainModule?.FileName ?? string.Empty; } catch { }
        }
        
        return string.Empty;
    }
}
