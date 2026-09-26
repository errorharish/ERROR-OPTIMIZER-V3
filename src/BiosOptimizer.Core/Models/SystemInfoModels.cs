using System;
using System.Collections.Generic;

namespace BiosOptimizer.Core.Models
{
    public class DetailedGpuInfo
    {
        public string Name { get; set; } = "Graphics Adapter";
        public string Vendor { get; set; } = "GPU Vendor";
        public string DeviceId { get; set; } = "";
        public string AdapterType { get; set; } = "DEDICATED GPU"; // "INTEGRATED GPU" / "DEDICATED GPU"
        public string DedicatedVramText { get; set; } = "N/A";
        public string SharedMemoryText { get; set; } = "Dynamic";
        public string ReservedMemoryText { get; set; } = "";
        public string MemorySource { get; set; } = "DXGI 1.1 Hardware Adapter API";
        public long DedicatedBytes { get; set; } = 0;
        public long SharedBytes { get; set; } = 0;
        public long ReservedBytes { get; set; } = 0;
        public string VramText { get => DedicatedVramText; set => DedicatedVramText = value; }
        public string DriverVersion { get; set; } = "Standard";
        public string DriverDate { get; set; } = "";
        public string DirectXVersion { get; set; } = "DirectX 12";
        public string FeatureLevel { get; set; } = "12_1";
        public string WddmVersion { get; set; } = "WDDM 3.1";
        public string Resolution { get; set; } = "1920 x 1080";
        public string RefreshRate { get; set; } = "60 Hz";
        public string HagsStatus { get; set; } = "Supported";
        public bool IsPrimary { get; set; } = true;
        public string StatusBadge { get; set; } = "ACTIVE";
    }

    public class DetailedCpuInfo
    {
        public string Name { get; set; } = "Multi-Core Processor";
        public string Manufacturer { get; set; } = "Intel / AMD";
        public string Architecture { get; set; } = "x64 (64-bit Architecture)";
        public string Generation { get; set; } = "";
        public int PhysicalCores { get; set; } = 0;
        public int LogicalProcessors { get; set; } = 0;
        public string BaseClock { get; set; } = "Standard Frequency";
        public string MaxClock { get; set; } = "Dynamic Boost";
        public string Socket { get; set; } = "Standard Socket";
        public string L1Cache { get; set; } = "NOT AVAILABLE";
        public string L2Cache { get; set; } = "NOT AVAILABLE";
        public string L3Cache { get; set; } = "NOT AVAILABLE";
        public string Virtualization { get; set; } = "Hardware Supported (VT-x / AMD-V)";
        public string HyperVStatus { get; set; } = "Disabled / Inactive";
        public string CurrentLoad { get; set; } = "Normal";
        public string Temperature { get; set; } = "NOT AVAILABLE";
    }

    public class MemoryModuleInfo
    {
        public string BankLocator { get; set; } = "DIMM 1";
        public string CapacityText { get; set; } = "8 GB";
        public string SpeedText { get; set; } = "4800 MT/s";
        public string Manufacturer { get; set; } = "Samsung";
        public string PartNumber { get; set; } = "";
    }

    public class DetailedMemoryInfo
    {
        public string InstalledRamText { get; set; } = "LOADING...";
        public string UsableRamText { get; set; } = "LOADING...";
        public string AvailableRamText { get; set; } = "LOADING...";
        public string UsedRamText { get; set; } = "LOADING...";
        public double UsagePercentage { get; set; } = 0.0;
        public string SpeedText { get; set; } = "LOADING...";
        public string MemoryType { get; set; } = "LOADING...";
        public string ChannelConfig { get; set; } = "LOADING...";
        public int TotalSlots { get; set; } = 0;
        public int UsedSlots { get; set; } = 0;
        public string SlotsText => $"{UsedSlots} / {TotalSlots} Used";
        public string PageFileText { get; set; } = "System Managed";
        public string VirtualMemoryText { get; set; } = "LOADING...";
        public List<MemoryModuleInfo> Modules { get; set; } = new();
    }

    public class PhysicalDiskInfo
    {
        public string Model { get; set; } = "Physical Storage Disk";
        public string MediaType { get; set; } = "SSD";
        public string SizeText { get; set; } = "LOADING...";
        public string BusType { get; set; } = "PCIe / SATA";
        public string HealthStatus { get; set; } = "HEALTHY";
        public string Temperature { get; set; } = "NOT AVAILABLE";
        public bool IsSystemDisk { get; set; } = true;
    }

    public class VolumePartitionInfo
    {
        public string DriveLetter { get; set; } = "C:";
        public string Label { get; set; } = "OS";
        public string Format { get; set; } = "NTFS";
        public string TotalText { get; set; } = "LOADING...";
        public string UsedText { get; set; } = "LOADING...";
        public string FreeText { get; set; } = "LOADING...";
        public double UsagePercentage { get; set; } = 0.0;
        public bool IsSystemDrive { get; set; } = true;
        public string HealthBadge { get; set; } = "HEALTHY";
    }

    public class DetailedStorageInfo
    {
        public List<PhysicalDiskInfo> PhysicalDisks { get; set; } = new();
        public List<VolumePartitionInfo> Partitions { get; set; } = new();
        public string PrimaryStorageType => PhysicalDisks.Count > 0 ? PhysicalDisks[0].MediaType : "SSD";
        public string PrimaryStorageModel => PhysicalDisks.Count > 0 ? PhysicalDisks[0].Model : "Solid State Drive";
    }

    public class DetailedMotherboardInfo
    {
        public string Manufacturer { get; set; } = "LOADING...";
        public string Model { get; set; } = "LOADING...";
        public string SystemModel { get; set; } = "LOADING...";
        public string SystemSku { get; set; } = "LOADING...";
        public string BiosVendor { get; set; } = "LOADING...";
        public string BiosVersion { get; set; } = "LOADING...";
        public string BiosDate { get; set; } = "LOADING...";
        public string FirmwareType { get; set; } = "UEFI (GPT)";
        public string SecureBoot { get; set; } = "LOADING...";
        public string TpmVersion { get; set; } = "2.0";
        public string TpmStatus { get; set; } = "LOADING...";
    }

    public class DetailedWindowsInfo
    {
        public string Edition { get; set; } = "LOADING...";
        public string Version { get; set; } = "LOADING...";
        public string OsBuild { get; set; } = "LOADING...";
        public string Architecture { get; set; } = "64-bit Operating System";
        public string InstallDate { get; set; } = "NOT AVAILABLE";
        public string SystemLocale { get; set; } = "LOADING...";
        public string TimeZone { get; set; } = "LOADING...";
        public string UptimeText { get; set; } = "LOADING...";
        public string PowerPlan { get; set; } = "LOADING...";
        public string FastStartup { get; set; } = "Enabled";
        public string Hibernation { get; set; } = "Enabled";
        public string VirtualizationState { get; set; } = "Hyper-V / Core Isolation Capable";
    }

    public class DetailedSecurityInfo
    {
        public string SecureBootStatus { get; set; } = "GOOD";
        public string SecureBootDetail { get; set; } = "LOADING...";

        public string TpmStatus { get; set; } = "GOOD";
        public string TpmDetail { get; set; } = "TPM 2.0 Active & Ready";

        public string DefenderStatus { get; set; } = "GOOD";
        public string DefenderDetail { get; set; } = "Microsoft Defender Active";

        public string RealtimeProtection { get; set; } = "GOOD";
        public string RealtimeDetail { get; set; } = "Real-time Protection Enabled";

        public string FirewallStatus { get; set; } = "GOOD";
        public string FirewallDetail { get; set; } = "Windows Firewall Active";

        public string SmartScreenStatus { get; set; } = "GOOD";
        public string SmartScreenDetail { get; set; } = "SmartScreen Filter Enabled";

        public string CoreIsolationHvci { get; set; } = "GOOD";
        public string CoreIsolationDetail { get; set; } = "Memory Integrity Active";

        public string VbsStatus { get; set; } = "GOOD";
        public string VbsDetail { get; set; } = "Virtualization-Based Security Active";

        public string UacStatus { get; set; } = "GOOD";
        public string UacDetail { get; set; } = "User Account Control Enabled";
    }

    public class DisplayMonitorInfo
    {
        public string Name { get; set; } = "Internal Display";
        public string Resolution { get; set; } = "1920 x 1080";
        public string RefreshRate { get; set; } = "144 Hz";
        public bool IsPrimary { get; set; } = true;
        public string HdrStatus { get; set; } = "Supported (SDR Active)";
    }

    public class DetailedGraphicsDiagnostics
    {
        public string DirectXVersion { get; set; } = "DirectX 12 Ultimate";
        public string WddmVersion { get; set; } = "WDDM 3.1";
        public string FeatureLevels { get; set; } = "12_2, 12_1, 12_0, 11_1";
        public string HagsStatus { get; set; } = "Enabled (HAGS Active)";
        public string GpuScheduling { get; set; } = "Hardware Accelerated Scheduling Ready";
        public string HybridGraphics { get; set; } = "Advanced Optimus / MUX Ready";
        public int DisplayCount { get; set; } = 1;
        public List<DisplayMonitorInfo> Displays { get; set; } = new();
    }

    public class DetailedPowerInfo
    {
        public bool IsLaptop { get; set; } = true;
        public bool HasBattery { get; set; } = true;
        public bool AcConnected { get; set; } = true;
        public string PowerSourceText => AcConnected ? "AC Power Connected" : "Running on Battery";
        public int BatteryPercentage { get; set; } = 90;
        public string BatteryStatus { get; set; } = "Discharging";
        public string DesignCapacity { get; set; } = "NOT AVAILABLE";
        public string FullChargeCapacity { get; set; } = "NOT AVAILABLE";
        public string BatteryHealth { get; set; } = "GOOD (95% Design Capacity)";
        public string PowerPlan { get; set; } = "Balanced";
    }

    public class DetailedNetworkInfo
    {
        public string ActiveAdapterName { get; set; } = "Wi-Fi / Ethernet";
        public string ConnectionType { get; set; } = "Wireless (802.11ax)";
        public string LinkSpeed { get; set; } = "866 Mbps";
        public string Ipv4Address { get; set; } = "192.168.1.100";
        public string Gateway { get; set; } = "192.168.1.1";
        public string Dns { get; set; } = "1.1.1.1 / 8.8.8.8";
        public string Status { get; set; } = "Connected (Internet Access)";
    }

    public class DriverItemInfo
    {
        public string Category { get; set; } = "GPU";
        public string DeviceName { get; set; } = "NVIDIA GeForce RTX 4050 Laptop GPU";
        public string DriverVersion { get; set; } = "32.0.15.8186";
        public string DriverDate { get; set; } = "2025-11-12";
        public string Provider { get; set; } = "NVIDIA";
        public string Status { get; set; } = "HEALTHY";
    }

    public class DetailedDriverInfo
    {
        public List<DriverItemInfo> Drivers { get; set; } = new();
    }

    public class SystemHealthFinding
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Hardware"; // Hardware, Windows, Security, Storage, Drivers, Performance
        public string Severity { get; set; } = "PASS"; // PASS, WARNING, CRITICAL, INFO
        public string CurrentState { get; set; } = string.Empty;
        public string ExpectedState { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string RecommendedAction { get; set; } = string.Empty;
    }

    public class SystemHealthReport
    {
        public int OverallScore { get; set; } = 100;
        public string OverallRating { get; set; } = "EXCELLENT"; // EXCELLENT, GOOD, FAIR, WARNING, CRITICAL, NOT ANALYZED
        public string Summary { get; set; } = "All core hardware, security, and OS diagnostic parameters are operating optimally.";
        public int TotalChecks { get; set; } = 18;
        public int PassedChecks { get; set; } = 18;
        public int WarningChecks { get; set; } = 0;
        public int CriticalChecks { get; set; } = 0;
        public int HardwareScore { get; set; } = 100;
        public int WindowsScore { get; set; } = 100;
        public int SecurityScore { get; set; } = 100;
        public int StorageScore { get; set; } = 100;
        public int DriversScore { get; set; } = 100;
        public int PerformanceScore { get; set; } = 100;
        public DateTime ScanTime { get; set; } = DateTime.Now;
        public List<SystemHealthFinding> Findings { get; set; } = new();
    }

    public class CompleteSystemDiagnostics
    {
        public DetailedCpuInfo Cpu { get; set; } = new();
        public List<DetailedGpuInfo> Gpus { get; set; } = new();
        public DetailedMemoryInfo Memory { get; set; } = new();
        public DetailedStorageInfo Storage { get; set; } = new();
        public DetailedMotherboardInfo Motherboard { get; set; } = new();
        public DetailedWindowsInfo Windows { get; set; } = new();
        public DetailedSecurityInfo Security { get; set; } = new();
        public DetailedGraphicsDiagnostics Graphics { get; set; } = new();
        public DetailedPowerInfo Power { get; set; } = new();
        public DetailedNetworkInfo Network { get; set; } = new();
        public DetailedDriverInfo Drivers { get; set; } = new();
        public SystemHealthReport HealthReport { get; set; } = new();
    }
}
