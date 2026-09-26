namespace BiosOptimizer.Core.Models;

public enum DeviceState
{
    Available,
    Unavailable,
    Unknown
}

public enum LowEndClassification
{
    LowEnd,
    Standard,
    HighPerformance,
    Unknown
}

public class PerformanceHardwareProfile
{
    public string CPUName { get; set; } = string.Empty;
    public int PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }
    public long RAM { get; set; }
    public string GPU { get; set; } = string.Empty;
    public string GPUVendor { get; set; } = string.Empty;
    public long VRAM { get; set; }
    public string GPUDriver { get; set; } = string.Empty;
    public string StorageType { get; set; } = string.Empty;
    public string SystemDrive { get; set; } = string.Empty;
    public bool IsSSD { get; set; }
    public bool VirtualizationSupported { get; set; }
    public bool VirtualizationEnabled { get; set; }
    public bool HagsSupported { get; set; }
    public bool HagsEnabled { get; set; }
    public string PowerPlan { get; set; } = string.Empty;
    public bool BatteryPresent { get; set; }
    public bool IsLaptop { get; set; }
}

public class MachineProfile
{
    public string MachineType { get; set; } = "Unknown";
    public string Manufacturer { get; set; } = "Unknown";
    public string Model { get; set; } = "Unknown";
    public string SystemSku { get; set; } = "Unknown";
    public string BaseboardManufacturer { get; set; } = "Unknown";
    public string BaseboardModel { get; set; } = "Unknown";
    public string BiosVendor { get; set; } = "Unknown";
    public string BiosVersion { get; set; } = "Unknown";
    public string FirmwareType { get; set; } = "Unknown";
    public bool BiosInterfacesExposed { get; set; } = false;
    public string CpuModel { get; set; } = "Unknown";
    public int CpuCores { get; set; } = 0;
    public int CpuLogicalProcessors { get; set; } = 0;
    public List<GpuInfo> Gpus { get; set; } = new();
    public long TotalRamBytes { get; set; } = 0;
    public string SystemDriveType { get; set; } = "Unknown";
    public string WindowsVersion { get; set; } = "Unknown";
    public string ActivePowerPlan { get; set; } = "Unknown";
    public bool IsOnBattery { get; set; } = false;
    public int SupportedBiosTargets { get; set; } = 0;
    public string MemorySpeedAndType { get; set; } = "DDR5 / High Speed";
    public string SecureBootStatus { get; set; } = "Enabled (UEFI)";
    public string TpmStatus { get; set; } = "TPM 2.0 (Active)";
    public string VirtualizationStatus { get; set; } = "Enabled (Hardware Supported)";
    public string RebarStatus { get; set; } = "Supported (GPU Ready)";
    public string HagsStatus { get; set; } = "Hardware Accelerated";

}

public class EnvironmentContext
{
    public DeviceState HasPrinter { get; set; } = DeviceState.Unknown;
    public DeviceState HasBluetooth { get; set; } = DeviceState.Unknown;
    public DeviceState HasTouchscreen { get; set; } = DeviceState.Unknown;
    public DeviceState HasCamera { get; set; } = DeviceState.Unknown;
    public bool BitLockerActive { get; set; }
    public bool BitLockerSupported { get; set; }
    public bool HasBattery { get; set; }
    public bool IsLaptop { get; set; }
    public bool IsDesktop { get; set; }
    public bool IsDomainJoined { get; set; }
    public string DomainName { get; set; } = string.Empty;
    public long RamSizeGb { get; set; }
    public long RamSizeBytes { get; set; }
    public int PhysicalCoreCount { get; set; }
    public int LogicalCoreCount { get; set; }
    public string StorageType { get; set; } = string.Empty;
    public string GpuVendor { get; set; } = string.Empty;
    public string GpuName { get; set; } = string.Empty;
    public string GpuDriverVersion { get; set; } = string.Empty;
    public long VramBytes { get; set; }
    public List<GpuInfo> Gpus { get; set; } = new();
    public bool VirtualizationSupported { get; set; }
    public bool VirtualizationEnabled { get; set; }
    
    // Windows Context
    public bool IsWindows10 { get; set; }
    public bool IsWindows11 { get; set; }
    public string WindowsBuild { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    
    // BIOS Context
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SystemFamily { get; set; } = string.Empty;
    public string SystemSKU { get; set; } = string.Empty;
    public string BIOSVendor { get; set; } = string.Empty;
    public string BIOSVersion { get; set; } = string.Empty;
    public string BIOSReleaseDate { get; set; } = string.Empty;

    // CPU Context
    public string CpuName { get; set; } = string.Empty;
    public bool Avx2Supported { get; set; }

    // Workload Context
    public List<InstalledWorkload> Workloads { get; set; } = new();
    
    // Performance Context
    public LowEndClassification Classification { get; set; } = LowEndClassification.Unknown;
    public PerformanceHardwareProfile HardwareProfile { get; set; } = new();
    public MachineProfile MachineProfile { get; set; } = new();
}

public class InstalledWorkload
{
    public string WorkloadType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string InstallationPath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Detected { get; set; }
}

public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public long VramBytes { get; set; }
    public string DriverVersion { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}
