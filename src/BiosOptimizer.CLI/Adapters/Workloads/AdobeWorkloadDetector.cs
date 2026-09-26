using System.Diagnostics;
using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Workloads;

public class AdobeWorkloadDetector : IWorkloadDetector
{
    public string WorkloadType => "Adobe After Effects";

    public InstalledWorkload? DetectInstallation()
    {
        var commonPaths = new[]
        {
            @"C:\Program Files\Adobe\Adobe After Effects 2024\Support Files\AfterFX.exe",
            @"C:\Program Files\Adobe\Adobe After Effects 2023\Support Files\AfterFX.exe",
            @"C:\Program Files\Adobe\Adobe After Effects 2022\Support Files\AfterFX.exe"
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(Path.GetDirectoryName(path));
                var version = new DirectoryInfo(dir!).Name.Replace("Adobe After Effects ", "");
                
                return new InstalledWorkload
                {
                    WorkloadType = WorkloadType,
                    DisplayName = $"Adobe After Effects {version}",
                    InstallationPath = dir ?? string.Empty,
                    ExecutablePath = path,
                    Version = version,
                    Detected = true
                };
            }
        }

        // Try uninstall registry
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
                        if (displayName != null && displayName.Contains("Adobe After Effects", StringComparison.OrdinalIgnoreCase))
                        {
                            var installLocation = subkey.GetValue("InstallLocation")?.ToString();
                            if (!string.IsNullOrEmpty(installLocation))
                            {
                                var exePath = Path.Combine(installLocation, "Support Files", "AfterFX.exe");
                                if (File.Exists(exePath))
                                {
                                    return new InstalledWorkload
                                    {
                                        WorkloadType = WorkloadType,
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
        var processes = Process.GetProcessesByName("AfterFX");
        return processes.Length > 0;
    }

    public string GetRunningProcessPath()
    {
        var processes = Process.GetProcessesByName("AfterFX");
        if (processes.Length > 0)
        {
            try
            {
                return processes[0].MainModule?.FileName ?? string.Empty;
            }
            catch { return string.Empty; }
        }
        return string.Empty;
    }
}
