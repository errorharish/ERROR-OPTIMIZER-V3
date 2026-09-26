using System.Diagnostics;
using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Workloads;

public class GameWorkloadDetector : IWorkloadDetector
{
    public string WorkloadType => "AAA Games";

    public InstalledWorkload? DetectInstallation()
    {
        // Simple Steam detection for demonstration. A robust implementation would parse libraryfolders.vdf
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key != null)
            {
                var installPath = key.GetValue("InstallPath")?.ToString();
                if (!string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(installPath, "steam.exe")))
                {
                    return new InstalledWorkload
                    {
                        WorkloadType = "Steam",
                        DisplayName = "Steam Client",
                        InstallationPath = installPath,
                        ExecutablePath = Path.Combine(installPath, "steam.exe"),
                        Detected = true
                    };
                }
            }
        }
        catch { }
        
        return null;
    }

    public bool IsRunning()
    {
        // For actual game detection, we'd need a robust list of game executables or to detect via Steam API.
        // For now, we'll return false or check if a known game is running if we have a list.
        return false;
    }

    public string GetRunningProcessPath()
    {
        return string.Empty;
    }
}
