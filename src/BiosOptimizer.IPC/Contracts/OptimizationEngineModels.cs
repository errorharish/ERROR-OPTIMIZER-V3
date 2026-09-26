using System.Collections.Generic;

namespace BiosOptimizer.IPC.Contracts
{
    public class MachineProfileDto
    {
        public string OsName { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string OsBuild { get; set; } = string.Empty;
        
        public string CpuVendor { get; set; } = string.Empty;
        public string CpuName { get; set; } = string.Empty;
        public int PhysicalCores { get; set; }
        public int LogicalProcessors { get; set; }
        public bool IsHybridArchitecture { get; set; }
        
        public string GpuVendor { get; set; } = string.Empty;
        public string GpuName { get; set; } = string.Empty;
        public long GpuVRamBytes { get; set; }
        
        public List<GpuDto> Gpus { get; set; } = new();
        
        public long TotalRamBytes { get; set; }
        public long AvailableRamBytes { get; set; }
        
        public bool IsLaptop { get; set; }
        public bool IsOnBattery { get; set; }
        public string PowerProfile { get; set; } = string.Empty;
        
        public bool HasWifi { get; set; }
        public bool HasBluetooth { get; set; }
        
        public string BiosVendor { get; set; } = string.Empty;
        public string BiosVersion { get; set; } = string.Empty;
        public bool IsSecureBootEnabled { get; set; }
        
        public List<string> ActiveNetworkAdapters { get; set; } = new();
        public List<StorageDriveDto> Drives { get; set; } = new();
    }

    public class GpuDto
    {
        public string Name { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public long VramBytes { get; set; }
    }

    public class StorageDriveDto
    {
        public string Letter { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty; // SSD, HDD
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public bool IsBootDrive { get; set; }
    }

    public class WorkloadProfileDto
    {
        public bool IsGamingDetected { get; set; }
        public bool IsCreativeDetected { get; set; }
        public bool IsEmulatorDetected { get; set; }
        
        public List<string> DetectedWorkloadProcesses { get; set; } = new();
    }
}
