using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using System;
using System.Collections.Generic;
using System.Management;

namespace BiosOptimizer.Core.Implementations;

public class MachineProfileDetector : IMachineProfileDetector
{
    public MachineProfile DetectMachine()
    {
        var profile = new MachineProfile();
        
        try
        {
            // System Enclosure / Type
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SystemEnclosure"))
            {
                foreach (var obj in searcher.Get())
                {
                    if (obj["ChassisTypes"] != null)
                    {
                        var types = (ushort[])obj["ChassisTypes"];
                        if (types.Length > 0)
                        {
                            var type = types[0];
                            // 9=Laptop, 10=Notebook, 8=Portable, 14=Sub Notebook
                            if (type == 9 || type == 10 || type == 8 || type == 14) profile.MachineType = "Laptop";
                            else if (type == 3 || type == 4 || type == 6 || type == 7) profile.MachineType = "Desktop";
                            else if (type == 11 || type == 12 || type == 13) profile.MachineType = "HandHeld";
                        }
                    }
                }
            }

            // Computer System (Manufacturer, Model)
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemSKUNumber FROM Win32_ComputerSystem"))
            {
                foreach (var obj in searcher.Get())
                {
                    profile.Manufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown";
                    profile.Model = obj["Model"]?.ToString() ?? "Unknown";
                    profile.SystemSku = obj["SystemSKUNumber"]?.ToString() ?? "Unknown";
                }
            }

            // BIOS
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS"))
            {
                foreach (var obj in searcher.Get())
                {
                    profile.BiosVendor = obj["Manufacturer"]?.ToString() ?? "Unknown";
                    profile.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "Unknown";
                }
            }

            // Baseboard
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            {
                foreach (var obj in searcher.Get())
                {
                    profile.BaseboardManufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown";
                    profile.BaseboardModel = obj["Product"]?.ToString() ?? "Unknown";
                }
            }

            // CPU
            using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
            {
                foreach (var obj in searcher.Get())
                {
                    profile.CpuModel = obj["Name"]?.ToString() ?? "Unknown";
                    if (obj["NumberOfCores"] != null) profile.CpuCores = Convert.ToInt32(obj["NumberOfCores"]);
                    if (obj["NumberOfLogicalProcessors"] != null) profile.CpuLogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    break;
                }
            }

            // RAM
            using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
            {
                foreach (var obj in searcher.Get())
                {
                    if (obj["TotalVisibleMemorySize"] != null)
                    {
                        long kb = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                        profile.TotalRamBytes = kb * 1024;
                    }
                }
            }

            // GPU
            using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, DriverVersion FROM Win32_VideoController"))
            {
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "Unknown";
                    var vendor = obj["AdapterCompatibility"]?.ToString() ?? "Unknown";
                    
                    var isDiscrete = !name.Contains("Intel", StringComparison.OrdinalIgnoreCase) && 
                                     !name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase) &&
                                     (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase));

                    profile.Gpus.Add(new GpuInfo
                    {
                        Name = name,
                        Vendor = vendor,
                        DriverVersion = obj["DriverVersion"]?.ToString() ?? "Unknown"
                    });
                }
            }

            // Power (AC/Battery)
            using (var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery"))
            {
                var search = searcher.Get();
                if (search.Count > 0)
                {
                    foreach (var obj in search)
                    {
                        var status = Convert.ToInt32(obj["BatteryStatus"]);
                        profile.IsOnBattery = (status == 1); // 1 = discharging
                    }
                }
                else
                {
                    profile.IsOnBattery = false; // No battery = Desktop/AC
                }
            }
        }
        catch { }

        return profile;
    }
}
