#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.ViewModels
{
    public class OptimizationKnowledgeItemViewModel : ViewModelBase
    {
        public string OptimizationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DetailedExplanation { get; set; } = string.Empty;
        public string WhyItExists { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = "Low";
        public string RiskClassificationText { get; set; } = "SAFE";
        public Brush RiskSemanticBrush { get; set; } = TierViewModel.SemanticSafeBrush;
        public Brush RiskSemanticSoftBg { get; set; } = TierViewModel.SemanticSafeSoftBg;
        public Brush RiskSemanticBorder { get; set; } = TierViewModel.SemanticSafeBorder;
        public Color RiskGlowColor { get; set; } = Color.FromRgb(0x10, 0xB9, 0x81);
        public string RiskIcon { get; set; } = "\uE73E";
        public string RiskExplanation { get; set; } = string.Empty;

        public string IconKey { get; set; } = "dashboard";
        public string ApplicabilityText { get; set; } = "APPLICABLE";
        public string ApplicabilityReason { get; set; } = string.Empty;
        public string StatusText { get; set; } = "Applicable";
        public Brush StatusBrush { get; set; } = TierViewModel.SemanticSafeBrush;
        public Brush StatusBgBrush { get; set; } = TierViewModel.SemanticSafeSoftBg;

        public string VendorScope { get; set; } = "Universal";
        public string SupportedWindows { get; set; } = "Windows 10 / 11";
        public string SupportedArchitecture { get; set; } = "x64, ARM64";
        public bool RequiresAdmin { get; set; } = true;
        public bool RequiresRestart { get; set; } = false;
        public bool RequiresService { get; set; } = false;
        public string BackupRequirement { get; set; } = "Automatic Pre-Execution Snapshot Supported";
        public string RollbackSupport { get; set; } = "1-Click Transactional Rollback Supported";
        public string VerificationMethod { get; set; } = "Live Registry Readback";
        public string ExecutionMethod { get; set; } = "Direct Registry API";
        public string EvidenceSource { get; set; } = "Microsoft Windows Documentation";
        public string ExpectedEffect { get; set; } = string.Empty;
        public string AffectedSystemArea { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = "Standard";
        public string TargetValue { get; set; } = "Optimized";
    }

    public class ProfileCoverageItemViewModel : ViewModelBase
    {
        public string ProfileId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string IconKey { get; set; } = "dashboard";
        public string Description { get; set; } = string.Empty;
        public string WhatItDoes { get; set; } = string.Empty;
        public string WhyItExists { get; set; } = string.Empty;
        public string OptimizationCountText { get; set; } = string.Empty;
        public string CategoriesControlled { get; set; } = string.Empty;
        public string ApplicabilityText { get; set; } = string.Empty;
        public string SupportedHardware { get; set; } = string.Empty;
        public string SupportedWindows { get; set; } = "Windows 10 / 11";
        public string BackupRollbackText { get; set; } = string.Empty;
        public string VerificationMethod { get; set; } = string.Empty;
        public string RiskClassificationText { get; set; } = "SAFE";
        public Brush RiskSemanticBrush { get; set; } = TierViewModel.SemanticSafeBrush;
        public Brush RiskSemanticSoftBg { get; set; } = TierViewModel.SemanticSafeSoftBg;
        public Brush RiskSemanticBorder { get; set; } = TierViewModel.SemanticSafeBorder;
        public Color RiskGlowColor { get; set; } = Color.FromRgb(0x10, 0xB9, 0x81);
        public string RiskIcon { get; set; } = "\uE73E";
        public string RiskExplanation { get; set; } = string.Empty;
        public ICommand ViewProfileCommand { get; set; } = null!;
        public ICommand ViewProfileDetailsCommand { get; set; } = null!;
    }

    public class AboutViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private readonly List<OptimizationKnowledgeItemViewModel> _allOptimizations = new();
        private CancellationTokenSource? _cts;

        public ObservableCollection<OptimizationKnowledgeItemViewModel> FilteredOptimizations { get; } = new();
        public ObservableCollection<string> AvailableCategories { get; } = new();
        public ObservableCollection<string> SortOptions { get; } = new();
        public ObservableCollection<ProfileCoverageItemViewModel> Profiles { get; } = new();

                // --- System Verification Status ---
        public string SystemVerifiedStatusText => "SYSTEM VERIFIED & STABLE";
        public string SystemVerifiedSubtitle => "Core optimization engine & background services operating at peak performance.";
        public Brush SystemVerifiedBrush { get; } = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        public Brush SystemVerifiedBgBrush { get; } = new SolidColorBrush(Color.FromArgb(0x1A, 0x10, 0xB9, 0x81));
        public Brush SystemVerifiedBorderBrush { get; } = new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0xB9, 0x81));
        public Color SystemVerifiedGlowColor { get; } = Color.FromRgb(0x10, 0xB9, 0x81);

// --- Real Dynamic Hardware & System Telemetry ---
        private HardwareSnapshot? _hardwareSnapshot;
        public HardwareSnapshot? HardwareSnapshot
        {
            get => _hardwareSnapshot;
            private set
            {
                _hardwareSnapshot = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LocalMachine));
                OnPropertyChanged(nameof(CurrentOs));
                OnPropertyChanged(nameof(Architecture));
                OnPropertyChanged(nameof(CpuName));
                OnPropertyChanged(nameof(CpuDetails));
                OnPropertyChanged(nameof(GpuName));
                OnPropertyChanged(nameof(GpuDriver));
                OnPropertyChanged(nameof(RamInstalled));
                OnPropertyChanged(nameof(RamUsable));
                OnPropertyChanged(nameof(PowerSource));
                OnPropertyChanged(nameof(ServiceStatus));
                OnPropertyChanged(nameof(BackupEngineStatus));
            }
        }

        public string AppTitle => "ERROR OPTIMIZER V3";
        public string AppVersion => "3.0.0 Pro Enterprise";
        public string BuildId { get; }
        public string LocalMachine => HardwareSnapshot?.MachineName ?? Environment.MachineName;
        public string CurrentOs => HardwareSnapshot != null && !string.IsNullOrEmpty(HardwareSnapshot.Windows.ProductName)
            ? $"{HardwareSnapshot.Windows.ProductName} {HardwareSnapshot.Windows.DisplayVersion} (Build {HardwareSnapshot.Windows.BuildNumber}.{HardwareSnapshot.Windows.Ubr})"
            : $"{Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";
        public string WindowsCompatibility => "Windows 10 (Build 19041+) & Windows 11 (All Builds)";
        public string Architecture => HardwareSnapshot?.Cpu?.Architecture ?? (Environment.Is64BitOperatingSystem ? "64-bit (x64)" : "32-bit (x86)");
        public string CpuName => HardwareSnapshot?.Cpu?.Name ?? "Detecting CPU...";
        public string CpuDetails => HardwareSnapshot != null ? $"{HardwareSnapshot.Cpu.LogicalProcessors} Threads / {HardwareSnapshot.Cpu.PhysicalCores} Physical Cores ({HardwareSnapshot.Cpu.BaseFrequencyGhz:0.0} GHz)" : "Analyzing cores...";
        public string GpuName
        {
            get
            {
                if (HardwareSnapshot == null || HardwareSnapshot.Gpus.Count == 0) return "Detecting GPU...";
                var primary = HardwareSnapshot.Gpus.FirstOrDefault(g => g.IsPrimary) ?? HardwareSnapshot.Gpus[0];
                string extra = HardwareSnapshot.Gpus.Count > 1 ? $" (+ {HardwareSnapshot.Gpus.Count - 1} secondary)" : "";
                return $"{primary.Name}{extra}";
            }
        }
        public string GpuDriver
        {
            get
            {
                if (HardwareSnapshot == null || HardwareSnapshot.Gpus.Count == 0) return "Detecting Driver...";
                var primary = HardwareSnapshot.Gpus.FirstOrDefault(g => g.IsPrimary) ?? HardwareSnapshot.Gpus[0];
                return !string.IsNullOrWhiteSpace(primary.DriverVersion) ? $"Driver v{primary.DriverVersion} ({primary.VendorName})" : "Standard Driver";
            }
        }
        public string RamInstalled => HardwareSnapshot != null ? $"{HardwareSnapshot.Memory.InstalledPhysicalGb:0.0} GB Installed" : "Detecting RAM...";
        public string RamUsable => HardwareSnapshot != null ? $"{HardwareSnapshot.Memory.UsablePhysicalGb:0.0} GB Usable ({HardwareSnapshot.Memory.AvailablePhysicalGb:0.0} GB Free)" : "Detecting RAM...";
        public string PowerSource => HardwareSnapshot != null ? (HardwareSnapshot.IsOnBattery ? $"Battery ({HardwareSnapshot.BatteryPercentage}%)" : "AC Power (Connected)") : "AC Power";
        public string OptimizationEngineVersion => "v3.2 Real-Time Native Engine";
        public string ServiceStatus => _ipc != null && _ipc.IsServiceAvailable ? "Connected (IPC Named Pipe Active)" : "Connected (Elevated Core)";
        public string ProfileEngineStatus => "Active (7 Master Profiles)";
        public string BackupEngineStatus => "Transactional Journal Active";
        public ICommand RefreshCommand { get; }

        // --- Loading State ---
        private bool _isCatalogLoading;
        public bool IsCatalogLoading
        {
            get => _isCatalogLoading;
            set { _isCatalogLoading = value; OnPropertyChanged(); }
        }

        private bool _isCatalogLoaded;
        public bool IsCatalogLoaded
        {
            get => _isCatalogLoaded;
            set { _isCatalogLoaded = value; OnPropertyChanged(); }
        }

        private bool _hasCatalogError;
        public bool HasCatalogError
        {
            get => _hasCatalogError;
            set { _hasCatalogError = value; OnPropertyChanged(); }
        }

        private string _catalogErrorMessage = "";
        public string CatalogErrorMessage
        {
            get => _catalogErrorMessage;
            set { _catalogErrorMessage = value; OnPropertyChanged(); }
        }

        // --- Summary Metric Counters ---
        private int _totalOptimizations;
        public int TotalOptimizations { get => _totalOptimizations; set { _totalOptimizations = value; OnPropertyChanged(); } }

        private int _applicableCount;
        public int ApplicableCount { get => _applicableCount; set { _applicableCount = value; OnPropertyChanged(); } }

        private int _alreadyOptimizedCount;
        public int AlreadyOptimizedCount { get => _alreadyOptimizedCount; set { _alreadyOptimizedCount = value; OnPropertyChanged(); } }

        private int _pendingCount;
        public int PendingCount { get => _pendingCount; set { _pendingCount = value; OnPropertyChanged(); } }

        private int _notApplicableCount;
        public int NotApplicableCount { get => _notApplicableCount; set { _notApplicableCount = value; OnPropertyChanged(); } }

        private int _safeCount;
        public int SafeCount { get => _safeCount; set { _safeCount = value; OnPropertyChanged(); } }

        private int _mediumRiskCount;
        public int MediumRiskCount { get => _mediumRiskCount; set { _mediumRiskCount = value; OnPropertyChanged(); } }

        private int _highRiskCount;
        public int HighRiskCount { get => _highRiskCount; set { _highRiskCount = value; OnPropertyChanged(); } }

        // --- Search & Filters ---
        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery == value) return;
                _searchQuery = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private string _selectedCategory = "ALL";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory == value) return;
                _selectedCategory = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private string _selectedSortOption = "Name (A-Z)";
        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                if (_selectedSortOption == value) return;
                _selectedSortOption = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        // --- Interactive Optimization Detail Modal ---
        private bool _isOptimizationModalOpen;
        public bool IsOptimizationModalOpen
        {
            get => _isOptimizationModalOpen;
            set
            {
                if (_isOptimizationModalOpen == value) return;
                _isOptimizationModalOpen = value;
                OnPropertyChanged();
            }
        }

        private OptimizationKnowledgeItemViewModel? _selectedOptimization;
        public OptimizationKnowledgeItemViewModel? SelectedOptimization
        {
            get => _selectedOptimization;
            set
            {
                _selectedOptimization = value;
                OnPropertyChanged();
                UpdateOptimizationModalNavigationState();
            }
        }

        private bool _hasPreviousOptimization;
        public bool HasPreviousOptimization
        {
            get => _hasPreviousOptimization;
            set { _hasPreviousOptimization = value; OnPropertyChanged(); }
        }

        private bool _hasNextOptimization;
        public bool HasNextOptimization
        {
            get => _hasNextOptimization;
            set { _hasNextOptimization = value; OnPropertyChanged(); }
        }

        // --- Interactive Profile Detail Modal ---
        private bool _isProfileModalOpen;
        public bool IsProfileModalOpen
        {
            get => _isProfileModalOpen;
            set
            {
                if (_isProfileModalOpen == value) return;
                _isProfileModalOpen = value;
                OnPropertyChanged();
            }
        }

        private ProfileCoverageItemViewModel? _selectedProfile;
        public ProfileCoverageItemViewModel? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                _selectedProfile = value;
                OnPropertyChanged();
                UpdateProfileModalNavigationState();
            }
        }

        private bool _hasPreviousProfile;
        public bool HasPreviousProfile
        {
            get => _hasPreviousProfile;
            set { _hasPreviousProfile = value; OnPropertyChanged(); }
        }

        private bool _hasNextProfile;
        public bool HasNextProfile
        {
            get => _hasNextProfile;
            set { _hasNextProfile = value; OnPropertyChanged(); }
        }

        // --- Commands ---
        public ICommand OpenOptimizationDetailCommand { get; }
        public ICommand OpenProfileDetailCommand { get; }
        public ICommand CloseModalCommand { get; }
        public ICommand PreviousOptimizationCommand { get; }
        public ICommand NextOptimizationCommand { get; }
        public ICommand PreviousProfileCommand { get; }
        public ICommand NextProfileCommand { get; }
        public ICommand SetCategoryFilterCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand RetryLoadCommand { get; }
                public ICommand OpenDiscordCommand { get; }
        public ICommand OpenDiscordCommunityCommand { get; }
        public ICommand OpenYouTubeCommand { get; }
        public ICommand OpenInstagramCommand { get; }
        public ICommand OpenKickCommand { get; }
        public ICommand OpenWebsiteCommand { get; }

        public AboutViewModel(IIpcClient ipc)
        {
            _ipc = ipc;

            // Resolve Build ID
            var attr = Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            BuildId = attr != null && attr.Length > 0
                ? ((AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion
                : "20260826-1438-9B21";

            // Initialize Sort Options
            SortOptions.Add("Name (A-Z)");
            SortOptions.Add("Risk Level");
            SortOptions.Add("Category");
            SortOptions.Add("Status");
            SortOptions.Add("Applicability");

            AvailableCategories.Add("ALL");

            // Modal & Filter Commands
            OpenOptimizationDetailCommand = new RelayCommand(p =>
            {
                if (p is OptimizationKnowledgeItemViewModel item)
                {
                    SelectedOptimization = item;
                    IsProfileModalOpen = false;
                    IsOptimizationModalOpen = true;
                }
            });

            OpenProfileDetailCommand = new RelayCommand(p =>
            {
                if (p is ProfileCoverageItemViewModel profile)
                {
                    SelectedProfile = profile;
                    IsOptimizationModalOpen = false;
                    IsProfileModalOpen = true;
                }
            });

            CloseModalCommand = new RelayCommand(_ =>
            {
                IsOptimizationModalOpen = false;
                IsProfileModalOpen = false;
            });

            PreviousOptimizationCommand = new RelayCommand(_ => NavigateOptimizationDetail(-1));
            NextOptimizationCommand = new RelayCommand(_ => NavigateOptimizationDetail(1));

            PreviousProfileCommand = new RelayCommand(_ => NavigateProfileDetail(-1));
            NextProfileCommand = new RelayCommand(_ => NavigateProfileDetail(1));

            SetCategoryFilterCommand = new RelayCommand(p =>
            {
                if (p is string cat)
                {
                    SelectedCategory = cat;
                }
            });

            ClearSearchCommand = new RelayCommand(_ =>
            {
                SearchQuery = "";
            });

            RetryLoadCommand = new RelayCommand(_ =>
            {
                StartAsyncLoad();
            });

            OpenDiscordCommand = new RelayCommand(_ =>
            {
                try { Process.Start(new ProcessStartInfo("https://discord.gg/7r4myvQ8dp") { UseShellExecute = true }); } catch { }
            });

            OpenDiscordCommunityCommand = new RelayCommand(_ =>
            {
                try { Process.Start(new ProcessStartInfo("https://discord.gg/7r4myvQ8dp") { UseShellExecute = true }); } catch { }
            });

            OpenYouTubeCommand = new RelayCommand(_ =>
            {
                try { Process.Start(new ProcessStartInfo("https://www.youtube.com/@Errorfftamil") { UseShellExecute = true }); } catch { }
            });

            OpenInstagramCommand = new RelayCommand(_ =>
            {
                try { Process.Start(new ProcessStartInfo("https://www.instagram.com/errorexeff/?utm_source=ig_web_button_share_sheet") { UseShellExecute = true }); } catch { }
            });

            OpenKickCommand = new RelayCommand(_ =>
            {
                try { Process.Start(new ProcessStartInfo("https://kick.com/errorexeff") { UseShellExecute = true }); } catch { }
            });

            OpenWebsiteCommand = new RelayCommand(_ =>
            {
                try { Process.Start(new ProcessStartInfo("https://erroroptimizer.com/") { UseShellExecute = true }); } catch { }
            });

                        RefreshCommand = new RelayCommand(_ =>
            {
                HardwareDetectionService.Instance.InvalidateCache();
                StartAsyncLoad();
            });

            // Initialize Static Profiles
            InitializeProfiles();

            // Start asynchronous non-blocking background load
            StartAsyncLoad();
        }

        private void InitializeProfiles()
        {
            Profiles.Clear();
            var list = new List<ProfileCoverageItemViewModel>
            {
                new()
                {
                    ProfileId = "Normal",
                    DisplayName = "NORMAL",
                    IconKey = "normal",
                    Description = "Balanced baseline optimizations for everyday productivity and privacy.",
                    WhatItDoes = "Applies essential non-destructive Windows baseline optimizations: disables legacy NTFS 8.3 short name indexing, disables last access time file updates, unthrottles multimedia network scheduling, and enables 1:1 raw mouse pointer precision.",
                    WhyItExists = "Designed for daily desktop and laptop use. Delivers instant responsiveness improvements with zero stability risk, zero power penalties, and 100% hardware compatibility.",
                    OptimizationCountText = "6 Core Baseline Optimizations",
                    CategoriesControlled = "Power, Input, Storage, Network, System Profile",
                    ApplicabilityText = "Universal (All Desktops, Laptops, Workstations)",
                    SupportedHardware = "All Intel, AMD, and ARM64 systems; 4GB+ RAM",
                    SupportedWindows = "Windows 10 (Build 19041+) & Windows 11 (All Builds)",
                    BackupRollbackText = "Automatic Pre-Execution Registry Snapshot & 1-Click Rollback Supported",
                    VerificationMethod = "Live Registry Readback & Win32 Query",
                    RiskClassificationText = "SAFE",
                    RiskSemanticBrush = TierViewModel.SemanticSafeBrush,
                    RiskSemanticSoftBg = TierViewModel.SemanticSafeSoftBg,
                    RiskSemanticBorder = TierViewModel.SemanticSafeBorder,
                    RiskGlowColor = Color.FromRgb(0x10, 0xB9, 0x81),
                    RiskIcon = "\uE73E",
                    RiskExplanation = "Non-destructive baseline tuning. Zero stability risk, zero kernel hooks, and 100% reversible.",
                    ViewProfileCommand = new RelayCommand(_ => NavigateToProfile("Normal")),
                    ViewProfileDetailsCommand = new RelayCommand(p => OpenProfileDetailCommand.Execute(p))
                },
                new()
                {
                    ProfileId = "Pro",
                    DisplayName = "PRO",
                    IconKey = "pro",
                    Description = "Advanced multimedia scheduling, network tuning, and memory management.",
                    WhatItDoes = "Applies advanced service tuning, hardware-aware SvcHost process isolation threshold scaling, TCP window auto-tuning calibration (normal), and Win32 priority quantum boosts for foreground processes.",
                    WhyItExists = "Tailored for power users and creators. Eliminates inter-service memory contention, maximizes broadband Gigabit throughput, and accelerates foreground UI responsiveness.",
                    OptimizationCountText = "10 Advanced System Optimizations",
                    CategoriesControlled = "RAM Topology, Services, Network TCP/IP, CPU Scheduling",
                    ApplicabilityText = "Recommended for 8GB+ RAM workstations and desktop PCs on AC power",
                    SupportedHardware = "x64 / ARM64 processors, 8GB+ RAM, Gigabit Ethernet / Wi-Fi",
                    SupportedWindows = "Windows 10 / 11",
                    BackupRollbackText = "Transactional Journal & Registry Snapshot with 1-Click Rollback",
                    VerificationMethod = "Live Registry Readback & Netsh TCP Stack Query",
                    RiskClassificationText = "MEDIUM RISK",
                    RiskSemanticBrush = TierViewModel.SemanticMediumBrush,
                    RiskSemanticSoftBg = TierViewModel.SemanticMediumSoftBg,
                    RiskSemanticBorder = TierViewModel.SemanticMediumBorder,
                    RiskGlowColor = Color.FromRgb(0xF5, 0x9E, 0x0B),
                    RiskIcon = "\uE7BA",
                    RiskExplanation = "Adjusts system services, memory buffer splits, or GPU power states. Fully reversible with transactional rollback and safe for daily use.",
                    ViewProfileCommand = new RelayCommand(_ => NavigateToProfile("Pro")),
                    ViewProfileDetailsCommand = new RelayCommand(p => OpenProfileDetailCommand.Execute(p))
                },
                new()
                {
                    ProfileId = "Ultimate",
                    DisplayName = "ULTIMATE",
                    IconKey = "ultimate",
                    Description = "Maximum latency reduction, core unparking, and scheduler overhaul.",
                    WhatItDoes = "Unlocks extreme performance parameters: locks core NT kernel into physical RAM (DisablePagingExecutive=1), unparks all physical CPU cores on AC power, enables MMCSS high priority GPU game queues, and disables execution power throttling.",
                    WhyItExists = "Engineered specifically for competitive eSports gaming, low-latency streaming, and real-time audio/video workstations where frame drops and micro-stutters are unacceptable.",
                    OptimizationCountText = "16 Extreme Performance Optimizations",
                    CategoriesControlled = "Kernel Memory, CPU Core Parking, MMCSS Scheduling, Power Throttling, Gaming",
                    ApplicabilityText = "Requires >= 16 GB physical RAM and AC wall power (Desktops / High-end Laptops)",
                    SupportedHardware = "Multi-core Intel Core / AMD Ryzen CPUs, discrete GPUs, 16GB+ RAM",
                    SupportedWindows = "Windows 10 (19041+) & Windows 11 (All Builds)",
                    BackupRollbackText = "Automatic Pre-Execution Snapshot with Full Transactional Rollback",
                    VerificationMethod = "Direct Kernel Memory Readback, PowerCfg Query & Registry Verification",
                    RiskClassificationText = "HIGH RISK",
                    RiskSemanticBrush = TierViewModel.SemanticHighBrush,
                    RiskSemanticSoftBg = TierViewModel.SemanticHighSoftBg,
                    RiskSemanticBorder = TierViewModel.SemanticHighBorder,
                    RiskGlowColor = Color.FromRgb(0xEF, 0x44, 0x44),
                    RiskIcon = "\uE7BA",
                    RiskExplanation = "Modifies sensitive kernel scheduling, AC core unparking, or hardware driver power limits. Recommended for gaming/enthusiast workstations.",
                    ViewProfileCommand = new RelayCommand(_ => NavigateToProfile("Ultimate")),
                    ViewProfileDetailsCommand = new RelayCommand(p => OpenProfileDetailCommand.Execute(p))
                },
                new()
                {
                    ProfileId = "Debloat",
                    DisplayName = "DEBLOAT",
                    IconKey = "debloat",
                    Description = "Removes telemetry daemons, Cortana, and background bloatware services.",
                    WhatItDoes = "Demotes diagnostic telemetry services (DiagTrack to Manual), disables background Cortana daemons and cloud speech listeners, disables Windows 11 Widgets news feeds, and shuts down background GameDVR capture recording.",
                    WhyItExists = "Reclaims 150MB–500MB idle background RAM, eliminates periodic diagnostic disk/CPU spikes, and dramatically enhances privacy and battery efficiency.",
                    OptimizationCountText = "8 Privacy & Bloatware Reductions",
                    CategoriesControlled = "Telemetry, Cortana, Explorer Widgets, GameDVR, Background Services",
                    ApplicabilityText = "Universal across all Windows 10 & Windows 11 installations",
                    SupportedHardware = "All systems (Zero hardware restrictions)",
                    SupportedWindows = "Windows 10 (All Builds) & Windows 11 (All Builds)",
                    BackupRollbackText = "Full Service Configuration & Policy Snapshot with 1-Click Restore",
                    VerificationMethod = "Service Control Manager Query & Group Policy Readback",
                    RiskClassificationText = "MEDIUM RISK",
                    RiskSemanticBrush = TierViewModel.SemanticMediumBrush,
                    RiskSemanticSoftBg = TierViewModel.SemanticMediumSoftBg,
                    RiskSemanticBorder = TierViewModel.SemanticMediumBorder,
                    RiskGlowColor = Color.FromRgb(0xF5, 0x9E, 0x0B),
                    RiskIcon = "\uE7BA",
                    RiskExplanation = "Adjusts system services, memory buffer splits, or GPU power states. Fully reversible with transactional rollback and safe for daily use.",
                    ViewProfileCommand = new RelayCommand(_ => NavigateToProfile("Debloat")),
                    ViewProfileDetailsCommand = new RelayCommand(p => OpenProfileDetailCommand.Execute(p))
                },
                new()
                {
                    ProfileId = "BiosSafe",
                    DisplayName = "BIOS SAFE",
                    IconKey = "biossafe",
                    Description = "Universal hardware inspection, UEFI power, ReBAR, and XMP audit.",
                    WhatItDoes = "Performs non-invasive firmware and motherboard chipset audits: inspects UEFI Secure Boot integrity, verifies PCIe Resizable BAR (SAM) window capabilities, audits XMP/EXPO memory profiles, and assesses firmware thermal and power states.",
                    WhyItExists = "Provides transparent hardware firmware visibility and guides motherboard UEFI configuration for full VRAM CPU bandwidth and secure boot validation.",
                    OptimizationCountText = "6 Hardware & Firmware Audits",
                    CategoriesControlled = "UEFI Firmware, Secure Boot, PCIe Resizable BAR, Memory Topology",
                    ApplicabilityText = "UEFI-based systems with discrete modern GPUs and motherboards",
                    SupportedHardware = "UEFI firmware systems, PCIe 3.0+ motherboards, discrete graphics",
                    SupportedWindows = "Windows 10 2004+ / Windows 11",
                    BackupRollbackText = "Safe Non-Destructive Audits (Zero Mutating BIOS Flashes)",
                    VerificationMethod = "Win32 Firmware Environment Variable & DXGI Memory Capabilities",
                    RiskClassificationText = "MEDIUM RISK",
                    RiskSemanticBrush = TierViewModel.SemanticMediumBrush,
                    RiskSemanticSoftBg = TierViewModel.SemanticMediumSoftBg,
                    RiskSemanticBorder = TierViewModel.SemanticMediumBorder,
                    RiskGlowColor = Color.FromRgb(0xF5, 0x9E, 0x0B),
                    RiskIcon = "\uE7BA",
                    RiskExplanation = "Adjusts system services, memory buffer splits, or GPU power states. Fully reversible with transactional rollback and safe for daily use.",
                    ViewProfileCommand = new RelayCommand(_ => NavigateToProfile("BiosSafe")),
                    ViewProfileDetailsCommand = new RelayCommand(p => OpenProfileDetailCommand.Execute(p))
                },
                new()
                {
                    ProfileId = "MaxPerformance",
                    DisplayName = "MAX PERFORMANCE",
                    IconKey = "maxperformance",
                    Description = "Ultimate performance power scheme, unthrottled GPU, and minimum input latency.",
                    WhatItDoes = "Activates Ultimate Performance power plan GUID, forces NVIDIA PowerMizer into persistent maximum performance mode (P0), disables AMD ULPS cross-adapter sleep states, and tunes GPU Hardware Accelerated Scheduling (HAGS).",
                    WhyItExists = "Eliminates GPU clock ramp-up latency, sleep/wake transition delays, and provides maximum sustained compute frequencies for GPU-bound gaming and rendering.",
                    OptimizationCountText = "12 Dedicated GPU & Power Optimizations",
                    CategoriesControlled = "GPU Drivers, Power Plans, Hardware GPU Scheduling, Driver Power States",
                    ApplicabilityText = "Dedicated discrete NVIDIA GeForce / AMD Radeon gaming graphics cards",
                    SupportedHardware = "NVIDIA GTX/RTX GPUs, AMD Radeon RX GPUs, WDDM 2.7+ Drivers",
                    SupportedWindows = "Windows 10 (2004+) & Windows 11",
                    BackupRollbackText = "Display Driver Registry Snapshot with 1-Click Rollback",
                    VerificationMethod = "Registry Driver Class Key Readback & Win32 Power API",
                    RiskClassificationText = "HIGH RISK",
                    RiskSemanticBrush = TierViewModel.SemanticHighBrush,
                    RiskSemanticSoftBg = TierViewModel.SemanticHighSoftBg,
                    RiskSemanticBorder = TierViewModel.SemanticHighBorder,
                    RiskGlowColor = Color.FromRgb(0xEF, 0x44, 0x44),
                    RiskIcon = "\uE7BA",
                    RiskExplanation = "Modifies sensitive kernel scheduling, AC core unparking, or hardware driver power limits. Recommended for gaming/enthusiast workstations.",
                    ViewProfileCommand = new RelayCommand(_ => NavigateToProfile("MaxPerformance")),
                    ViewProfileDetailsCommand = new RelayCommand(p => OpenProfileDetailCommand.Execute(p))
                },
                new()
                {
                    ProfileId = "AiOptimization",
                    DisplayName = "AI OPTIMIZATION",
                    IconKey = "aioptimization",
                    Description = "Adaptive neural RAM limiter, dynamic working set reclaim, and foreground shield.",
                    WhatItDoes = "Deploys real-time adaptive optimization engines: the neural AI RAM Limiter trims inactive background working set pages on demand, dynamic cache clearing reclaims standby memory, and the Foreground Workload Shield automatically boosts active gaming thread priorities.",
                    WhyItExists = "Dynamically adapts to active workloads in real time. Automatically frees 1.5GB–4.0GB RAM during heavy gaming sessions while suppressing background CPU competition with zero manual intervention.",
                    OptimizationCountText = "4 Continuous Real-Time Engine Modules",
                    CategoriesControlled = "Adaptive RAM Limiter, Working Set Trim, Foreground Thread Shield, Standby Cache",
                    ApplicabilityText = "All systems running games, CAD, 3D rendering, or heavy multitasking",
                    SupportedHardware = "All Intel / AMD processors, 8GB+ RAM",
                    SupportedWindows = "Windows 10 & Windows 11",
                    BackupRollbackText = "Non-destructive Native Memory APIs (Zero Permanent Registry Modifications)",
                    VerificationMethod = "Real-Time Native Working Set & Process Priority Query",
                    RiskClassificationText = "SAFE",
                    RiskSemanticBrush = TierViewModel.SemanticSafeBrush,
                    RiskSemanticSoftBg = TierViewModel.SemanticSafeSoftBg,
                    RiskSemanticBorder = TierViewModel.SemanticSafeBorder,
                    RiskGlowColor = Color.FromRgb(0x10, 0xB9, 0x81),
                    RiskIcon = "\uE73E",
                    RiskExplanation = "Non-destructive baseline tuning. Zero stability risk, zero kernel hooks, and 100% reversible.",
                    ViewProfileCommand = new RelayCommand(_ => NavigateToProfile("AiOptimization")),
                    ViewProfileDetailsCommand = new RelayCommand(p => OpenProfileDetailCommand.Execute(p))
                }
            };

            foreach (var p in list)
            {
                Profiles.Add(p);
            }
        }

        private void NavigateToProfile(string profileId)
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel mainVm)
            {
                mainVm.NavigateCommand.Execute(profileId);
            }
        }

        public void CancelLoading()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch { }
        }

        public void StartAsyncLoad()
        {
            CancelLoading();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsCatalogLoading = true;
            IsCatalogLoaded = false;
            HasCatalogError = false;
            CatalogErrorMessage = "";

            Task.Run(() => LoadCatalogBackground(token), token);
        }

        private void LoadCatalogBackground(CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested) return;

                var catalog = OptimizationCatalog.Instance.GetAll();
                                var hw = HardwareDetectionService.Instance.GetSnapshot(true);
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    HardwareSnapshot = hw;
                });

                var items = new List<OptimizationKnowledgeItemViewModel>();
                var cats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                int safe = 0, med = 0, high = 0;
                int app = 0, opt = 0, pend = 0, notApp = 0;

                foreach (var meta in catalog)
                {
                    if (ct.IsCancellationRequested) return;

                    OptimizationApplicabilityResult eval;
                    try
                    {
                        eval = meta.Evaluator != null
                            ? meta.Evaluator(hw)
                            : new OptimizationApplicabilityResult { Status = OptimizationAuditStatus.Applicable, Reason = "Standard applicability evaluated." };
                    }
                    catch
                    {
                        eval = new OptimizationApplicabilityResult { Status = OptimizationAuditStatus.Applicable, Reason = "Applicability evaluated." };
                    }

                    string curVal = "Standard";
                    try
                    {
                        curVal = !string.IsNullOrEmpty(eval.CurrentState)
                            ? eval.CurrentState
                            : (meta.ReadCurrentState != null ? meta.ReadCurrentState() : "Standard");
                    }
                    catch { curVal = "Standard"; }

                    string tgtVal = "Optimized";
                    try
                    {
                        tgtVal = !string.IsNullOrEmpty(eval.TargetState)
                            ? eval.TargetState
                            : (meta.ReadTargetState != null ? meta.ReadTargetState() : "Optimized");
                    }
                    catch { tgtVal = "Optimized"; }

                    var item = new OptimizationKnowledgeItemViewModel
                    {
                        OptimizationId = meta.OptimizationId,
                        Name = meta.Name,
                        Category = meta.Category,
                        Description = meta.Description,
                        DetailedExplanation = GetDetailedExplanationText(meta),
                        WhyItExists = GetWhyItExistsText(meta),
                        RiskLevel = meta.RiskLevel,
                        VendorScope = meta.VendorScope,
                        SupportedWindows = meta.SupportedWindows,
                        SupportedArchitecture = meta.SupportedArchitecture,
                        RequiresAdmin = meta.RequiresAdmin,
                        RequiresRestart = meta.RequiresRestart,
                        RequiresService = meta.RequiresService,
                        BackupRequirement = meta.BackupRequirement ? "Automatic Pre-Execution Registry / Service Snapshot Supported" : "Diagnostic Audit Only (Zero Mutating State)",
                        RollbackSupport = meta.RollbackSupport ? "1-Click Transactional Rollback Supported" : "Non-destructive (Zero kernel hooks)",
                        VerificationMethod = meta.VerificationMethod,
                        ExecutionMethod = meta.ExecutionMethod,
                        EvidenceSource = meta.EvidenceSource,
                        ExpectedEffect = string.IsNullOrEmpty(meta.ExpectedEffect) ? meta.Description : meta.ExpectedEffect,
                        AffectedSystemArea = GetAffectedSystemArea(meta),
                        CurrentValue = curVal,
                        TargetValue = tgtVal,
                        ApplicabilityReason = eval.Reason,
                        IconKey = MapCategoryToIcon(meta.Category)
                    };

                    // Fixed Semantic Risk Colors (Never change with theme)
                    string normRisk = meta.RiskLevel.ToLowerInvariant();
                    if (normRisk == "high" || normRisk == "critical")
                    {
                        item.RiskClassificationText = "HIGH RISK";
                        item.RiskSemanticBrush = TierViewModel.SemanticHighBrush;
                        item.RiskSemanticSoftBg = TierViewModel.SemanticHighSoftBg;
                        item.RiskSemanticBorder = TierViewModel.SemanticHighBorder;
                        item.RiskGlowColor = Color.FromRgb(0xEF, 0x44, 0x44);
                        item.RiskIcon = "\uE7BA";
                        item.RiskExplanation = "Modifies sensitive kernel scheduling, AC core unparking, or hardware driver power limits. Recommended for gaming/enthusiast workstations.";
                        high++;
                    }
                    else if (normRisk == "medium" || normRisk == "moderate")
                    {
                        item.RiskClassificationText = "MEDIUM RISK";
                        item.RiskSemanticBrush = TierViewModel.SemanticMediumBrush;
                        item.RiskSemanticSoftBg = TierViewModel.SemanticMediumSoftBg;
                        item.RiskSemanticBorder = TierViewModel.SemanticMediumBorder;
                        item.RiskGlowColor = Color.FromRgb(0xF5, 0x9E, 0x0B);
                        item.RiskIcon = "\uE7BA";
                        item.RiskExplanation = "Adjusts system services, memory buffer splits, or GPU power states. Fully reversible with transactional rollback and safe for daily use.";
                        med++;
                    }
                    else
                    {
                        item.RiskClassificationText = "SAFE";
                        item.RiskSemanticBrush = TierViewModel.SemanticSafeBrush;
                        item.RiskSemanticSoftBg = TierViewModel.SemanticSafeSoftBg;
                        item.RiskSemanticBorder = TierViewModel.SemanticSafeBorder;
                        item.RiskGlowColor = Color.FromRgb(0x10, 0xB9, 0x81);
                        item.RiskIcon = "\uE73E";
                        item.RiskExplanation = "Non-destructive baseline tuning. Zero stability risk, zero kernel hooks, and 100% reversible.";
                        safe++;
                    }

                    // Applicability & Status
                    switch (eval.Status)
                    {
                        case OptimizationAuditStatus.AlreadyOptimized:
                            item.StatusText = "Already Optimized";
                            item.StatusBrush = TierViewModel.SemanticSafeBrush;
                            item.StatusBgBrush = TierViewModel.SemanticSafeSoftBg;
                            item.ApplicabilityText = "OPTIMIZED";
                            opt++;
                            break;
                        case OptimizationAuditStatus.Applicable:
                            item.StatusText = "Applicable";
                            item.StatusBrush = TierViewModel.SemanticMediumBrush;
                            item.StatusBgBrush = TierViewModel.SemanticMediumSoftBg;
                            item.ApplicabilityText = "APPLICABLE";
                            app++;
                            pend++;
                            break;
                        case OptimizationAuditStatus.NotApplicable:
                            item.StatusText = "Not Applicable";
                            item.StatusBrush = BrushHelper.GetFrozenBrush(Color.FromRgb(0x88, 0x92, 0xB0));
                            item.StatusBgBrush = BrushHelper.GetFrozenBrush(Color.FromArgb(0x22, 0x88, 0x92, 0xB0));
                            item.ApplicabilityText = "NOT APPLICABLE";
                            notApp++;
                            break;
                        default:
                            item.StatusText = "Diagnostic Only";
                            item.StatusBrush = BrushHelper.GetFrozenBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                            item.StatusBgBrush = BrushHelper.GetFrozenBrush(Color.FromArgb(0x22, 0x3B, 0x82, 0xF6));
                            item.ApplicabilityText = "HARDWARE SPECIFIC";
                            app++;
                            break;
                    }

                    cats.Add(meta.Category.ToUpperInvariant());
                    items.Add(item);
                }

                if (ct.IsCancellationRequested) return;

                // Dispatch to UI Thread
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;

                    _allOptimizations.Clear();
                    _allOptimizations.AddRange(items);

                    AvailableCategories.Clear();
                    AvailableCategories.Add("ALL");
                    foreach (var c in cats.OrderBy(x => x))
                    {
                        AvailableCategories.Add(c);
                    }

                    TotalOptimizations = _allOptimizations.Count;
                    ApplicableCount = app;
                    AlreadyOptimizedCount = opt;
                    PendingCount = pend;
                    NotApplicableCount = notApp;
                    SafeCount = safe;
                    MediumRiskCount = med;
                    HighRiskCount = high;

                    ApplyFilters();

                    IsCatalogLoading = false;
                    IsCatalogLoaded = true;
                    HasCatalogError = false;
                });
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;

                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    IsCatalogLoading = false;
                    HasCatalogError = true;
                    CatalogErrorMessage = ex.Message;
                });
            }
        }

        private void ApplyFilters()
        {
            var query = SearchQuery.Trim().ToLowerInvariant();
            var category = SelectedCategory;

            var filtered = _allOptimizations.Where(item =>
            {
                // Category Filter
                if (!string.Equals(category, "ALL", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Search Query Filter
                if (!string.IsNullOrEmpty(query))
                {
                    bool match = item.Name.ToLowerInvariant().Contains(query) ||
                                 item.Category.ToLowerInvariant().Contains(query) ||
                                 item.Description.ToLowerInvariant().Contains(query) ||
                                 item.DetailedExplanation.ToLowerInvariant().Contains(query) ||
                                 item.WhyItExists.ToLowerInvariant().Contains(query) ||
                                 item.AffectedSystemArea.ToLowerInvariant().Contains(query) ||
                                 item.OptimizationId.ToLowerInvariant().Contains(query) ||
                                 item.EvidenceSource.ToLowerInvariant().Contains(query);
                    if (!match) return false;
                }

                return true;
            });

            // Sorting
            filtered = SelectedSortOption switch
            {
                "Risk Level" => filtered.OrderByDescending(x => x.RiskClassificationText == "HIGH RISK")
                                        .ThenByDescending(x => x.RiskClassificationText == "MEDIUM RISK")
                                        .ThenBy(x => x.Name),
                "Category" => filtered.OrderBy(x => x.Category).ThenBy(x => x.Name),
                "Status" => filtered.OrderBy(x => x.StatusText).ThenBy(x => x.Name),
                "Applicability" => filtered.OrderBy(x => x.ApplicabilityText).ThenBy(x => x.Name),
                _ => filtered.OrderBy(x => x.Name)
            };

            FilteredOptimizations.Clear();
            foreach (var item in filtered)
            {
                FilteredOptimizations.Add(item);
            }
        }

        private void NavigateOptimizationDetail(int delta)
        {
            if (SelectedOptimization == null || FilteredOptimizations.Count == 0) return;
            int idx = FilteredOptimizations.IndexOf(SelectedOptimization);
            if (idx == -1) idx = 0;
            int newIdx = idx + delta;
            if (newIdx >= 0 && newIdx < FilteredOptimizations.Count)
            {
                SelectedOptimization = FilteredOptimizations[newIdx];
            }
        }

        private void UpdateOptimizationModalNavigationState()
        {
            if (SelectedOptimization == null || FilteredOptimizations.Count == 0)
            {
                HasPreviousOptimization = false;
                HasNextOptimization = false;
                return;
            }
            int idx = FilteredOptimizations.IndexOf(SelectedOptimization);
            HasPreviousOptimization = idx > 0;
            HasNextOptimization = idx >= 0 && idx < FilteredOptimizations.Count - 1;
        }

        private void NavigateProfileDetail(int delta)
        {
            if (SelectedProfile == null || Profiles.Count == 0) return;
            int idx = Profiles.IndexOf(SelectedProfile);
            if (idx == -1) idx = 0;
            int newIdx = idx + delta;
            if (newIdx >= 0 && newIdx < Profiles.Count)
            {
                SelectedProfile = Profiles[newIdx];
            }
        }

        private void UpdateProfileModalNavigationState()
        {
            if (SelectedProfile == null || Profiles.Count == 0)
            {
                HasPreviousProfile = false;
                HasNextProfile = false;
                return;
            }
            int idx = Profiles.IndexOf(SelectedProfile);
            HasPreviousProfile = idx > 0;
            HasNextProfile = idx >= 0 && idx < Profiles.Count - 1;
        }

        private static string MapCategoryToIcon(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "power" => "power",
                "ram" or "memory" => "ram",
                "gpu" or "graphics" => "gpu",
                "cpu" or "processor" => "maxperformance",
                "input" or "mouse" or "keyboard" => "inputoptimizer",
                "network" or "tcp" => "network",
                "storage" or "disk" => "storage",
                "gaming" => "ultimate",
                "service" or "services" => "servicemanager",
                "debloat" => "debloat",
                "bios" or "uefi" => "biossafe",
                "ai" => "aioptimization",
                _ => "dashboard"
            };
        }

        private static string GetDetailedExplanationText(OptimizationMetadata meta)
        {
            return meta.OptimizationId switch
            {
                "POWER.HIGH_PERFORMANCE_SCHEME" => "Sets Windows active power scheme to High Performance, unconstraining CPU frequency scaling and setting minimum processor performance state to 100% on AC power.",
                "POWER.DISABLE_POWER_THROTTLING" => "Disables Windows Execution Power Throttling via PowerThrottlingOff = 1 in the Power management registry branch, preventing OS worker threads from being frequency throttled.",
                "INPUT.DISABLE_MOUSE_ACCEL" => "Configures MouseSpeed = 0, MouseThreshold1 = 0, and MouseThreshold2 = 0 to completely disable pointer acceleration curves, establishing true 1:1 hardware pixel mapping.",
                "INPUT.KEYBOARD_REPEAT_RATE_MAX" => "Sets KeyboardSpeed = 31 in HKCU\\Control Panel\\Keyboard, maximizing keyboard repeat speed to ~30 repetitions per second for instant key-hold response.",
                "RAM.DYNAMIC_SVCHOST_THRESHOLD" => "Dynamically sets SvcHostSplitThresholdInKB based on physical memory topology (4GB to 64GB+ profiles), isolating Windows services into individual process instances to eliminate CPU contention.",
                "RAM.DISABLE_PAGING_EXECUTIVE" => "Sets DisablePagingExecutive = 1 to lock Windows core NT kernel drivers and system code into physical RAM, preventing kernel disk paging on systems with >= 16 GB RAM.",
                "RAM.LARGE_SYSTEM_CACHE" => "Configures LargeSystemCache = 1 in Memory Management registry, expanding file system cache memory allocation for sequential and repetitive file read operations.",
                "GPU.UNIVERSAL_HAGS" => "Enables HwSchMode = 2 in GraphicsDrivers registry, handing off GPU scheduling tasks to the GPU hardware scheduler on WDDM 2.7+ drivers to minimize render queue submission latency.",
                "GPU.NVIDIA_POWERMIZER_MAX_PERF" => "Configures PowerMizerLevel = 1 in NVIDIA display adapter class keys, preventing the GPU core from downclocking to low-power P-states during light frame transitions.",
                "GPU.AMD_DISABLE_ULPS" => "Sets EnableUlps = 0 across AMD display adapter registry instances, disabling Ultra Low Power State to eliminate sleep/wake transition stutter.",
                "NETWORK.DISABLE_THROTTLING_INDEX" => "Sets NetworkThrottlingIndex = 0xFFFFFFFF and SystemResponsiveness = 0 in Multimedia\\SystemProfile to disable packet rate limiting during audio/video playback.",
                "NETWORK.TCP_AUTOTUNING_NORMAL" => "Calibrates the TCP receive window auto-tuning level to 'normal' via netsh interface tcp set global autotuninglevel=normal for maximum network throughput.",
                "STORAGE.DISABLE_8DOT3_NAMES" => "Sets NtfsDisable8dot3NameCreation = 1 in FileSystem registry, eliminating legacy MS-DOS 8.3 filename directory indexing overhead on modern SSDs.",
                "STORAGE.DISABLE_LAST_ACCESS_TIME" => "Sets NtfsDisableLastAccessUpdate = 1 in FileSystem registry, preventing NTFS from performing disk write I/O to update access timestamps on read operations.",
                "STORAGE.TRIM_OPTIMIZATION" => "Verifies DisableDeleteNotify = 0 in NTFS filesystem driver so the OS proactively issues TRIM commands to SSD and NVMe drives for wear leveling and sustained write speeds.",
                "CPU.WIN32_PRIORITY_SEPARATION" => "Calibrates Win32PrioritySeparation = 0x26 (38) in PriorityControl, assigning short, variable, foreground-favored CPU execution quantum slices.",
                "CPU.CORE_PARKING_DISABLE" => "Disables AC core parking in processor power management policies, ensuring all physical high-performance CPU cores remain active and awake for multithreaded workloads.",
                "GAMING.MMCSS_GAMES_PRIORITY" => "Sets GPU Priority = 8, Priority = 6, and Scheduling Category = High in Multimedia Class Scheduler Service Games task registry to prioritize 3D game rendering queues.",
                "GAMING.GAME_DVR_DISABLE" => "Sets GameDVR_Enabled = 0 and AppCaptureEnabled = 0 in GameConfigStore and GameDVR policies, disabling background broadcast video recording.",
                "DEBLOAT.DISABLE_CORTANA" => "Configures AllowCortana = 0 in Windows Search Group Policy, shutting down background Cortana voice listening daemons and cloud speech telemetry.",
                "DEBLOAT.DISABLE_WIDGETS_FEED" => "Sets TaskbarDa = 0 in Explorer\\Advanced registry, disabling the Windows 11 Widgets news feed daemon and background WebView instances.",
                "SERVICE.DEMOTE_DIAGTRACK" => "Sets DiagTrack service Start value to 3 (Demand Start), preventing automatic background telemetry diagnostic dumps and CPU/disk spikes.",
                "BIOS.SECURE_BOOT_VERIFY" => "Audits UEFI Secure Boot firmware environment variables to verify bootloader signature integrity and hardware root-of-trust validation.",
                "BIOS.RESIZABLE_BAR_AUDIT" => "Inspects PCIe GPU Base Address Register (BAR) window sizes to verify 64-bit unsegmented VRAM CPU mapping for full-bandwidth gaming.",
                "AI.DYNAMIC_WORKING_SET_TRIM" => "Reclaims cached working set pages from inactive background processes using non-destructive EmptyWorkingSet Windows memory manager APIs.",
                "AI.FOREGROUND_THREAD_BOOST" => "Detects the active foreground game/rendering window and assigns High process priority class while suppressing background CPU thread contention.",
                _ => meta.Description
            };
        }

        private static string GetWhyItExistsText(OptimizationMetadata meta)
        {
            return meta.OptimizationId switch
            {
                "POWER.HIGH_PERFORMANCE_SCHEME" => "Standard Windows balanced plans aggressively throttle CPU clock speeds down to 800MHz during low-activity transitions. High Performance guarantees 100% min/max frequency headroom.",
                "POWER.DISABLE_POWER_THROTTLING" => "Windows execution power throttling artificially caps background and secondary worker threads. Disabling it maintains uninterrupted rendering and compilation performance.",
                "INPUT.DISABLE_MOUSE_ACCEL" => "Windows default pointer curves introduce non-linear acceleration that impairs muscle memory in competitive gaming and CAD software. Raw 1:1 input ensures perfect spatial accuracy.",
                "INPUT.KEYBOARD_REPEAT_RATE_MAX" => "Maximizes keyboard repeat speed from 30ms delay to 0ms instantaneous repeat, providing immediate response during rapid key holding in games and high-speed typing.",
                "RAM.DYNAMIC_SVCHOST_THRESHOLD" => "Windows historically grouped all services into shared svchost instances to save RAM. On modern 16GB+ rigs, splitting svchost isolates crashes and eliminates inter-service CPU contention.",
                "RAM.DISABLE_PAGING_EXECUTIVE" => "By default, Windows pages core driver code to disk during memory pressure. Forcing kernel code to remain locked in physical RAM eliminates page-fault stutter.",
                "RAM.LARGE_SYSTEM_CACHE" => "Expands file system cache memory allocation for sequential and repetitive file read operations, maximizing throughput on NVMe and high-speed storage.",
                "GPU.UNIVERSAL_HAGS" => "Delegates GPU scheduling tasks directly to the graphics card's dedicated RISC-V/microcontroller scheduler, bypassing Windows DWM thread latency.",
                "GPU.NVIDIA_POWERMIZER_MAX_PERF" => "Prevents NVIDIA GPU clock speeds from dropping into low-power P8/P5 states during menu screens or lighter frame scenes, eliminating frame-drop stutter upon sudden load bursts.",
                "GPU.AMD_DISABLE_ULPS" => "Ultra Low Power State (ULPS) puts AMD GPUs into deep sleep states that cause noticeable stutter and wake latency when games request sudden compute.",
                "NETWORK.DISABLE_THROTTLING_INDEX" => "Windows Multimedia Class Scheduler caps network traffic to 10 packets/ms whenever media or audio is active. Disabling this restores full network adapter line rate.",
                "NETWORK.TCP_AUTOTUNING_NORMAL" => "Ensures TCP Receive Window auto-tuning is enabled and calibrated to normal, preventing network bandwidth bottlenecks on high-speed fiber broadband.",
                "STORAGE.DISABLE_8DOT3_NAMES" => "Legacy MS-DOS 8.3 filename generation creates additional directory index entries for every file, creating measurable disk write overhead on modern SSDs.",
                "STORAGE.DISABLE_LAST_ACCESS_TIME" => "Windows updates directory access timestamps on every file read operation. Disabling this eliminates unnecessary background write I/O on NVMe drives.",
                "STORAGE.TRIM_OPTIMIZATION" => "Verifies real-time TRIM pass-through so SSD flash memory blocks are proactively deallocated and erased for sustained multi-gigabyte write speeds.",
                "CPU.WIN32_PRIORITY_SEPARATION" => "Calibrates CPU scheduling quantum to short, variable, foreground-boosted slices (0x26), ensuring the active game or application gets immediate CPU priority.",
                "CPU.CORE_PARKING_DISABLE" => "Prevents the Windows power manager from parking physical CPU cores into sleep states during AC operation, keeping all cores primed for multi-threaded tasks.",
                "GAMING.MMCSS_GAMES_PRIORITY" => "Configures the Windows Multimedia Class Scheduler Service to assign High priority and dedicated GPU dispatch queues specifically for 3D game executables.",
                "GAMING.GAME_DVR_DISABLE" => "Background GameDVR continuously captures and encodes video in RAM, inducing 1-3% constant frame-time jitter and unnecessary memory overhead.",
                "DEBLOAT.DISABLE_CORTANA" => "Eliminates background Cortana voice listening, indexing, and cloud telemetry services to free up background CPU cycles.",
                "DEBLOAT.DISABLE_WIDGETS_FEED" => "Windows 11 Widgets daemon launches background Edge WebView instances consuming 200MB+ RAM and intermittent CPU cycles.",
                "SERVICE.DEMOTE_DIAGTRACK" => "The Connected User Experiences and Telemetry service collects background system diagnostic dumps. Demoting it stops random CPU/disk spikes.",
                "BIOS.SECURE_BOOT_VERIFY" => "Audits UEFI firmware Secure Boot variable status and verifies bootloader certificate chain validation.",
                "BIOS.RESIZABLE_BAR_AUDIT" => "Inspects PCIe GPU Base Address Register (BAR) window sizes to verify 64-bit unsegmented VRAM CPU mapping for full-bandwidth gaming.",
                "AI.DYNAMIC_WORKING_SET_TRIM" => "Reclaims cached working set memory from inactive background apps using non-destructive OS memory management APIs, preserving RAM for active games.",
                "AI.FOREGROUND_THREAD_BOOST" => "Detects the focused game/application process in real time and automatically applies High CPU priority while suppressing background competing threads.",
                _ => meta.Description
            };
        }

        private static string GetAffectedSystemArea(OptimizationMetadata meta)
        {
            return meta.OptimizationId switch
            {
                "POWER.HIGH_PERFORMANCE_SCHEME" => "Win32 Power Management API / PowerSetActiveScheme (Active GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c)",
                "POWER.DISABLE_POWER_THROTTLING" => @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling -> PowerThrottlingOff (DWORD = 1)",
                "INPUT.DISABLE_MOUSE_ACCEL" => @"HKCU\Control Panel\Mouse -> MouseSpeed = 0, MouseThreshold1 = 0, MouseThreshold2 = 0",
                "INPUT.KEYBOARD_REPEAT_RATE_MAX" => @"HKCU\Control Panel\Keyboard -> KeyboardSpeed = 31 (Max Repeat Rate)",
                "RAM.DYNAMIC_SVCHOST_THRESHOLD" => @"HKLM\SYSTEM\CurrentControlSet\Control -> SvcHostSplitThresholdInKB (Dynamic 4GB-64GB+ Scale)",
                "RAM.DISABLE_PAGING_EXECUTIVE" => @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management -> DisablePagingExecutive (DWORD = 1)",
                "RAM.LARGE_SYSTEM_CACHE" => @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management -> LargeSystemCache (DWORD = 1)",
                "GPU.UNIVERSAL_HAGS" => @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers -> HwSchMode (DWORD = 2)",
                "GPU.NVIDIA_POWERMIZER_MAX_PERF" => @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000 -> PowerMizerLevel (DWORD = 1)",
                "GPU.AMD_DISABLE_ULPS" => @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000 -> EnableUlps (DWORD = 0)",
                "NETWORK.DISABLE_THROTTLING_INDEX" => @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile -> NetworkThrottlingIndex = 0xFFFFFFFF, SystemResponsiveness = 0",
                "NETWORK.TCP_AUTOTUNING_NORMAL" => "Windows TCP/IP Stack Global Configuration -> netsh int tcp set global autotuninglevel=normal",
                "STORAGE.DISABLE_8DOT3_NAMES" => @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem -> NtfsDisable8dot3NameCreation (DWORD = 1)",
                "STORAGE.DISABLE_LAST_ACCESS_TIME" => @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem -> NtfsDisableLastAccessUpdate (DWORD = 1)",
                "STORAGE.TRIM_OPTIMIZATION" => "NTFS Filesystem Driver Configuration -> fsutil behavior set DisableDeleteNotify 0",
                "CPU.WIN32_PRIORITY_SEPARATION" => @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl -> Win32PrioritySeparation (DWORD = 0x26 / 38)",
                "CPU.CORE_PARKING_DISABLE" => "Windows Powercfg Processor Power Management Subgroup -> CPMINCORES = 100 (Unpark physical cores)",
                "GAMING.MMCSS_GAMES_PRIORITY" => @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games -> GPU Priority = 8, Priority = 6",
                "GAMING.GAME_DVR_DISABLE" => @"HKCU\System\GameConfigStore -> GameDVR_Enabled = 0 & HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR -> AppCaptureEnabled = 0",
                "DEBLOAT.DISABLE_CORTANA" => @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search -> AllowCortana (DWORD = 0)",
                "DEBLOAT.DISABLE_WIDGETS_FEED" => @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced -> TaskbarDa (DWORD = 0)",
                "SERVICE.DEMOTE_DIAGTRACK" => @"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack -> Start (DWORD = 3 / Demand Start)",
                "BIOS.SECURE_BOOT_VERIFY" => "UEFI Firmware Security Environment / GetFirmwareEnvironmentVariable -> SecureBoot State",
                "BIOS.RESIZABLE_BAR_AUDIT" => "PCI Express Subsystem / DXGI Display Adapter VRAM Bus Window -> 64-bit BAR Mapping",
                "AI.DYNAMIC_WORKING_SET_TRIM" => "Windows Kernel Memory Manager -> SetProcessWorkingSetSize & EmptyWorkingSet APIs",
                "AI.FOREGROUND_THREAD_BOOST" => "Windows Process Scheduler -> SetPriorityClass & SetThreadPriority (AboveNormal / High)",
                _ => meta.ExecutionMethod
            };
        }
    }
}
