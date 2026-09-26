using System;
using System.Collections.Generic;
using System.Management;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Diagnostics;

public class HardwareDiagnostics
{
    public string CpuName { get; set; } = "Unknown";
    public int PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }
    public long RamTotalBytes { get; set; }
    public string GpuName { get; set; } = "Unknown";
    public long VramBytes { get; set; }
    public string GpuDriverVersion { get; set; } = "Unknown";
    public List<string> AllGpuNames { get; set; } = new List<string>();
    public string Motherboard { get; set; } = "Unknown";
    public string BiosVersion { get; set; } = "Unknown";
    public string WindowsVersion { get; set; } = "Unknown";
    public string HagsStatus { get; set; } = "Unknown";
    public string SecureBootStatus { get; set; } = "Unknown";
    public string TpmStatus { get; set; } = "Unknown";
    public string BitLockerStatus { get; set; } = "Unknown";
}

public class DiagnosticsEngine
{
    public HardwareDiagnostics GatherDiagnostics(EnvironmentContext context)
    {
        var diag = new HardwareDiagnostics
        {
            PhysicalCores = context.PhysicalCoreCount,
            LogicalProcessors = Environment.ProcessorCount,
            WindowsVersion = context.IsWindows11 ? "Windows 11" : context.IsWindows10 ? "Windows 10" : "Windows",
            RamTotalBytes = GetTotalRam(),
            CpuName = GetCpuName(),
            Motherboard = GetMotherboard(),
            BiosVersion = GetBiosVersion()
        };

        GetGpuInfo(diag);

        return diag;
    }

    private T RunWmiQuery<T>(Func<T> query, T defaultValue, int timeoutMs = 2000)
    {
        try
        {
            var task = System.Threading.Tasks.Task.Run(query);
            if (task.Wait(timeoutMs))
                return task.Result;
        }
        catch { }
        return defaultValue;
    }

    private void RunWmiAction(Action action, int timeoutMs = 2000)
    {
        try
        {
            var task = System.Threading.Tasks.Task.Run(action);
            task.Wait(timeoutMs);
        }
        catch { }
    }

    private string GetCpuName()
    {
        string? cpu = RunWmiQuery<string?>(() =>
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                return obj["Name"]?.ToString();
            }
            return null;
        }, null);

        if (string.IsNullOrEmpty(cpu))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                cpu = key?.GetValue("ProcessorNameString")?.ToString();
            }
            catch { }
        }
        
        return cpu ?? "Unknown CPU";
    }

    private long GetTotalRam()
    {
        long fallback = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return RunWmiQuery(() =>
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                if (long.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out var bytes))
                    return bytes;
            }
            return fallback;
        }, fallback);
    }

    private string GetMotherboard()
    {
        string? mb = RunWmiQuery<string?>(() =>
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                var mfg = obj["Manufacturer"]?.ToString();
                var prod = obj["Product"]?.ToString();
                if (!string.IsNullOrEmpty(prod)) return $"{mfg} {prod}".Trim();
            }
            return null;
        }, null);

        if (string.IsNullOrEmpty(mb))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                var mfg = key?.GetValue("BaseBoardManufacturer")?.ToString();
                var prod = key?.GetValue("BaseBoardProduct")?.ToString();
                if (!string.IsNullOrEmpty(prod)) mb = $"{mfg} {prod}".Trim();
            }
            catch { }
        }
        
        return mb ?? "Unknown Motherboard";
    }

    private string GetBiosVersion()
    {
        string? bios = RunWmiQuery<string?>(() =>
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (var obj in searcher.Get())
            {
                return obj["SMBIOSBIOSVersion"]?.ToString();
            }
            return null;
        }, null);

        if (string.IsNullOrEmpty(bios))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                bios = key?.GetValue("BIOSVersion")?.ToString();
            }
            catch { }
        }
        
        return bios ?? "Unknown";
    }

    private void GetGpuInfo(HardwareDiagnostics diag)
    {
        bool wmiSuccess = false;
        RunWmiAction(() =>
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                var driver = obj["DriverVersion"]?.ToString();
                var ramStr = obj["AdapterRAM"]?.ToString();
                long.TryParse(ramStr, out var vram);

                if (!string.IsNullOrEmpty(name))
                {
                    wmiSuccess = true;
                    diag.AllGpuNames.Add($"{name} (Driver: {driver})");
                    if (diag.GpuName == "Unknown" || (diag.GpuName.Contains("Intel") && !name.Contains("Intel")))
                    {
                        diag.GpuName = name;
                        diag.GpuDriverVersion = driver ?? "Unknown";
                        diag.VramBytes = vram;
                    }
                }
            }
        });

        if (!wmiSuccess)
        {
            // Fallback to registry
            try
            {
                using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey != null)
                {
                    foreach (var subKeyName in classKey.GetSubKeyNames())
                    {
                        if (subKeyName == "Properties") continue;
                        using var subKey = classKey.OpenSubKey(subKeyName);
                        var desc = subKey?.GetValue("DriverDesc")?.ToString();
                        var ver = subKey?.GetValue("DriverVersion")?.ToString();
                        if (!string.IsNullOrEmpty(desc))
                        {
                            diag.AllGpuNames.Add($"{desc} (Driver: {ver})");
                            if (diag.GpuName == "Unknown" || (diag.GpuName.Contains("Intel") && !desc.Contains("Intel")))
                            {
                                diag.GpuName = desc;
                                diag.GpuDriverVersion = ver ?? "Unknown";
                            }
                        }
                    }
                }
            }
            catch { }
        }

        if (diag.AllGpuNames.Count == 0)
            diag.AllGpuNames.Add("Unknown GPU");
    }
}
