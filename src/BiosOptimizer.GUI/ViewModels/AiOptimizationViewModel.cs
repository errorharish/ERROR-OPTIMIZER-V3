using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.Power;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BiosOptimizer.GUI.ViewModels
{
    public enum AiOptimizationActiveTab
    {
        RamLimiter = 0,
        WorkloadOptimization = 1,
        PowerPlan = 2
    }

    public class AiOptimizationViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private readonly RamLimiterEngine _ramEngine;
        private readonly WorkloadOptimizationEngine _workloadEngine;
        private readonly PowerPlanEngine _powerEngine;
        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

        public AiOptimizationViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            _ramEngine = RamLimiterEngine.Instance;
            _workloadEngine = WorkloadOptimizationEngine.Instance;
            _powerEngine = PowerPlanEngine.Instance;

            // Module Tab Switching
            SelectRamLimiterTabCommand = new RelayCommand(_ => SelectTab(AiOptimizationActiveTab.RamLimiter));
            SelectWorkloadTabCommand = new RelayCommand(_ => SelectTab(AiOptimizationActiveTab.WorkloadOptimization));
            SelectPowerPlanTabCommand = new RelayCommand(_ => SelectTab(AiOptimizationActiveTab.PowerPlan));

            // Power Plan Commands
            ApplyPowerPlanCommand = new RelayCommand(async p => await ApplyPowerPlanAsync(p as PowerPlanItem));
            RestorePreviousPowerPlanCommand = new RelayCommand(async _ => await RestorePreviousPowerPlanAsync());
            EnableUltimatePerformanceCommand = new RelayCommand(async _ => await EnableUltimatePerformanceAsync());
            CreateCustomAiPlanCommand = new RelayCommand(async _ => await CreateAiMaxPerformancePlanAsync());
            CreateAiMaxPerformancePlanCommand = new RelayCommand(async _ => await CreateAiMaxPerformancePlanAsync());
            CreateAiBatteryEfficiencyPlanCommand = new RelayCommand(async _ => await CreateAiBatteryEfficiencyPlanAsync());
            DeleteCustomAiPlanCommand = new RelayCommand(async p => await DeleteCustomAiPlanAsync(p as PowerPlanItem));
            RefreshPowerPlansCommand = new RelayCommand(async _ => await RefreshPowerPlansAsync());
            SetStartupPowerPlanCommand = new RelayCommand(async p => await SetStartupPowerPlanAsync(p as PowerPlanItem));
            ToggleAutoEnableOnStartupCommand = new RelayCommand(async _ => await ToggleAutoEnableOnStartupAsync());
            ToggleContinuousEnforcementCommand = new RelayCommand(_ => ToggleContinuousEnforcement());
            OpenPlanEditorCommand = new RelayCommand(p => OpenPlanEditor(p as PowerPlanItem));
            ClosePlanEditorCommand = new RelayCommand(_ => ClosePlanEditor());
            SavePlanSettingCommand = new RelayCommand(async s => await SavePlanSettingAsync(s as PowerPlanSettingItem));
            SaveAllPlanSettingsCommand = new RelayCommand(async _ => await SaveAllPlanSettingsAsync());
            OpenCreatePlanModalCommand = new RelayCommand(_ => OpenCreatePlanModal());
            CloseCreatePlanModalCommand = new RelayCommand(_ => CloseCreatePlanModal());
            CreateCustomNamedPlanCommand = new RelayCommand(async _ => await CreateCustomNamedPlanAsync());

            // Top Primary Mode Controller Commands (Shared UI Structure)
            ToggleActiveModuleModeCommand = new RelayCommand(_ => ToggleActiveModuleMode());
            SetActiveModuleNormalCommand = new RelayCommand(_ => SetActiveModuleNormal());
            SetActiveModuleCustomCommand = new RelayCommand(_ => SetActiveModuleCustom());

            // RAM Limiter Commands
            ToggleMasterSwitchCommand = new RelayCommand(_ => ToggleMasterSwitch());
            SetViewModeNormalCommand = new RelayCommand(_ => SetViewMode(RamLimiterViewMode.Normal));
            SetViewModeCustomCommand = new RelayCommand(_ => SetViewMode(RamLimiterViewMode.Custom));
            SetAutoModeCommand = new RelayCommand(_ => SetNormalMode(RamLimiterMode.Auto));
            SetLightModeCommand = new RelayCommand(_ => SetNormalMode(RamLimiterMode.Light));
            SetAggressiveModeCommand = new RelayCommand(_ => SetNormalMode(RamLimiterMode.Aggressive));
            SelectSearchAppCommand = new RelayCommand(p => SelectSearchApp(p as SearchableAppItem));
            EnableCustomTargetCommand = new RelayCommand(_ => EnableCustomTarget());
            ToggleCustomTargetCommand = new RelayCommand(p => ToggleCustomTarget(p as CustomTargetItem));
            RemoveCustomTargetCommand = new RelayCommand(p => RemoveCustomTarget(p as CustomTargetItem));
            ToggleForegroundProtectionCommand = new RelayCommand(_ => ToggleForegroundProtection());
            BrowseCustomRamAppCommand = new RelayCommand(_ => BrowseCustomRamApp());

            // Workload Optimization Commands
            ToggleWorkloadMasterSwitchCommand = new RelayCommand(_ => ToggleWorkloadMasterSwitch());
            SetWorkloadViewNormalCommand = new RelayCommand(_ => SetWorkloadViewMode(WorkloadViewMode.Normal));
            SetWorkloadViewCustomCommand = new RelayCommand(_ => SetWorkloadViewMode(WorkloadViewMode.Custom));
            SetWorkloadAutoModeCommand = new RelayCommand(_ => SetWorkloadNormalMode(WorkloadNormalMode.Auto));
            SetWorkloadLightModeCommand = new RelayCommand(_ => SetWorkloadNormalMode(WorkloadNormalMode.Light));
            SetWorkloadAggressiveModeCommand = new RelayCommand(_ => SetWorkloadNormalMode(WorkloadNormalMode.Aggressive));
            ToggleCustomAutoApplyCommand = new RelayCommand(_ => ToggleCustomAutoApply());
            
            // Workload Inventory Category Filter Commands
            SetCategoryAllCommand = new RelayCommand(_ => SetInventoryCategory(WorkloadType.All));
            SetCategoryGamesCommand = new RelayCommand(_ => SetInventoryCategory(WorkloadType.Gaming));
            SetCategoryCreativeCommand = new RelayCommand(_ => SetInventoryCategory(WorkloadType.Creative));
            SetCategoryVideoAiCommand = new RelayCommand(_ => SetInventoryCategory(WorkloadType.VideoAi));
            SetCategoryEmulatorsCommand = new RelayCommand(_ => SetInventoryCategory(WorkloadType.Emulator));
            SetCategoryRecordingCommand = new RelayCommand(_ => SetInventoryCategory(WorkloadType.Recording));

            SelectCustomAppCommand = new RelayCommand(p => SelectCustomApp(p as InstalledWorkloadItem));
            BrowseExecutableForWorkloadCommand = new RelayCommand(p => BrowseExecutableForWorkload(p as InstalledWorkloadItem));
            AddCustomWorkloadAppCommand = new RelayCommand(_ => AddCustomWorkloadApp());
            SaveCustomAppProfileCommand = new RelayCommand(_ => SaveCustomAppProfile());
            DeleteCustomProfileCommand = new RelayCommand(p => DeleteCustomProfile(p as CustomAppProfile));
            ToggleCustomProfileCommand = new RelayCommand(p => ToggleCustomProfile(p as CustomAppProfile));
            RescanInventoryCommand = new RelayCommand(_ => RescanInventory());
            RefreshCommand = new RelayCommand(_ => RescanInventory());

            // Timer for UI sync
            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.0)
            };
            _refreshTimer.Tick += (s, e) => RefreshUiState();

            _ramEngine.TelemetryUpdated += () => UiDispatcher.Run(RefreshUiState);
            _workloadEngine.StateUpdated += () => UiDispatcher.Run(RefreshUiState);

            RefreshUiState();
        }

        #region Module Tab Navigation
        private AiOptimizationActiveTab _activeTab = AiOptimizationActiveTab.RamLimiter;
        public AiOptimizationActiveTab ActiveTab
        {
            get => _activeTab;
            set
            {
                if (_activeTab != value)
                {
                    _activeTab = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRamLimiterTab));
                    OnPropertyChanged(nameof(IsWorkloadTab));
                    OnPropertyChanged(nameof(IsPowerPlanTab));
                    OnPropertyChanged(nameof(Description));
                    OnPropertyChanged(nameof(CurrentActiveModuleModeText));
                    OnPropertyChanged(nameof(CurrentActiveModuleModeToggleText));
                    OnPropertyChanged(nameof(CurrentActiveModuleDescription));
                    OnPropertyChanged(nameof(CurrentActiveModuleIsNormal));
                    OnPropertyChanged(nameof(CurrentActiveModuleIsCustom));
                    OnPropertyChanged(nameof(ProTipText));
                }
            }
        }

        public bool IsRamLimiterTab => ActiveTab == AiOptimizationActiveTab.RamLimiter;
        public bool IsWorkloadTab => ActiveTab == AiOptimizationActiveTab.WorkloadOptimization;
        public bool IsPowerPlanTab => ActiveTab == AiOptimizationActiveTab.PowerPlan;

        public ICommand SelectRamLimiterTabCommand { get; }
        public ICommand SelectWorkloadTabCommand { get; }
        public ICommand SelectPowerPlanTabCommand { get; }

        private void SelectTab(AiOptimizationActiveTab tab)
        {
            ActiveTab = tab;
            if (tab == AiOptimizationActiveTab.PowerPlan)
            {
                _ = RefreshPowerPlansAsync();
            }
            RefreshUiState();
        }
        #endregion

        #region Header Properties
        public string TierName => "AI OPTIMIZATION";
        public string Description => IsPowerPlanTab
            ? "Hardware-aware Windows power scheme manager. Automatically analyzes CPU, battery, and foreground applications to recommend and verify optimal power schemes."
            : (IsRamLimiterTab
                ? "Intelligent adaptive memory optimization. Automatically guards system RAM in Normal view or enforces specific limits for selected apps in Custom view."
                : "Persistent background workload acceleration. Automatically detects active games, creative tools, video AI and emulators to apply hardware-aware performance profiles.");
        public string ProfileIcon => "\uE945";
        public string RiskLabel => "SAFE";
        public string RiskClassificationText => "SAFE";
        public Brush RiskSemanticBrush => TierViewModel.SemanticSafeBrush;
        public Brush RiskSemanticSoftBg => TierViewModel.SemanticSafeSoftBg;
        public Brush RiskSemanticBorderBrush => TierViewModel.SemanticSafeBorder;
        public Color RiskGlowColor => Color.FromRgb(0x10, 0xB9, 0x81);
        public string RiskIcon => "";
        public string RiskDescription => "Non-destructive adaptive memory tuning & dynamic thread priority.";
        public int SafeActionCount => _ramEngine.AutoActionCount > 0 ? _ramEngine.AutoActionCount : 18;
        public int MediumRiskActionCount => 0;
        public int HighRiskActionCount => 0;
        #endregion

        #region Top Mode Switcher Properties (Consistent with 6 Profiles)
        public string CurrentActiveModuleModeText => IsPowerPlanTab
            ? "WINDOWS POWER SCHEME"
            : (IsRamLimiterTab 
                ? (IsNormalView ? "CURRENT MODE: NORMAL" : "CURRENT MODE: CUSTOM")
                : (IsWorkloadNormalView ? "CURRENT MODE: NORMAL" : "CURRENT MODE: CUSTOM"));

        public string CurrentActiveModuleModeToggleText => IsPowerPlanTab
            ? "REFRESH POWER PLANS"
            : (IsRamLimiterTab
                ? (IsNormalView ? "SWITCH TO CUSTOM" : "SWITCH TO NORMAL")
                : (IsWorkloadNormalView ? "SWITCH TO CUSTOM" : "SWITCH TO NORMAL"));

        public string CurrentActiveModuleDescription => IsPowerPlanTab
            ? "Real hardware-configured Windows power schemes. Select any verified applicable scheme or create Ultimate Performance on supported machines."
            : (IsRamLimiterTab
                ? (IsNormalView ? "Automatically manages background process memory and enforces dynamic working-set bounds." : "Manually control specific application targets, memory limits, and foreground protection.")
                : (IsWorkloadNormalView ? "Automatically detects active games, creative tools, and emulators to apply hardware-aware performance profiles." : "Manually configure per-application optimizations, saved profiles, and custom auto-apply rules."));

        public bool CurrentActiveModuleIsNormal => IsPowerPlanTab ? true : (IsRamLimiterTab ? IsNormalView : IsWorkloadNormalView);
        public bool CurrentActiveModuleIsCustom => IsPowerPlanTab ? false : (IsRamLimiterTab ? IsCustomView : IsWorkloadCustomView);

        public ICommand ToggleActiveModuleModeCommand { get; }
        public ICommand SetActiveModuleNormalCommand { get; }
        public ICommand SetActiveModuleCustomCommand { get; }

        private void ToggleActiveModuleMode()
        {
            if (IsPowerPlanTab)
            {
                _ = RefreshPowerPlansAsync();
            }
            else if (IsRamLimiterTab)
            {
                if (IsNormalView) SetViewMode(RamLimiterViewMode.Custom);
                else SetViewMode(RamLimiterViewMode.Normal);
            }
            else
            {
                if (IsWorkloadNormalView) SetWorkloadViewMode(WorkloadViewMode.Custom);
                else SetWorkloadViewMode(WorkloadViewMode.Normal);
            }
        }

        private void SetActiveModuleNormal()
        {
            if (IsRamLimiterTab) SetViewMode(RamLimiterViewMode.Normal);
            else SetWorkloadViewMode(WorkloadViewMode.Normal);
        }

        private void SetActiveModuleCustom()
        {
            if (IsRamLimiterTab) SetViewMode(RamLimiterViewMode.Custom);
            else SetWorkloadViewMode(WorkloadViewMode.Custom);
        }
        #endregion

        #region 1. RAM LIMITER
        public bool IsNormalView => _ramEngine.Config.ViewMode == RamLimiterViewMode.Normal;
        public bool IsCustomView => _ramEngine.Config.ViewMode == RamLimiterViewMode.Custom;
        public string ActiveViewModeName => IsNormalView ? "NORMAL (AUTOMATIC)" : "CUSTOM (TARGETED)";

        public bool IsMasterEnabled => _ramEngine.Config.IsEnabled;
        public string MasterSwitchText => IsMasterEnabled ? "ON" : "OFF";
        public string MasterSwitchBrush => IsMasterEnabled ? "#10B981" : "#6B7280";

        public RamLimiterMode NormalMode => _ramEngine.Config.NormalMode;
        public bool IsAutoMode => NormalMode == RamLimiterMode.Auto;
        public bool IsLightMode => NormalMode == RamLimiterMode.Light;
        public bool IsAggressiveMode => NormalMode == RamLimiterMode.Aggressive;

        public string ActiveModeDisplay => NormalMode switch
        {
            RamLimiterMode.Light => "LIGHT ACTIVE",
            RamLimiterMode.Aggressive => "AGGRESSIVE ACTIVE",
            _ => "AUTO ACTIVE"
        };

        public string ActiveModeHeader => NormalMode switch
        {
            RamLimiterMode.Light => "LIGHT",
            RamLimiterMode.Aggressive => "AGGRESSIVE",
            _ => "AUTO"
        };

        public bool ForegroundProtection => _ramEngine.Config.ForegroundProtection;
        public string ForegroundProtectionText => ForegroundProtection ? "PROTECTED (ON)" : "DISABLED (OFF)";

        public string SystemRamStatus => $"{_ramEngine.SystemUsedRamMb / 1024.0:F1} / {_ramEngine.SystemTotalRamMb / 1024.0:F1} GB";
        public string TotalSystemRamDisplay => $"{_ramEngine.SystemTotalRamMb / 1024.0:F1} GB";
        public string UsedSystemRamDisplay => $"{_ramEngine.SystemUsedRamMb / 1024.0:F1} GB";
        public double SystemRamPercent => _ramEngine.SystemRamUsagePercent;
        public string SystemRamPercentDisplay => $"{_ramEngine.SystemRamUsagePercent:F0}%";

        public string MemoryPressureText => _ramEngine.PressureLevelText;
        public string MemoryPressureBrush => _ramEngine.PressureLevelBrush;

        public int HighRamAppsCount => _ramEngine.AutoDetectedGroups.Count(g => g.Status == RamAppStatus.HighRam || g.Status == RamAppStatus.NearLimit);
        public int ActiveCustomTargetsCount => _ramEngine.ActiveCustomTargets.Count(t => t.IsEnabled);
        public string CustomTargetsStatusText => $"{ActiveCustomTargetsCount} ACTIVE";
        public int AutoActionCount => _ramEngine.AutoActionCount;

        public ObservableCollection<TargetProcessGroup> AutoDetectedGroups { get; } = new();
        public ObservableCollection<CustomTargetItem> CustomTargets { get; } = new();
        public ObservableCollection<SearchableAppItem> SearchResults { get; } = new();
        public ObservableCollection<RamLimiterLogEntry> ActionLogs { get; } = new();

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set { if (_searchQuery != value) { _searchQuery = value; OnPropertyChanged(); PerformSearch(); } }
        }

        private SearchableAppItem? _selectedSearchApp;
        public SearchableAppItem? SelectedSearchApp
        {
            get => _selectedSearchApp;
            set
            {
                _selectedSearchApp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedApp));
                OnPropertyChanged(nameof(SelectedAppName));
                OnPropertyChanged(nameof(SelectedAppExe));
                OnPropertyChanged(nameof(SelectedAppPath));
            }
        }

        public bool HasSelectedApp => SelectedSearchApp != null;
        public string SelectedAppName => SelectedSearchApp?.DisplayName ?? "No application selected";
        public string SelectedAppExe => SelectedSearchApp?.ExecutableName ?? "";
        public string SelectedAppPath => SelectedSearchApp?.ExecutablePath ?? "";

        private double _customRamLimitMb = 1500;
        public double CustomRamLimitMb { get => _customRamLimitMb; set { _customRamLimitMb = value; OnPropertyChanged(); } }

        public ICommand ToggleMasterSwitchCommand { get; }
        public ICommand SetViewModeNormalCommand { get; }
        public ICommand SetViewModeCustomCommand { get; }
        public ICommand SetAutoModeCommand { get; }
        public ICommand SetLightModeCommand { get; }
        public ICommand SetAggressiveModeCommand { get; }
        public ICommand SelectSearchAppCommand { get; }
        public ICommand EnableCustomTargetCommand { get; }
        public ICommand ToggleCustomTargetCommand { get; }
        public ICommand RemoveCustomTargetCommand { get; }
        public ICommand ToggleForegroundProtectionCommand { get; }

        private void ToggleMasterSwitch()
        {
            if (IsMasterEnabled) _ramEngine.Stop();
            else _ramEngine.Start();
            RefreshUiState();
        }

        private void SetViewMode(RamLimiterViewMode mode)
        {
            _ramEngine.SetViewMode(mode);
            RefreshUiState();
        }

        private void SetNormalMode(RamLimiterMode mode)
        {
            _ramEngine.SetNormalMode(mode);
            RefreshUiState();
        }

        private void ToggleForegroundProtection()
        {
            _ramEngine.SetForegroundProtection(!ForegroundProtection);
            RefreshUiState();
        }

        private void SelectSearchApp(SearchableAppItem? app)
        {
            if (app == null) return;
            if (SelectedSearchApp == app)
            {
                SelectedSearchApp = null;
            }
            else
            {
                SelectedSearchApp = app;
                CustomRamLimitMb = app.CurrentRamMb > 500 ? Math.Round(app.CurrentRamMb * 0.75) : 1500;
            }
            
            // Trigger UI update for search results
            foreach (var item in SearchResults)
            {
                item.IsSelected = (item == SelectedSearchApp);
            }
        }

        private void EnableCustomTarget()
        {
            if (SelectedSearchApp == null) return;
            _ramEngine.AddOrUpdateCustomTarget(new CustomTargetItem
            {
                DisplayName = SelectedSearchApp.DisplayName,
                ExecutableName = SelectedSearchApp.ExecutableName,
                ExecutablePath = SelectedSearchApp.ExecutablePath,
                RamLimitMb = CustomRamLimitMb > 50 ? CustomRamLimitMb : 1500,
                IsEnabled = true,
                ForegroundProtection = ForegroundProtection
            });
            RefreshUiState();
        }

        private void ToggleCustomTarget(CustomTargetItem? item)
        {
            if (item == null) return;
            _ramEngine.ToggleCustomTarget(item.Id);
            RefreshUiState();
        }

        private void RemoveCustomTarget(CustomTargetItem? item)
        {
            if (item == null) return;
            _ramEngine.RemoveCustomTarget(item.Id);
            RefreshUiState();
        }

        public ICommand BrowseCustomRamAppCommand { get; }

        private void BrowseCustomRamApp()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Application Executable for RAM Limiter",
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    string path = dialog.FileName;
                    string exeName = System.IO.Path.GetFileNameWithoutExtension(path);
                    string displayName = exeName;
                    try
                    {
                        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(vi.ProductName) && !vi.ProductName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        {
                            displayName = vi.ProductName;
                        }
                    }
                    catch { }

                    _ramEngine.AddOrUpdateCustomTarget(new CustomTargetItem
                    {
                        DisplayName = displayName,
                        ExecutableName = exeName,
                        ExecutablePath = path,
                        RamLimitMb = CustomRamLimitMb > 50 ? CustomRamLimitMb : 1500,
                        IsEnabled = true,
                        ForegroundProtection = ForegroundProtection
                    });

                    // Unified application identity across Workload and RAM limiter
                    _workloadEngine.AddCustomWorkloadApp(path, displayName, WorkloadType.Gaming);

                    RefreshUiState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiOptimizationViewModel] Error browsing RAM app: {ex.Message}");
            }
        }

        private void PerformSearch()
        {
            var results = _ramEngine.SearchApplications(SearchQuery);
            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(r);
        }
        #endregion

        #region 2. WORKLOAD OPTIMIZATION
        public bool IsWorkloadEnabled => _workloadEngine.Config.IsEnabled;
        public string WorkloadMasterSwitchText => IsWorkloadEnabled ? "ON" : "OFF";
        public string WorkloadMasterSwitchBrush => IsWorkloadEnabled ? "#10B981" : "#6B7280";

        // View Mode: Normal vs Custom
        public bool IsWorkloadNormalView => _workloadEngine.Config.ViewMode == WorkloadViewMode.Normal;
        public bool IsWorkloadCustomView => _workloadEngine.Config.ViewMode == WorkloadViewMode.Custom;
        public string ActiveWorkloadViewModeName => IsWorkloadNormalView ? "NORMAL (AUTOMATIC)" : "CUSTOM (PER-APP RULES)";

        // Normal Modes: Auto vs Light vs Aggressive
        public WorkloadNormalMode WorkloadNormalMode => _workloadEngine.Config.NormalMode;
        public bool IsWorkloadAutoMode => WorkloadNormalMode == WorkloadNormalMode.Auto;
        public bool IsWorkloadLightMode => WorkloadNormalMode == WorkloadNormalMode.Light;
        public bool IsWorkloadAggressiveMode => WorkloadNormalMode == WorkloadNormalMode.Aggressive;

        public string ActiveWorkloadModeDisplay => WorkloadNormalMode switch
        {
            WorkloadNormalMode.Light => "LIGHT ACTIVE",
            WorkloadNormalMode.Aggressive => "AGGRESSIVE ACTIVE",
            _ => "AUTO ACTIVE"
        };

        public string ActiveWorkloadModeHeader => WorkloadNormalMode switch
        {
            WorkloadNormalMode.Light => "LIGHT",
            WorkloadNormalMode.Aggressive => "AGGRESSIVE",
            _ => "AUTO"
        };

        public string WorkloadStatusText => _workloadEngine.StatusText;
        public string WorkloadStatusBrush => _workloadEngine.StatusBrush;

        // Custom Auto Apply Switch
        public bool CustomAutoApply => _workloadEngine.Config.CustomAutoApply;
        public string CustomAutoApplyText => CustomAutoApply ? "AUTO APPLY: ON" : "AUTO APPLY: OFF";
        public string CustomAutoApplyBrush => CustomAutoApply ? "#10B981" : "#6B7280";

        // Multi-Workload Live Active Sessions
        public ObservableCollection<RunningWorkloadSession> ActiveWorkloadSessions { get; } = new();
        public int ActiveWorkloadsCount => ActiveWorkloadSessions.Count;
        public string ActiveWorkloadCountHeader => $"{ActiveWorkloadsCount} ACTIVE";
        public bool HasActiveWorkloads => ActiveWorkloadsCount > 0;

        // Real Backend Engine & Startup States
        public WorkloadInfo CurrentWorkload => _workloadEngine.CurrentWorkload;
        public string WorkloadDisplayName => CurrentWorkload.DisplayName;
        public string WorkloadVersionDisplay => HasRunningWorkload && !string.IsNullOrEmpty(CurrentWorkload.Version) && CurrentWorkload.Version != "N/A" ? $"v{CurrentWorkload.Version}" : "";
        public string WorkloadTypeDisplay => CurrentWorkload.TypeDisplay;
        public string WorkloadLauncherOrigin => CurrentWorkload.LauncherOrigin;
        public string WorkloadForegroundDisplay => CurrentWorkload.ForegroundDisplay;
        public string WorkloadFullscreenDisplay => CurrentWorkload.FullscreenDisplay;
        public string WorkloadConfidenceText => $"{CurrentWorkload.ConfidenceScore}%";
        public string WorkloadProcessCountDisplay => CurrentWorkload.ProcessCount > 0 ? $"{CurrentWorkload.ProcessCount} active {(CurrentWorkload.ProcessCount == 1 ? "process" : "processes")}" : "0 active processes";
        public string WorkloadProfileSummary => _workloadEngine.ActiveProfileSummary;
        public string TargetRamTelemetry => _workloadEngine.TargetRamDisplay;

        public bool HasRunningWorkload => _workloadEngine.Status == WorkloadEngineStatus.Optimized && CurrentWorkload.PrimaryPid > 0 && CurrentWorkload.Type != WorkloadType.All && CurrentWorkload.Type != WorkloadType.GeneralPerformance;
        public string WorkloadBoostStatusDisplay => HasRunningWorkload ? (IsWorkloadCustomView ? "CUSTOM PROFILE ACTIVE" : "PERFORMANCE BOOST ACTIVE") : "NOT RUNNING";
        public string AutoOptimizationToggleText => IsWorkloadEnabled ? "AUTO OPTIMIZATION: ON" : "AUTO OPTIMIZATION: OFF";
        public string AutoOptimizationToggleBrush => IsWorkloadEnabled ? "#10B981" : "#6B7280";
        public string WorkloadEngineRunningText => IsWorkloadEnabled ? "● ENGINE RUNNING" : "○ ENGINE STOPPED";
        public string WorkloadStartupText => IsWorkloadEnabled ? "● ENABLED — STARTS AUTOMATICALLY" : "○ DISABLED — WILL NOT START";
        public string RamEngineRunningText => IsMasterEnabled ? "● ENGINE RUNNING" : "○ ENGINE STOPPED";
        public string RamStartupText => IsMasterEnabled ? "● ENABLED — STARTS AUTOMATICALLY" : "○ DISABLED — WILL NOT START";

        public int VerifiedActionsCount => WorkloadPlan.Count(p => p.Verified);
        public int TotalPlanActionsCount => WorkloadPlan.Count;
        public string PerformanceBoostSummaryText => TotalPlanActionsCount > 0 ? $"{VerifiedActionsCount} OF {TotalPlanActionsCount} OPTIMIZATIONS VERIFIED & ACTIVE" : "WAITING FOR WORKLOAD";

        // Inventory Category Filter
        private WorkloadType _selectedCategory = WorkloadType.All;
        public WorkloadType SelectedCategory
        {
            get => _selectedCategory;
            set { if (_selectedCategory != value) { _selectedCategory = value; OnPropertyChanged(); UpdateFilteredInventory(); } }
        }

        public bool IsCategoryAll => SelectedCategory == WorkloadType.All;
        public bool IsCategoryGames => SelectedCategory == WorkloadType.Gaming;
        public bool IsCategoryCreative => SelectedCategory == WorkloadType.Creative;
        public bool IsCategoryVideoAi => SelectedCategory == WorkloadType.VideoAi;
        public bool IsCategoryEmulators => SelectedCategory == WorkloadType.Emulator;
        public bool IsCategoryRecording => SelectedCategory == WorkloadType.Recording;

        // Live Inventory Search Query
        private string _inventorySearchQuery = string.Empty;
        public string InventorySearchQuery
        {
            get => _inventorySearchQuery;
            set { if (_inventorySearchQuery != value) { _inventorySearchQuery = value; OnPropertyChanged(); UpdateFilteredInventory(); } }
        }

        // Custom View Selection State
        private InstalledWorkloadItem? _selectedCustomApp;
        public InstalledWorkloadItem? SelectedCustomApp
        {
            get => _selectedCustomApp;
            set
            {
                if (_selectedCustomApp != null) _selectedCustomApp.IsSelected = false;
                _selectedCustomApp = value;
                if (_selectedCustomApp != null) _selectedCustomApp.IsSelected = true;

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedCustomApp));
                OnPropertyChanged(nameof(CustomSelectedAppName));
                OnPropertyChanged(nameof(CustomSelectedAppVersion));
                OnPropertyChanged(nameof(CustomSelectedAppType));
                OnPropertyChanged(nameof(CustomSelectedAppExe));
                OnPropertyChanged(nameof(CustomSelectedAppPath));
                OnPropertyChanged(nameof(CustomSelectedAppLauncher));
                OnPropertyChanged(nameof(CustomSelectedAppStatus));
                LoadCustomAppOptions();
            }
        }

        public bool HasSelectedCustomApp => SelectedCustomApp != null;
        public string CustomSelectedAppName => SelectedCustomApp?.DisplayName ?? "No Application Selected";
        public string CustomSelectedAppVersion => SelectedCustomApp?.Version ?? "N/A";
        public string CustomSelectedAppType => SelectedCustomApp?.TypeDisplay ?? "N/A";
        public string CustomSelectedAppExe => SelectedCustomApp?.ExecutableName ?? "";
        public string CustomSelectedAppPath => SelectedCustomApp?.InstallationPath ?? "";
        public string CustomSelectedAppLauncher => SelectedCustomApp?.Launcher ?? "Standalone";
        public string CustomSelectedAppStatus => SelectedCustomApp?.IsRunning == true ? "RUNNING" : "INSTALLED";

        // Summary Counts for Custom Profile
        public int SelectedOptimizationTotalCount => AvailableOptions.Count(o => o.IsEnabled);
        public int CpuOptionsCount => AvailableOptions.Count(o => o.Category == "CPU" && o.IsEnabled);
        public int MemoryOptionsCount => AvailableOptions.Count(o => o.Category == "MEMORY" && o.IsEnabled);
        public int IoOptionsCount => AvailableOptions.Count(o => o.Category == "I/O" && o.IsEnabled);
        public int PowerOptionsCount => AvailableOptions.Count(o => o.Category == "POWER" && o.IsEnabled);
        public int GpuOptionsCount => AvailableOptions.Count(o => o.Category == "GPU" && o.IsEnabled);
        public int BackgroundOptionsCount => AvailableOptions.Count(o => o.Category == "BACKGROUND" && o.IsEnabled);

        // Collections
        public ObservableCollection<InstalledWorkloadItem> FilteredInventory { get; } = new();
        public ObservableCollection<InstalledWorkloadItem> RunningWorkloads { get; } = new();
        public ObservableCollection<WorkloadActionPlanItem> WorkloadPlan { get; } = new();
        public ObservableCollection<WorkloadSessionHistoryItem> WorkloadHistory { get; } = new();
        public ObservableCollection<CustomWorkloadOptimizationOption> AvailableOptions { get; } = new();
        public ObservableCollection<CustomAppProfile> SavedProfiles { get; } = new();

        public ICommand ToggleWorkloadMasterSwitchCommand { get; }
        public ICommand SetWorkloadViewNormalCommand { get; }
        public ICommand SetWorkloadViewCustomCommand { get; }
        public ICommand SetWorkloadAutoModeCommand { get; }
        public ICommand SetWorkloadLightModeCommand { get; }
        public ICommand SetWorkloadAggressiveModeCommand { get; }
        public ICommand ToggleCustomAutoApplyCommand { get; }
        public ICommand SetCategoryAllCommand { get; }
        public ICommand SetCategoryGamesCommand { get; }
        public ICommand SetCategoryCreativeCommand { get; }
        public ICommand SetCategoryVideoAiCommand { get; }
        public ICommand SetCategoryEmulatorsCommand { get; }
        public ICommand SetCategoryRecordingCommand { get; }
        public ICommand SelectCustomAppCommand { get; }
        public ICommand BrowseExecutableForWorkloadCommand { get; }
        public ICommand AddCustomWorkloadAppCommand { get; }
        public ICommand SaveCustomAppProfileCommand { get; }
        public ICommand DeleteCustomProfileCommand { get; }
        public ICommand ToggleCustomProfileCommand { get; }
        public ICommand RescanInventoryCommand { get; }
        public ICommand RefreshCommand { get; }

        private void BrowseExecutableForWorkload(InstalledWorkloadItem? item)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = item != null ? $"Select Executable for {item.DisplayName}" : "Select Application Executable",
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    string targetName = item?.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                    _workloadEngine.SetCustomExecutableForWorkload(targetName, dialog.FileName);
                    UpdateFilteredInventory();
                    RefreshUiState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiOptimizationViewModel] Error browsing executable: {ex.Message}");
            }
        }

        private void AddCustomWorkloadApp()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Application / Game Executable to Optimize",
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    _workloadEngine.AddCustomWorkloadApp(dialog.FileName);
                    UpdateFilteredInventory();
                    RefreshUiState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiOptimizationViewModel] Error adding custom app: {ex.Message}");
            }
        }

        private void RescanInventory()
        {
            _workloadEngine.RescanInventory();
            RefreshUiState();
        }

        private void ToggleWorkloadMasterSwitch()
        {
            if (IsWorkloadEnabled) _workloadEngine.Stop();
            else _workloadEngine.Start();
            RefreshUiState();
        }

        private void SetWorkloadViewMode(WorkloadViewMode mode)
        {
            _workloadEngine.SetViewMode(mode);
            RefreshUiState();
        }

        private void SetWorkloadNormalMode(WorkloadNormalMode mode)
        {
            _workloadEngine.SetNormalMode(mode);
            RefreshUiState();
        }

        private void ToggleCustomAutoApply()
        {
            _workloadEngine.ToggleCustomAutoApply(!CustomAutoApply);
            RefreshUiState();
        }

        private void SetInventoryCategory(WorkloadType cat)
        {
            SelectedCategory = cat;
            OnPropertyChanged(nameof(IsCategoryAll));
            OnPropertyChanged(nameof(IsCategoryGames));
            OnPropertyChanged(nameof(IsCategoryCreative));
            OnPropertyChanged(nameof(IsCategoryVideoAi));
            OnPropertyChanged(nameof(IsCategoryEmulators));
            OnPropertyChanged(nameof(IsCategoryRecording));
        }

        private void SelectCustomApp(InstalledWorkloadItem? app)
        {
            if (app == null) return;
            if (SelectedCustomApp != null && (SelectedCustomApp == app || (SelectedCustomApp.InstallationPath == app.InstallationPath && SelectedCustomApp.DisplayName == app.DisplayName)))
            {
                SelectedCustomApp = null;
            }
            else
            {
                SelectedCustomApp = app;
            }

            foreach (var item in FilteredInventory)
            {
                item.IsSelected = (SelectedCustomApp != null && item.InstallationPath == SelectedCustomApp.InstallationPath && item.DisplayName == SelectedCustomApp.DisplayName);
            }
        }

        private void LoadCustomAppOptions()
        {
            AvailableOptions.Clear();
            if (SelectedCustomApp == null) return;

            var existing = _workloadEngine.CustomProfiles.FirstOrDefault(p =>
                p.InstallationPath.Equals(SelectedCustomApp.InstallationPath, StringComparison.OrdinalIgnoreCase) ||
                (p.ExecutableName.Equals(SelectedCustomApp.ExecutableName, StringComparison.OrdinalIgnoreCase) &&
                 p.ApplicationName.Equals(SelectedCustomApp.DisplayName, StringComparison.OrdinalIgnoreCase)));

            var allTemplateOptions = new List<CustomWorkloadOptimizationOption>
            {
                new() { Key = "cpu_prio", Category = "CPU", Name = "High CPU Priority", Description = "Elevates process thread scheduling priority to High.", Layer = "Process Priority", CurrentState = "Normal", TargetState = "High", Risk = "Low", IsEnabled = true },
                new() { Key = "cpu_thread", Category = "CPU", Name = "Thread Priority Boost", Description = "Enables dynamic thread boost for active execution pipelines.", Layer = "Thread Scheduling", CurrentState = "Disabled", TargetState = "Boosted", Risk = "Low", IsEnabled = true },
                new() { Key = "mem_prio", Category = "MEMORY", Name = "Foreground Memory Priority", Description = "Sets prioritized memory working-set allocation for the active window.", Layer = "Memory Priority", CurrentState = "Standard", TargetState = "Foreground Priority", Risk = "Low", IsEnabled = true },
                new() { Key = "io_prio", Category = "I/O", Name = "High I/O Priority", Description = "Elevates disk & storage pipeline read/write queue priority.", Layer = "I/O Prioritization", CurrentState = "Normal", TargetState = "High I/O", Risk = "Low", IsEnabled = true },
                new() { Key = "power_plan", Category = "POWER", Name = "High Performance Power Plan", Description = "Engages High Performance clock residency while this application runs.", Layer = "Power Scheme", CurrentState = "Balanced", TargetState = "High Performance", Risk = "Low", IsEnabled = true },
                new() { Key = "gpu_pref", Category = "GPU", Name = "High Performance GPU Preference", Description = "Instructs Windows Graphics to render on the Discrete High-Performance GPU.", Layer = "GPU Preference", CurrentState = "Default", TargetState = "High Performance GPU", Risk = "Low", IsEnabled = true },
                new() { Key = "bg_reduce", Category = "BACKGROUND", Name = "Background Priority Reduction", Description = "De-prioritizes competing background applications to free execution cycles.", Layer = "Background Contention", CurrentState = "Normal", TargetState = "Reduced Contention", Risk = "Low", IsEnabled = false }
            };

            foreach (var opt in allTemplateOptions)
            {
                if (existing != null)
                {
                    var match = existing.Options.FirstOrDefault(o => o.Key == opt.Key);
                    if (match != null) opt.IsEnabled = match.IsEnabled;
                }
                opt.PropertyChanged += (s, e) => UpdateOptionCounts();
                AvailableOptions.Add(opt);
            }

            UpdateOptionCounts();
        }

        private void UpdateOptionCounts()
        {
            OnPropertyChanged(nameof(SelectedOptimizationTotalCount));
            OnPropertyChanged(nameof(CpuOptionsCount));
            OnPropertyChanged(nameof(MemoryOptionsCount));
            OnPropertyChanged(nameof(IoOptionsCount));
            OnPropertyChanged(nameof(PowerOptionsCount));
            OnPropertyChanged(nameof(GpuOptionsCount));
            OnPropertyChanged(nameof(BackgroundOptionsCount));
        }

        private void SaveCustomAppProfile()
        {
            if (SelectedCustomApp == null) return;

            var profile = new CustomAppProfile
            {
                ApplicationName = SelectedCustomApp.DisplayName,
                ExecutableName = SelectedCustomApp.ExecutableName,
                InstallationPath = SelectedCustomApp.InstallationPath,
                Version = SelectedCustomApp.Version,
                Type = SelectedCustomApp.Type,
                Launcher = SelectedCustomApp.Launcher,
                IsCustomProfileEnabled = true,
                Options = AvailableOptions.ToList()
            };

            _workloadEngine.AddOrUpdateCustomProfile(profile);
            RefreshUiState();
        }

        private void DeleteCustomProfile(CustomAppProfile? profile)
        {
            if (profile == null) return;
            _workloadEngine.RemoveCustomProfile(profile.Id);
            RefreshUiState();
        }

        private void ToggleCustomProfile(CustomAppProfile? profile)
        {
            if (profile == null) return;
            profile.IsCustomProfileEnabled = !profile.IsCustomProfileEnabled;
            _workloadEngine.SaveConfiguration();
            RefreshUiState();
        }

        private void UpdateFilteredInventory()
        {
            UiDispatcher.Run(() =>
            {
                FilteredInventory.Clear();
                var q = (InventorySearchQuery ?? string.Empty).Trim();

                var snapshot = _workloadEngine.InstalledInventory.ToList();
                var query = snapshot.Where(i => 
                    !WorkloadOptimizationEngine.IsTestOrMockIdentifier(i.DisplayName, i.InstallationPath) &&
                    !WorkloadOptimizationEngine.IsTestOrMockIdentifier(i.ExecutableName, i.InstallationPath));

                if (SelectedCategory == WorkloadType.Gaming)
                {
                    query = query.Where(i => i.Type == WorkloadType.Gaming);
                }
                else if (SelectedCategory == WorkloadType.Creative)
                {
                    query = query.Where(i => i.Type == WorkloadType.Creative || i.Type == WorkloadType.Recording);
                }
                else if (SelectedCategory == WorkloadType.VideoAi)
                {
                    query = query.Where(i => i.Type == WorkloadType.VideoAi);
                }
                else if (SelectedCategory == WorkloadType.Emulator)
                {
                    query = query.Where(i => i.Type == WorkloadType.Emulator);
                }
                else
                {
                    query = query.Where(i => i.Type == WorkloadType.Gaming || i.Type == WorkloadType.Creative || i.Type == WorkloadType.VideoAi || i.Type == WorkloadType.Emulator || i.Type == WorkloadType.Recording);
                }

                if (!string.IsNullOrEmpty(q))
                {
                    query = query.Where(i =>
                        (!string.IsNullOrEmpty(i.DisplayName) && i.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(i.ExecutableName) && i.ExecutableName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(i.Version) && i.Version.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(i.Launcher) && i.Launcher.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(i.InstallationPath) && i.InstallationPath.Contains(q, StringComparison.OrdinalIgnoreCase)));
                }

                foreach (var item in query)
                {
                    item.IsSelected = (SelectedCustomApp != null && item.InstallationPath == SelectedCustomApp.InstallationPath && item.DisplayName == SelectedCustomApp.DisplayName);
                    FilteredInventory.Add(item);
                }
            });
        }
        #endregion

        public string ProTipText
        {
            get
            {
                if (IsWorkloadTab)
                {
                    if (IsWorkloadCustomView)
                    {
                        return "Pro Tip: Custom mode lets you choose exactly which optimizations are allowed for each application.";
                    }

                    return WorkloadNormalMode switch
                    {
                        WorkloadNormalMode.Light => "Pro Tip: LIGHT mode provides conservative workload optimization with system-wide stability prioritized.",
                        WorkloadNormalMode.Aggressive => "Pro Tip: AGGRESSIVE mode enforces maximum resource priority. Note: More aggressive optimization prioritizes the active workload and may reduce background responsiveness.",
                        _ => "Pro Tip: Auto mode detects the active workload and applies a hardware-aware performance profile automatically."
                    };
                }

                return IsNormalView ? (NormalMode switch
                {
                    RamLimiterMode.Light => "Pro Tip: LIGHT mode provides conservative RAM control with application stability prioritized.",
                    RamLimiterMode.Aggressive => "Pro Tip: AGGRESSIVE mode enforces stronger RAM control. Note: More aggressive RAM control may affect application stability.",
                    _ => "Pro Tip: AUTO mode is recommended. It adapts memory control to current system pressure instead of forcing the same RAM limit on every application."
                }) : "Pro Tip: Custom mode only controls the specific applications you enable above. All other processes remain untouched.";
            }
        }

        public void RefreshUiState()
        {
            _ramEngine.ScanAndEnforce();
            _workloadEngine.DetectAndProcessWorkload();

            // Top Mode Switcher Properties
            OnPropertyChanged(nameof(CurrentActiveModuleModeText));
            OnPropertyChanged(nameof(CurrentActiveModuleModeToggleText));
            OnPropertyChanged(nameof(CurrentActiveModuleDescription));
            OnPropertyChanged(nameof(CurrentActiveModuleIsNormal));
            OnPropertyChanged(nameof(CurrentActiveModuleIsCustom));

            // RAM Limiter Properties
            OnPropertyChanged(nameof(IsMasterEnabled));
            OnPropertyChanged(nameof(MasterSwitchText));
            OnPropertyChanged(nameof(MasterSwitchBrush));
            OnPropertyChanged(nameof(IsNormalView));
            OnPropertyChanged(nameof(IsCustomView));
            OnPropertyChanged(nameof(ActiveViewModeName));
            OnPropertyChanged(nameof(NormalMode));
            OnPropertyChanged(nameof(IsAutoMode));
            OnPropertyChanged(nameof(IsLightMode));
            OnPropertyChanged(nameof(IsAggressiveMode));
            OnPropertyChanged(nameof(ActiveModeDisplay));
            OnPropertyChanged(nameof(ActiveModeHeader));
            OnPropertyChanged(nameof(ForegroundProtection));
            OnPropertyChanged(nameof(ForegroundProtectionText));
            OnPropertyChanged(nameof(SystemRamStatus));
            OnPropertyChanged(nameof(TotalSystemRamDisplay));
            OnPropertyChanged(nameof(UsedSystemRamDisplay));
            OnPropertyChanged(nameof(SystemRamPercent));
            OnPropertyChanged(nameof(SystemRamPercentDisplay));
            OnPropertyChanged(nameof(MemoryPressureText));
            OnPropertyChanged(nameof(MemoryPressureBrush));
            OnPropertyChanged(nameof(HighRamAppsCount));
            OnPropertyChanged(nameof(ActiveCustomTargetsCount));
            OnPropertyChanged(nameof(CustomTargetsStatusText));
            OnPropertyChanged(nameof(AutoActionCount));

            // Workload Properties
            OnPropertyChanged(nameof(IsWorkloadEnabled));
            OnPropertyChanged(nameof(WorkloadMasterSwitchText));
            OnPropertyChanged(nameof(WorkloadMasterSwitchBrush));
            OnPropertyChanged(nameof(IsWorkloadNormalView));
            OnPropertyChanged(nameof(IsWorkloadCustomView));
            OnPropertyChanged(nameof(ActiveWorkloadViewModeName));
            OnPropertyChanged(nameof(WorkloadNormalMode));
            OnPropertyChanged(nameof(IsWorkloadAutoMode));
            OnPropertyChanged(nameof(IsWorkloadLightMode));
            OnPropertyChanged(nameof(IsWorkloadAggressiveMode));
            OnPropertyChanged(nameof(ActiveWorkloadModeDisplay));
            OnPropertyChanged(nameof(ActiveWorkloadModeHeader));
            OnPropertyChanged(nameof(WorkloadStatusText));
            OnPropertyChanged(nameof(WorkloadStatusBrush));
            OnPropertyChanged(nameof(CustomAutoApply));
            OnPropertyChanged(nameof(CustomAutoApplyText));
            OnPropertyChanged(nameof(CustomAutoApplyBrush));
            OnPropertyChanged(nameof(CurrentWorkload));
            OnPropertyChanged(nameof(WorkloadDisplayName));
            OnPropertyChanged(nameof(WorkloadVersionDisplay));
            OnPropertyChanged(nameof(WorkloadTypeDisplay));
            OnPropertyChanged(nameof(WorkloadLauncherOrigin));
            OnPropertyChanged(nameof(WorkloadForegroundDisplay));
            OnPropertyChanged(nameof(WorkloadFullscreenDisplay));
            OnPropertyChanged(nameof(WorkloadConfidenceText));
            OnPropertyChanged(nameof(WorkloadProcessCountDisplay));
            OnPropertyChanged(nameof(WorkloadProfileSummary));
            OnPropertyChanged(nameof(TargetRamTelemetry));
            OnPropertyChanged(nameof(HasRunningWorkload));
            OnPropertyChanged(nameof(WorkloadBoostStatusDisplay));
            OnPropertyChanged(nameof(AutoOptimizationToggleText));
            OnPropertyChanged(nameof(AutoOptimizationToggleBrush));
            OnPropertyChanged(nameof(WorkloadEngineRunningText));
            OnPropertyChanged(nameof(WorkloadStartupText));
            OnPropertyChanged(nameof(RamEngineRunningText));
            OnPropertyChanged(nameof(RamStartupText));
            OnPropertyChanged(nameof(VerifiedActionsCount));
            OnPropertyChanged(nameof(TotalPlanActionsCount));
            OnPropertyChanged(nameof(PerformanceBoostSummaryText));
            OnPropertyChanged(nameof(ProTipText));

            // Sync RAM Limiter Collections
            AutoDetectedGroups.Clear();
            foreach (var g in _ramEngine.AutoDetectedGroups) AutoDetectedGroups.Add(g);

            CustomTargets.Clear();
            foreach (var t in _ramEngine.ActiveCustomTargets) CustomTargets.Add(t);

            if (SearchResults.Count == 0 && string.IsNullOrEmpty(SearchQuery)) PerformSearch();

            ActionLogs.Clear();
            foreach (var log in _ramEngine.ActionLogs.Take(20)) ActionLogs.Add(log);

            // Sync Multi-Workload Live Sessions
            var currentSessions = _workloadEngine.ActiveSessions;
            for (int i = ActiveWorkloadSessions.Count - 1; i >= 0; i--)
            {
                if (!currentSessions.Any(s => s.ApplicationId == ActiveWorkloadSessions[i].ApplicationId))
                {
                    ActiveWorkloadSessions.RemoveAt(i);
                }
            }
            foreach (var cs in currentSessions)
            {
                var existing = ActiveWorkloadSessions.FirstOrDefault(s => s.ApplicationId == cs.ApplicationId);
                if (existing == null)
                {
                    ActiveWorkloadSessions.Add(cs);
                }
                else
                {
                    existing.ProcessCount = cs.ProcessCount;
                    existing.ProcessIds = cs.ProcessIds;
                    existing.PrimaryPid = cs.PrimaryPid;
                    existing.RamUsageMb = cs.RamUsageMb;
                    existing.IsForeground = cs.IsForeground;
                    existing.IsFullscreen = cs.IsFullscreen;
                    existing.ActiveProfileSummary = cs.ActiveProfileSummary;
                    existing.ActionPlan = cs.ActionPlan;
                    existing.OptimizationStatusText = cs.OptimizationStatusText;
                    existing.OptimizationStatusBrush = cs.OptimizationStatusBrush;
                    existing.NotifyUpdated();
                }
            }

            OnPropertyChanged(nameof(ActiveWorkloadSessions));
            OnPropertyChanged(nameof(ActiveWorkloadsCount));
            OnPropertyChanged(nameof(ActiveWorkloadCountHeader));
            OnPropertyChanged(nameof(HasActiveWorkloads));

            // Sync Workload Collections
            WorkloadPlan.Clear();
            foreach (var plan in _workloadEngine.CurrentPlan) WorkloadPlan.Add(plan);

            WorkloadHistory.Clear();
            foreach (var h in _workloadEngine.History.Take(15)) WorkloadHistory.Add(h);

            // Saved Custom Profiles
            SavedProfiles.Clear();
            foreach (var prof in _workloadEngine.CustomProfiles) SavedProfiles.Add(prof);

            // Running Workloads
            RunningWorkloads.Clear();
            foreach (var item in _workloadEngine.InstalledInventory.Where(i => i.IsRunning))
            {
                RunningWorkloads.Add(item);
            }

            if (FilteredInventory.Count == 0 && string.IsNullOrEmpty(InventorySearchQuery))
            {
                UpdateFilteredInventory();
            }

            if (IsPowerPlanTab && !IsPowerBusy)
            {
                _ = SyncActivePowerSchemeAsync();
            }
        }

        #region 3. POWER PLAN OPTIMIZATION
        private PowerPlanRecommendation? _recommendation;
        private string _powerOperationStatus = string.Empty;
        private bool _isPowerBusy;

        public ObservableCollection<PowerPlanItem> PowerPlans { get; } = new();
        public ObservableCollection<PowerPlanItem> AiPowerPlans { get; } = new();
        public ObservableCollection<PowerPlanItem> WindowsPowerPlans { get; } = new();
        public ObservableCollection<PowerPlanItem> InstalledBasePlans { get; } = new();

        private string _currentActivePlanName = "Balanced";
        public string CurrentActivePlanName
        {
            get => _currentActivePlanName;
            private set { _currentActivePlanName = value; OnPropertyChanged(); }
        }

        private async Task SyncActivePowerSchemeAsync()
        {
            try
            {
                var (activeGuid, activeName) = await _powerEngine.GetActiveSchemeAsync();
                string normActive = PowerPlanEngine.NormalizeGuid(activeGuid);

                UiDispatcher.Run(() =>
                {
                    bool stateChanged = false;
                    foreach (var plan in PowerPlans)
                    {
                        string normGuid = PowerPlanEngine.NormalizeGuid(plan.Guid);
                        bool shouldBeActive = !string.IsNullOrEmpty(normActive) && normGuid.Equals(normActive, StringComparison.OrdinalIgnoreCase);
                        if (plan.IsActive != shouldBeActive)
                        {
                            plan.IsActive = shouldBeActive;
                            plan.Status = shouldBeActive ? PowerPlanStatus.Active : (plan.IsInstalled ? PowerPlanStatus.Available : (plan.IsSupported ? PowerPlanStatus.AvailableToEnable : PowerPlanStatus.Unsupported));
                            stateChanged = true;
                        }
                    }

                    if (!string.IsNullOrEmpty(activeName) && _currentActivePlanName != activeName)
                    {
                        CurrentActivePlanName = activeName;
                        stateChanged = true;
                    }

                    if (stateChanged)
                    {
                        OnPropertyChanged(nameof(PowerPlans));
                        OnPropertyChanged(nameof(CurrentActivePlanGuid));
                        OnPropertyChanged(nameof(LastTargetPlanGuid));
                        OnPropertyChanged(nameof(LastChangeSourceText));
                        OnPropertyChanged(nameof(LastChangeTimestampText));
                        OnPropertyChanged(nameof(UserSelectedPlanLockSummary));

                        var logs = _powerEngine.GetChangeLog();
                        PowerPlanChangeLog.Clear();
                        foreach (var l in logs) PowerPlanChangeLog.Add(l);
                    }
                });
            }
            catch { }
        }

        public PowerPlanRecommendation? Recommendation
        {
            get => _recommendation;
            private set
            {
                _recommendation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RecommendedPlanName));
                OnPropertyChanged(nameof(RecommendationReason));
                OnPropertyChanged(nameof(ActiveWorkloadSummary));
                OnPropertyChanged(nameof(BatteryWarning));
                OnPropertyChanged(nameof(HasBatteryWarning));
                OnPropertyChanged(nameof(IsCurrentActiveOptimal));
            }
        }

        public string RecommendedPlanName => Recommendation?.RecommendedPlanName ?? "Balanced";
        public string RecommendationReason => Recommendation?.Reason ?? "Optimal hardware energy efficiency.";
        public string ActiveWorkloadSummary => Recommendation?.ActiveWorkloadSummary ?? "Standard Workload";
        public string BatteryWarning => Recommendation?.BatteryWarning ?? string.Empty;
        public bool HasBatteryWarning => !string.IsNullOrEmpty(BatteryWarning);
        public bool IsCurrentActiveOptimal => Recommendation?.IsCurrentActiveOptimal ?? false;

        public string PreviousPlanName => !string.IsNullOrEmpty(_powerEngine.PreviousActiveSchemeName) ? _powerEngine.PreviousActiveSchemeName : "None Recorded";
        public bool CanRestorePreviousPlan => !string.IsNullOrEmpty(_powerEngine.PreviousActiveSchemeGuid);

        public string CurrentActivePlanGuid => PowerPlanEngine.NormalizeGuid(_powerEngine.GetActiveSchemeNative().guid.ToString());
        public string LastTargetPlanGuid => _powerEngine.LastTargetGuid ?? "None";
        public string LastChangeSourceText => _powerEngine.LastChangeSource.ToString();
        public string LastChangeTimestampText => _powerEngine.LastChangeTimestamp?.ToString("HH:mm:ss") ?? "None";
        public string UserSelectedPlanLockSummary => _powerEngine.UserLock.IsLocked ? $"{_powerEngine.UserLock.UserSelectedPlanName} (Locked at {_powerEngine.UserLock.UserSelectedTimestamp:HH:mm:ss})" : "Unlocked (System Dynamic)";

        public ObservableCollection<PowerPlanChangeLogEntry> PowerPlanChangeLog { get; } = new();

        #region Startup Auto-Apply Configuration Properties

        private StartupPowerPlanConfig _startupConfig = new();
        public StartupPowerPlanConfig StartupConfig
        {
            get => _startupConfig;
            set
            {
                _startupConfig = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartupPowerPlanGuid));
                OnPropertyChanged(nameof(StartupPowerPlanName));
                OnPropertyChanged(nameof(IsAutoEnableOnStartup));
                OnPropertyChanged(nameof(IsContinuousEnforcementEnabled));
                OnPropertyChanged(nameof(StartupStatusText));
                OnPropertyChanged(nameof(StartupStatusBrush));
                OnPropertyChanged(nameof(StartupStatusMessage));
            }
        }

        public string StartupPowerPlanGuid => StartupConfig.StartupPowerPlanGuid;
        public string StartupPowerPlanName => !string.IsNullOrEmpty(StartupConfig.StartupPowerPlanName) ? StartupConfig.StartupPowerPlanName : "None Selected";
        public bool IsAutoEnableOnStartup => StartupConfig.AutoEnableOnStartup;
        public bool IsContinuousEnforcementEnabled => StartupConfig.ContinuousEnforcement;

        public string StartupStatusText => StartupConfig.LastStartupExecutionResult switch
        {
            "ACTIVE & VERIFIED" => "ACTIVE & VERIFIED",
            "ALREADY ACTIVE & VERIFIED" => "ACTIVE & VERIFIED",
            "READY" => IsAutoEnableOnStartup ? "READY (AUTO-ENABLE ON)" : "DISABLED",
            "STARTUP POWER PLAN UNAVAILABLE" => "PLAN UNAVAILABLE",
            "DISABLED" => "DISABLED",
            "PLAN DELETED" => "PLAN DELETED",
            _ => StartupConfig.LastStartupExecutionResult
        };

        public string StartupStatusBrush => StartupConfig.LastStartupExecutionResult switch
        {
            "ACTIVE & VERIFIED" => "#10B981",
            "ALREADY ACTIVE & VERIFIED" => "#10B981",
            "READY" => IsAutoEnableOnStartup ? "#3B82F6" : "#6B7280",
            "STARTUP POWER PLAN UNAVAILABLE" => "#EF4444",
            "STARTUP POWER PLAN FAILED" => "#EF4444",
            _ => "#F59E0B"
        };

        public string StartupStatusMessage => StartupConfig.LastStartupExecutionMessage;

        #endregion

        #region Plan Creation & Customization State

        private bool _isCreatePlanModalOpen;
        public bool IsCreatePlanModalOpen
        {
            get => _isCreatePlanModalOpen;
            set { _isCreatePlanModalOpen = value; OnPropertyChanged(); }
        }

        private string _newCustomPlanName = "Error Optimizer Custom Performance";
        public string NewCustomPlanName
        {
            get => _newCustomPlanName;
            set { _newCustomPlanName = value; OnPropertyChanged(); }
        }

        private PowerPlanItem? _selectedBasePlanForCreation;
        public PowerPlanItem? SelectedBasePlanForCreation
        {
            get => _selectedBasePlanForCreation;
            set { _selectedBasePlanForCreation = value; OnPropertyChanged(); }
        }

        private bool _isPlanEditorOpen;
        public bool IsPlanEditorOpen
        {
            get => _isPlanEditorOpen;
            set { _isPlanEditorOpen = value; OnPropertyChanged(); }
        }

        private PowerPlanItem? _editingPlan;
        public PowerPlanItem? EditingPlan
        {
            get => _editingPlan;
            set { _editingPlan = value; OnPropertyChanged(); }
        }

        #endregion

        public string PowerOperationStatus
        {
            get => _powerOperationStatus;
            set { _powerOperationStatus = value; OnPropertyChanged(); }
        }

        public bool HasPowerOperationStatus => !string.IsNullOrEmpty(_powerOperationStatus);

        public bool IsPowerBusy
        {
            get => _isPowerBusy;
            set { _isPowerBusy = value; OnPropertyChanged(); }
        }

        public HardwareAnalysisReport? HardwareProfile => Recommendation?.HardwareProfile;
        public string HardwareSummaryText => HardwareProfile?.SummaryText ?? "Detecting hardware profile...";
        public string HardwareTierText => HardwareProfile?.TierClassification ?? "NORMAL BALANCED";
        public string DetectedWorkloadText => HardwareProfile?.DetectedWorkload ?? "Standard Workload";

        public ICommand ApplyPowerPlanCommand { get; }
        public ICommand RestorePreviousPowerPlanCommand { get; }
        public ICommand EnableUltimatePerformanceCommand { get; }
        public ICommand CreateCustomAiPlanCommand { get; }
        public ICommand CreateAiMaxPerformancePlanCommand { get; }
        public ICommand CreateAiBatteryEfficiencyPlanCommand { get; }
        public ICommand DeleteCustomAiPlanCommand { get; }
        public ICommand RefreshPowerPlansCommand { get; }
        public ICommand SetStartupPowerPlanCommand { get; }
        public ICommand ToggleAutoEnableOnStartupCommand { get; }
        public ICommand ToggleContinuousEnforcementCommand { get; }
        public ICommand OpenPlanEditorCommand { get; }
        public ICommand ClosePlanEditorCommand { get; }
        public ICommand SavePlanSettingCommand { get; }
        public ICommand SaveAllPlanSettingsCommand { get; }
        public ICommand OpenCreatePlanModalCommand { get; }
        public ICommand CloseCreatePlanModalCommand { get; }
        public ICommand CreateCustomNamedPlanCommand { get; }

        public async Task RefreshPowerPlansAsync()
        {
            try
            {
                IsPowerBusy = true;
                List<PowerPlanItem>? plans = null;

                if (_ipc.IsServiceAvailable)
                {
                    try
                    {
                        var response = await _ipc.SendRequestAsync(IpcMessageType.GetPowerPlans);
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            plans = System.Text.Json.JsonSerializer.Deserialize<List<PowerPlanItem>>(response.Data, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    catch { }
                }

                if (plans == null || plans.Count == 0)
                {
                    plans = await _powerEngine.DiscoverPowerPlansAsync();
                }

                var (activeGuid, activeName) = await _powerEngine.GetActiveSchemeAsync();
                string normActive = PowerPlanEngine.NormalizeGuid(activeGuid);
                var rec = _powerEngine.GenerateRecommendation(plans);
                var startupCfg = _powerEngine.GetStartupConfig();

                UiDispatcher.Run(() =>
                {
                    StartupConfig = startupCfg;
                    PowerPlans.Clear();
                    foreach (var p in plans)
                    {
                        string normGuid = PowerPlanEngine.NormalizeGuid(p.Guid);
                        bool isActive = !string.IsNullOrEmpty(normActive) && normGuid.Equals(normActive, StringComparison.OrdinalIgnoreCase);
                        bool isStartup = startupCfg.AutoEnableOnStartup &&
                                         !string.IsNullOrEmpty(startupCfg.StartupPowerPlanGuid) &&
                                         normGuid.Equals(PowerPlanEngine.NormalizeGuid(startupCfg.StartupPowerPlanGuid), StringComparison.OrdinalIgnoreCase);

                        p.IsActive = isActive;
                        p.IsStartupPlan = isStartup;
                        p.Status = isActive ? PowerPlanStatus.Active : (p.IsInstalled ? PowerPlanStatus.Available : (p.IsSupported ? PowerPlanStatus.AvailableToEnable : PowerPlanStatus.Unsupported));
                        PowerPlans.Add(p);
                    }

                    AiPowerPlans.Clear();
                    foreach (var ap in PowerPlans.Where(x => x.IsErrorOptimizerOwned || x.IsCustomAiPlan || x.IsAiBatteryEfficiency || x.IsAiMaxPerformance))
                    {
                        AiPowerPlans.Add(ap);
                    }

                    WindowsPowerPlans.Clear();
                    foreach (var wp in PowerPlans.Where(x => !x.IsErrorOptimizerOwned && !x.IsCustomAiPlan && !x.IsAiBatteryEfficiency && !x.IsAiMaxPerformance))
                    {
                        WindowsPowerPlans.Add(wp);
                    }

                    InstalledBasePlans.Clear();
                    foreach (var bp in plans.Where(x => x.IsInstalled && x.IsSupported))
                    {
                        InstalledBasePlans.Add(bp);
                    }

                    CurrentActivePlanName = !string.IsNullOrEmpty(activeName) ? activeName : "Balanced";
                    Recommendation = rec;
                    OnPropertyChanged(nameof(PreviousPlanName));
                    OnPropertyChanged(nameof(CanRestorePreviousPlan));
                    OnPropertyChanged(nameof(CurrentActivePlanGuid));
                    OnPropertyChanged(nameof(LastTargetPlanGuid));
                    OnPropertyChanged(nameof(LastChangeSourceText));
                    OnPropertyChanged(nameof(LastChangeTimestampText));
                    OnPropertyChanged(nameof(UserSelectedPlanLockSummary));

                    var logs = _powerEngine.GetChangeLog();
                    PowerPlanChangeLog.Clear();
                    foreach (var l in logs) PowerPlanChangeLog.Add(l);

                    if ((SelectedBasePlanForCreation == null || !InstalledBasePlans.Contains(SelectedBasePlanForCreation)) && InstalledBasePlans.Count > 0)
                    {
                        SelectedBasePlanForCreation = InstalledBasePlans.FirstOrDefault(p => p.Guid.Equals(PowerPlanEngine.BalancedGuid, StringComparison.OrdinalIgnoreCase)) ?? InstalledBasePlans.First();
                    }
                });
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"Failed to read power plans: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task ApplyPowerPlanAsync(PowerPlanItem? plan)
        {
            if (plan == null || string.IsNullOrEmpty(plan.Guid)) return;

            try
            {
                IsPowerBusy = true;
                PowerOperationStatus = $"Applying power scheme '{plan.Name}' ({plan.Guid})...";

                PowerPlanOperationResult? res = null;

                if (_ipc.IsServiceAvailable)
                {
                    try
                    {
                        var response = await _ipc.SendRequestAsync(IpcMessageType.ApplyPowerPlan, plan.Guid);
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            res = System.Text.Json.JsonSerializer.Deserialize<PowerPlanOperationResult>(response.Data, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        else if (!response.Success)
                        {
                            PowerOperationStatus = $"✗ {response.ErrorMessage}";
                        }
                    }
                    catch { }
                }

                if (res == null)
                {
                    res = await _powerEngine.ApplyPowerPlanAsync(plan.Guid);
                }

                if (res != null)
                {
                    if (res.Success && res.Verified)
                    {
                        PowerOperationStatus = $"✓ {res.Message}";
                        if (IsAutoEnableOnStartup)
                        {
                            await _powerEngine.SetStartupAutoEnableAsync(plan.Guid, true);
                        }
                    }
                    else
                    {
                        PowerOperationStatus = $"✗ {res.Message}";
                    }
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error applying plan: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task SetStartupPowerPlanAsync(PowerPlanItem? plan)
        {
            if (plan == null || string.IsNullOrEmpty(plan.Guid)) return;

            try
            {
                IsPowerBusy = true;
                bool enable = !plan.IsStartupPlan;
                PowerOperationStatus = enable
                    ? $"Configuring '{plan.Name}' as startup auto-enable plan..."
                    : $"Disabling startup auto-enable for '{plan.Name}'...";

                var res = await _powerEngine.SetStartupAutoEnableAsync(plan.Guid, enable);
                if (res.Success)
                {
                    PowerOperationStatus = $"✓ {res.Message}";
                }
                else
                {
                    PowerOperationStatus = $"✗ {res.Message}";
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error updating startup power plan: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task ToggleAutoEnableOnStartupAsync()
        {
            try
            {
                IsPowerBusy = true;
                bool newSetting = !IsAutoEnableOnStartup;

                if (newSetting)
                {
                    // Target active plan or first available
                    string targetGuid = !string.IsNullOrEmpty(StartupPowerPlanGuid)
                        ? StartupPowerPlanGuid
                        : (PowerPlans.FirstOrDefault(p => p.IsActive)?.Guid ?? PowerPlans.FirstOrDefault()?.Guid ?? PowerPlanEngine.BalancedGuid);

                    var res = await _powerEngine.SetStartupAutoEnableAsync(targetGuid, true);
                    PowerOperationStatus = res.Success ? $"✓ {res.Message}" : $"✗ {res.Message}";
                }
                else
                {
                    var res = await _powerEngine.SetStartupAutoEnableAsync(string.Empty, false);
                    PowerOperationStatus = res.Success ? $"✓ {res.Message}" : $"✗ {res.Message}";
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error toggling startup auto-enable: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public void ToggleContinuousEnforcement()
        {
            try
            {
                var cfg = _powerEngine.GetStartupConfig();
                cfg.ContinuousEnforcement = !cfg.ContinuousEnforcement;
                _powerEngine.SaveStartupConfig(cfg);
                StartupConfig = cfg;
                PowerOperationStatus = cfg.ContinuousEnforcement
                    ? "✓ Continuous Power Plan Enforcement enabled."
                    : "✓ Continuous Power Plan Enforcement disabled.";
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error updating continuous enforcement: {ex.Message}";
            }
        }

        public void OpenPlanEditor(PowerPlanItem? plan)
        {
            if (plan == null) return;
            EditingPlan = plan;
            IsPlanEditorOpen = true;
        }

        public void ClosePlanEditor()
        {
            IsPlanEditorOpen = false;
            EditingPlan = null;
        }

        public async Task SavePlanSettingAsync(PowerPlanSettingItem? setting)
        {
            if (EditingPlan == null || setting == null) return;

            try
            {
                IsPowerBusy = true;
                PowerOperationStatus = $"Applying and verifying setting '{setting.Name}'...";

                var res = await _powerEngine.UpdatePlanSettingAsync(
                    EditingPlan.Guid,
                    setting.SubgroupGuid,
                    setting.SettingGuid,
                    setting.Name,
                    setting.TargetAC,
                    setting.TargetDC
                );

                if (res.Success && res.Verified)
                {
                    setting.CurrentAC = setting.TargetAC;
                    setting.CurrentDC = setting.TargetDC;
                    setting.Verified = true;
                    setting.Applied = true;
                    PowerOperationStatus = $"✓ {res.Message}";
                }
                else
                {
                    setting.Verified = false;
                    PowerOperationStatus = $"✗ {res.Message}";
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error saving setting: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task SaveAllPlanSettingsAsync()
        {
            if (EditingPlan == null) return;

            try
            {
                IsPowerBusy = true;
                int successCount = 0;
                foreach (var setting in EditingPlan.Settings)
                {
                    var res = await _powerEngine.UpdatePlanSettingAsync(
                        EditingPlan.Guid,
                        setting.SubgroupGuid,
                        setting.SettingGuid,
                        setting.Name,
                        setting.TargetAC,
                        setting.TargetDC
                    );
                    if (res.Success && res.Verified)
                    {
                        setting.CurrentAC = setting.TargetAC;
                        setting.CurrentDC = setting.TargetDC;
                        setting.Verified = true;
                        setting.Applied = true;
                        successCount++;
                    }
                }

                PowerOperationStatus = $"✓ Successfully applied and verified {successCount}/{EditingPlan.Settings.Count} settings on '{EditingPlan.Name}'.";
                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error saving settings: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public void OpenCreatePlanModal()
        {
            NewCustomPlanName = "Error Optimizer Custom Performance";
            if (SelectedBasePlanForCreation == null && PowerPlans.Count > 0)
            {
                SelectedBasePlanForCreation = PowerPlans.FirstOrDefault(p => p.Guid.Equals(PowerPlanEngine.BalancedGuid, StringComparison.OrdinalIgnoreCase)) ?? PowerPlans.First();
            }
            IsCreatePlanModalOpen = true;
        }

        public void CloseCreatePlanModal()
        {
            IsCreatePlanModalOpen = false;
        }

        public async Task CreateCustomNamedPlanAsync()
        {
            try
            {
                IsPowerBusy = true;
                IsCreatePlanModalOpen = false;
                PowerOperationStatus = $"Creating custom power plan '{NewCustomPlanName}' in Windows...";

                string baseGuid = SelectedBasePlanForCreation?.Guid ?? PowerPlanEngine.BalancedGuid;
                var res = await _powerEngine.CreateCustomPowerPlanAsync(baseGuid, NewCustomPlanName, "Custom calibrated Windows power plan");

                if (res.Success && res.Verified)
                {
                    PowerOperationStatus = $"✓ {res.Message}";
                }
                else
                {
                    PowerOperationStatus = $"✗ {res.Message}";
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error creating custom plan: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task RestorePreviousPowerPlanAsync()
        {
            try
            {
                IsPowerBusy = true;
                PowerOperationStatus = "Restoring previous power scheme...";

                PowerPlanOperationResult? res = null;

                if (_ipc.IsServiceAvailable)
                {
                    try
                    {
                        var response = await _ipc.SendRequestAsync(IpcMessageType.RestorePowerPlan);
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            res = System.Text.Json.JsonSerializer.Deserialize<PowerPlanOperationResult>(response.Data, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    catch { }
                }

                if (res == null)
                {
                    res = await _powerEngine.RestorePreviousPowerPlanAsync();
                }

                if (res != null)
                {
                    if (res.Success && res.Verified)
                    {
                        PowerOperationStatus = $"✓ {res.Message}";
                    }
                    else
                    {
                        PowerOperationStatus = $"✗ {res.Message}";
                    }
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error restoring plan: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task EnableUltimatePerformanceAsync()
        {
            try
            {
                IsPowerBusy = true;
                PowerOperationStatus = "Enabling Ultimate Performance scheme...";

                PowerPlanOperationResult? res = null;

                if (_ipc.IsServiceAvailable)
                {
                    try
                    {
                        var response = await _ipc.SendRequestAsync(IpcMessageType.EnableUltimatePerformance);
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            res = System.Text.Json.JsonSerializer.Deserialize<PowerPlanOperationResult>(response.Data, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    catch { }
                }

                if (res == null)
                {
                    res = await _powerEngine.EnableUltimatePerformanceAsync();
                }

                if (res != null)
                {
                    if (res.Success)
                    {
                        PowerOperationStatus = $"✓ {res.Message}";
                    }
                    else
                    {
                        PowerOperationStatus = $"✗ {res.Message}";
                    }
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error enabling Ultimate Performance: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task CreateAiMaxPerformancePlanAsync()
        {
            try
            {
                IsPowerBusy = true;
                PowerOperationStatus = "Configuring Error Optimizer AI — Max Performance Windows power scheme...";

                var res = await _powerEngine.CreateOrGetAiMaxPerformancePlanAsync();
                if (res != null)
                {
                    PowerOperationStatus = (res.Success && res.Verified) ? $"✓ {res.Message}" : $"✗ {res.Message}";
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error creating AI Max Performance plan: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task CreateAiBatteryEfficiencyPlanAsync()
        {
            try
            {
                IsPowerBusy = true;
                PowerOperationStatus = "Configuring Error Optimizer AI — Battery Efficiency Windows power scheme...";

                var res = await _powerEngine.CreateOrGetAiBatteryEfficiencyPlanAsync();
                if (res != null)
                {
                    PowerOperationStatus = (res.Success && res.Verified) ? $"✓ {res.Message}" : $"✗ {res.Message}";
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error creating AI Battery Efficiency plan: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        public async Task CreateCustomAiPlanAsync()
        {
            await CreateAiMaxPerformancePlanAsync();
        }

        public async Task DeleteCustomAiPlanAsync(PowerPlanItem? plan)
        {
            if (plan == null || string.IsNullOrEmpty(plan.Guid)) return;

            try
            {
                IsPowerBusy = true;
                PowerOperationStatus = $"Deleting custom power scheme '{plan.Name}'...";

                PowerPlanOperationResult? res = null;

                if (_ipc.IsServiceAvailable)
                {
                    try
                    {
                        var response = await _ipc.SendRequestAsync(IpcMessageType.DeleteCustomAiPlan, plan.Guid);
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            res = System.Text.Json.JsonSerializer.Deserialize<PowerPlanOperationResult>(response.Data, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    catch { }
                }

                if (res == null)
                {
                    res = await _powerEngine.DeleteCustomAiPlanAsync(plan.Guid);
                }

                if (res != null)
                {
                    if (res.Success)
                    {
                        PowerOperationStatus = $"✓ {res.Message}";
                    }
                    else
                    {
                        PowerOperationStatus = $"✗ {res.Message}";
                    }
                }

                await RefreshPowerPlansAsync();
            }
            catch (Exception ex)
            {
                PowerOperationStatus = $"✗ Error deleting custom plan: {ex.Message}";
            }
            finally
            {
                IsPowerBusy = false;
            }
        }

        #endregion

        public override Task OnNavigatedToAsync()
        {
            _refreshTimer.Start();
            RefreshUiState();
            if (ActiveTab == AiOptimizationActiveTab.PowerPlan)
            {
                _ = RefreshPowerPlansAsync();
            }
            return Task.CompletedTask;
        }

        public override Task OnNavigatedFromAsync()
        {
            _refreshTimer.Stop();
            return Task.CompletedTask;
        }
    }
}
