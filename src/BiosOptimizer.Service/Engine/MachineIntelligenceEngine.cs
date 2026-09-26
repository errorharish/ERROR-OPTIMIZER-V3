using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using BiosOptimizer.IPC.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BiosOptimizer.Service.Engine
{
    public class MachineIntelligenceEngine
    {
        private readonly ILogger<MachineIntelligenceEngine> _logger;
        private MachineProfileDto? _cachedProfile;
        private WorkloadProfileDto? _cachedWorkload;
        private DateTime _lastProfileUpdate = DateTime.MinValue;
        private DateTime _lastWorkloadUpdate = DateTime.MinValue;

        public MachineIntelligenceEngine(ILogger<MachineIntelligenceEngine> logger)
        {
            _logger = logger;
        }

        public async Task<MachineProfileDto> GetMachineProfileAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedProfile != null && (DateTime.UtcNow - _lastProfileUpdate).TotalMinutes < 60)
            {
                return _cachedProfile;
            }

            var profile = new MachineProfileDto();

            await Task.Run(() =>
            {
                try
                {
                    // OS Info
                    using (var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            profile.OsName = obj["Caption"]?.ToString() ?? "Unknown";
                            profile.OsVersion = obj["Version"]?.ToString() ?? "Unknown";
                            profile.OsBuild = obj["BuildNumber"]?.ToString() ?? "Unknown";
                            break;
                        }
                    }

                    // CPU Info
                    using (var searcher = new ManagementObjectSearcher("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            profile.CpuName = obj["Name"]?.ToString() ?? "Unknown";
                            profile.CpuVendor = obj["Manufacturer"]?.ToString() ?? "Unknown";
                            if (int.TryParse(obj["NumberOfCores"]?.ToString(), out int cores)) profile.PhysicalCores = cores;
                            if (int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out int logical)) profile.LogicalProcessors = logical;
                            
                            // Simple hybrid detection for newer Intels
                            if (profile.CpuVendor.Contains("Intel") && profile.PhysicalCores > 0 && profile.LogicalProcessors > profile.PhysicalCores * 2)
                            {
                                profile.IsHybridArchitecture = true;
                            }
                            break;
                        }
                    }

                    // GPU Info
                    using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            var gpuName = obj["Name"]?.ToString() ?? "Unknown";
                            var ramObj = obj["AdapterRAM"];
                            long vram = 0;
                            if (ramObj != null && uint.TryParse(ramObj.ToString(), out uint ram))
                            {
                                vram = ram;
                            }
                            
                            // Detect vendor
                            string vendor = "Unknown";
                            if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) vendor = "NVIDIA";
                            else if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) vendor = "AMD";
                            else if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase)) vendor = "Intel";
                            
                            profile.Gpus.Add(new GpuDto { Name = gpuName, VramBytes = vram, Vendor = vendor });
                            
                            // Keep the first one as primary for backwards compatibility
                            if (string.IsNullOrEmpty(profile.GpuName))
                            {
                                profile.GpuName = gpuName;
                                profile.GpuVRamBytes = vram;
                                profile.GpuVendor = vendor;
                            }
                        }
                    }

                    // BIOS & Motherboard Info
                    using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            profile.BiosVendor = obj["Manufacturer"]?.ToString() ?? "Unknown";
                            profile.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "Unknown";
                            break;
                        }
                    }

                    // Laptop Detection
                    using (var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            var chassisTypes = (ushort[])obj["ChassisTypes"];
                            if (chassisTypes != null && chassisTypes.Length > 0)
                            {
                                // 8=Portable, 9=Laptop, 10=Notebook, 11=HandHeld, 12=DockingStation, 14=SubNotebook, 31=Convertible, 32=Detachable
                                int type = chassisTypes[0];
                                profile.IsLaptop = type == 8 || type == 9 || type == 10 || type == 14 || type == 31 || type == 32;
                            }
                            break;
                        }
                    }

                    // Network (WiFi/Bluetooth)
                    using (var searcher = new ManagementObjectSearcher("SELECT Name, NetConnectionStatus, AdapterType FROM Win32_NetworkAdapter"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            var name = obj["Name"]?.ToString() ?? "";
                            if (name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || name.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
                            {
                                profile.HasWifi = true;
                            }
                            if (name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                            {
                                profile.HasBluetooth = true;
                            }
                        }
                    }

                    // Total RAM
                    using (var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
                    {
                        foreach (var item in searcher.Get())
                        {
                            profile.TotalRamBytes = (long)(ulong.Parse(item["TotalVisibleMemorySize"]?.ToString() ?? "0") * 1024);
                            profile.AvailableRamBytes = (long)(ulong.Parse(item["FreePhysicalMemory"]?.ToString() ?? "0") * 1024);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fully build MachineProfile");
                }
            });

            _cachedProfile = profile;
            _lastProfileUpdate = DateTime.UtcNow;
            return profile;
        }

        public async Task<WorkloadProfileDto> GetWorkloadProfileAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedWorkload != null && (DateTime.UtcNow - _lastWorkloadUpdate).TotalMinutes < 5)
            {
                return _cachedWorkload;
            }

            var workload = new WorkloadProfileDto();

            await Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcesses();
                    var runningNames = new HashSet<string>(processes.Select(p => p.ProcessName.ToLowerInvariant()));

                    // Gaming detection
                    string[] gamingApps = { "steam", "epicgameslauncher", "upc", "origin", "battle.net", "riotclient" };
                    if (runningNames.Any(n => gamingApps.Contains(n)))
                    {
                        workload.IsGamingDetected = true;
                        workload.DetectedWorkloadProcesses.AddRange(gamingApps.Where(n => runningNames.Contains(n)));
                    }

                    // Creative detection
                    string[] creativeApps = { "photoshop", "premiere", "aftereffects", "illustrator", "davinciresolve" };
                    if (runningNames.Any(n => creativeApps.Contains(n)))
                    {
                        workload.IsCreativeDetected = true;
                        workload.DetectedWorkloadProcesses.AddRange(creativeApps.Where(n => runningNames.Contains(n)));
                    }

                    // Emulator detection
                    string[] emulators = { "bluestacks", "ldplayer", "nox", "memu" };
                    if (runningNames.Any(n => emulators.Contains(n)))
                    {
                        workload.IsEmulatorDetected = true;
                        workload.DetectedWorkloadProcesses.AddRange(emulators.Where(n => runningNames.Contains(n)));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to build WorkloadProfile");
                }
            });

            _cachedWorkload = workload;
            _lastWorkloadUpdate = DateTime.UtcNow;
            return workload;
        }
    }
}
