using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace BiosOptimizer.Core.Implementations.Storage
{
    public class StorageEngine
    {
        public List<StorageDriveInfo> GetDrives()
        {
            var result = new List<StorageDriveInfo>();
            var physicalDisks = GetPhysicalDiskWmiInfo();

            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

                foreach (var d in drives)
                {
                    try
                    {
                        var info = new StorageDriveInfo
                        {
                            Name = d.Name,
                            VolumeLabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? (d.Name.StartsWith(sysDrive, StringComparison.OrdinalIgnoreCase) ? "Windows" : "Local Disk") : d.VolumeLabel,
                            DriveFormat = d.DriveFormat,
                            DriveType = d.DriveType.ToString(),
                            TotalBytes = d.TotalSize,
                            FreeBytes = d.TotalFreeSpace,
                            IsReady = d.IsReady,
                            IsSystemDrive = d.Name.Equals(sysDrive, StringComparison.OrdinalIgnoreCase)
                        };

                        // Calculate Storage Pressure
                        info.StoragePressure = info.UsagePercentage switch
                        {
                            >= 95.0 => "CRITICAL",
                            >= 85.0 => "HIGH",
                            >= 70.0 => "MODERATE",
                            _ => "LOW"
                        };

                        // Associate with physical disk hardware if found
                        var matchedDisk = physicalDisks.FirstOrDefault(p => p.VolumeLetter.Equals(info.Letter, StringComparison.OrdinalIgnoreCase))
                                          ?? physicalDisks.FirstOrDefault();

                        if (matchedDisk != null)
                        {
                            info.Model = matchedDisk.Model;
                            info.InterfaceType = matchedDisk.InterfaceType;
                            info.MediaType = matchedDisk.MediaType;
                            info.SmartStatus = matchedDisk.SmartStatus;
                            info.TemperatureText = matchedDisk.TemperatureText;
                        }
                        else
                        {
                            info.Model = info.IsSystemDrive ? "NVMe SSD" : "Storage Drive";
                            info.InterfaceType = info.DriveType.Equals("Removable", StringComparison.OrdinalIgnoreCase) ? "USB" : "NVMe/SATA";
                            info.MediaType = "Solid State Drive";
                            info.SmartStatus = "OK";
                            info.TemperatureText = "NOT AVAILABLE";
                        }

                        // Determine Health
                        if (info.SmartStatus.Equals("PREDICT_FAILURE", StringComparison.OrdinalIgnoreCase) || info.UsagePercentage >= 98.0)
                        {
                            info.HealthStatus = "CRITICAL";
                        }
                        else if (info.UsagePercentage >= 90.0)
                        {
                            info.HealthStatus = "WARNING";
                        }
                        else if (info.SmartStatus.Equals("OK", StringComparison.OrdinalIgnoreCase))
                        {
                            info.HealthStatus = "EXCELLENT";
                        }
                        else
                        {
                            info.HealthStatus = "GOOD";
                        }

                        result.Add(info);
                    }
                    catch { }
                }
            }
            catch { }

            return result;
        }

        private class WmiDiskSummary
        {
            public string VolumeLetter { get; set; } = string.Empty;
            public string Model { get; set; } = "NVMe / SSD";
            public string InterfaceType { get; set; } = "NVMe";
            public string MediaType { get; set; } = "SSD";
            public string SmartStatus { get; set; } = "OK";
            public string TemperatureText { get; set; } = "NOT AVAILABLE";
        }

        private List<WmiDiskSummary> GetPhysicalDiskWmiInfo()
        {
            var list = new List<WmiDiskSummary>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT DeviceId, Model, InterfaceType, MediaType, Status FROM Win32_DiskDrive");
                foreach (var obj in searcher.Get())
                {
                    string model = obj["Model"]?.ToString() ?? "Disk Drive";
                    string ifType = obj["InterfaceType"]?.ToString() ?? "NVMe";
                    string media = obj["MediaType"]?.ToString() ?? "Fixed hard disk media";
                    string status = obj["Status"]?.ToString() ?? "OK";

                    string friendlyMedia = "SSD";
                    if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || ifType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                    {
                        friendlyMedia = "NVMe SSD";
                        ifType = "NVMe";
                    }
                    else if (ifType.Contains("USB", StringComparison.OrdinalIgnoreCase))
                    {
                        friendlyMedia = "USB Drive";
                        ifType = "USB";
                    }
                    else if (media.Contains("SSD", StringComparison.OrdinalIgnoreCase) || model.Contains("SSD", StringComparison.OrdinalIgnoreCase))
                    {
                        friendlyMedia = "SATA SSD";
                        ifType = "SATA";
                    }

                    list.Add(new WmiDiskSummary
                    {
                        VolumeLetter = "C:", // Default mapping
                        Model = model,
                        InterfaceType = ifType,
                        MediaType = friendlyMedia,
                        SmartStatus = status.Equals("OK", StringComparison.OrdinalIgnoreCase) ? "OK" : status,
                        TemperatureText = "NOT AVAILABLE"
                    });
                }
            }
            catch { }

            return list;
        }
    }
}
