using System.Diagnostics;
using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Workloads;

public class EmulatorWorkloadDetector : IWorkloadDetector
{
    public string WorkloadType => "Emulator";

    public InstalledWorkload? DetectInstallation()
    {
        // Check for BlueStacks
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_nxt");
            if (key != null)
            {
                var installDir = key.GetValue("InstallDir")?.ToString();
                if (!string.IsNullOrEmpty(installDir) && File.Exists(Path.Combine(installDir, "HD-Player.exe")))
                {
                    return new InstalledWorkload
                    {
                        WorkloadType = WorkloadType,
                        DisplayName = "BlueStacks 5",
                        InstallationPath = installDir,
                        ExecutablePath = Path.Combine(installDir, "HD-Player.exe"),
                        Detected = true
                    };
                }
            }
        }
        catch { }

        // Check for MSI App Player
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MSI App Player");
            if (key != null)
            {
                var installDir = key.GetValue("InstallDir")?.ToString();
                if (!string.IsNullOrEmpty(installDir) && File.Exists(Path.Combine(installDir, "HD-Player.exe")))
                {
                    return new InstalledWorkload
                    {
                        WorkloadType = WorkloadType,
                        DisplayName = "MSI App Player",
                        InstallationPath = installDir,
                        ExecutablePath = Path.Combine(installDir, "HD-Player.exe"),
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
        var processes = Process.GetProcessesByName("HD-Player");
        return processes.Length > 0;
    }

    public string GetRunningProcessPath()
    {
        var processes = Process.GetProcessesByName("HD-Player");
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
