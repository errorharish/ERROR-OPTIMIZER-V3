#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public enum DetectedGpuVendor
    {
        Nvidia,
        Amd,
        Intel,
        Unknown
    }

    public class GpuDeviceSnapshot
    {
        public string Name { get; set; } = "Display Adapter";
        public DetectedGpuVendor Vendor { get; set; } = DetectedGpuVendor.Unknown;
        public string VendorName { get; set; } = "Unknown";
        public string DriverVersion { get; set; } = "Unknown";
        public string DriverDate { get; set; } = "Unknown";
        public string DeviceId { get; set; } = "";
        public string RegistryClassKey { get; set; } = "";
        public double VramGb { get; set; }
        public string WddmVersion { get; set; } = "WDDM 3.0+";
        public bool IsDiscrete { get; set; }
        public bool IsPrimary { get; set; }
        public bool SupportsHags { get; set; }
    }

    public class CpuHardwareSnapshot
    {
        public string Name { get; set; } = "Processor";
        public int LogicalProcessors { get; set; } = Environment.ProcessorCount;
        public int PhysicalCores { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
        public double BaseFrequencyGhz { get; set; }
        public string Architecture { get; set; } = "x64";
    }

    public class MemoryHardwareSnapshot
    {
        public double InstalledPhysicalGb { get; set; } = 8.0;
        public double UsablePhysicalGb { get; set; } = 8.0;
        public double AvailablePhysicalGb { get; set; } = 4.0;
        public double LoadPercentage { get; set; } = 50.0;
        public string InstalledFormatted => $"{InstalledPhysicalGb:F1} GB";
        public string UsableFormatted => $"{UsablePhysicalGb:F1} GB";
    }

    public class WindowsEnvironmentSnapshot
    {
        public string ProductName { get; set; } = "Windows 11";
        public string DisplayVersion { get; set; } = "";
        public string BuildNumber { get; set; } = "26100";
        public int Ubr { get; set; } = 0;
        public string Edition { get; set; } = "Home Single Language";
        public string Architecture { get; set; } = "64-bit";

        public string FullDisplayString
        {
            get
            {
                string buildStr = Ubr > 0 ? $"{BuildNumber}.{Ubr}" : BuildNumber;
                string verPart = !string.IsNullOrWhiteSpace(DisplayVersion) ? $"{DisplayVersion}, " : "";
                return $"{ProductName} ({verPart}Build {buildStr}) {Architecture}";
            }
        }
    }

    public class HardwareSnapshot
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string MachineName { get; set; } = Environment.MachineName;
        public string Manufacturer { get; set; } = "Unknown";
        public string Model { get; set; } = "Unknown";
        public string ComputerModelFormatted => $"{Manufacturer} {Model}".Trim();
        
        public CpuHardwareSnapshot Cpu { get; set; } = new();
        public MemoryHardwareSnapshot Memory { get; set; } = new();
        public List<GpuDeviceSnapshot> Gpus { get; set; } = new();
        public WindowsEnvironmentSnapshot Windows { get; set; } = new();
        
        public bool IsLaptop { get; set; }
        public bool IsOnBattery { get; set; }
        public int BatteryPercentage { get; set; } = 100;
        public string Motherboard { get; set; } = "";
        public string BiosVersion { get; set; } = "";

        public GpuDeviceSnapshot PrimaryGpu => Gpus.Find(g => g.IsPrimary) ?? (Gpus.Count > 0 ? Gpus[0] : new GpuDeviceSnapshot());
        public bool HasNvidia => Gpus.Exists(g => g.Vendor == DetectedGpuVendor.Nvidia);
        public bool HasAmd => Gpus.Exists(g => g.Vendor == DetectedGpuVendor.Amd);
        public bool HasIntel => Gpus.Exists(g => g.Vendor == DetectedGpuVendor.Intel);
        public bool IsHybridGpu => Gpus.Count > 1;
    }

    public interface IHardwareDetectionService
    {
        HardwareSnapshot GetSnapshot(bool forceRefresh = false);
        Task<HardwareSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken ct = default);
        void InvalidateCache();
        event Action<HardwareSnapshot>? HardwareChanged;
    }
}
