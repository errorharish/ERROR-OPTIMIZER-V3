using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.ViewModels
{
    public class ServiceEntryItemViewModel : ViewModelBase
    {
        private readonly ServiceManagerViewModel _parent;
        private string _runningState = "Stopped";
        private string _startupType = "Manual";
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isBusy;

        public ServiceEntryItemViewModel(ServiceInfoDto dto, ServiceManagerViewModel parent)
        {
            _parent = parent;
            UpdateFromDto(dto);

            ToggleDetailsCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
            StartCommand = new RelayCommand(async _ => await _parent.ExecuteServiceActionAsync(this, "Start"));
            StopCommand = new RelayCommand(async _ => await _parent.RequestStopServiceAsync(this));
            RestartCommand = new RelayCommand(async _ => await _parent.RequestRestartServiceAsync(this));
            SetStartupModeCommand = new RelayCommand(async p => 
            {
                if (p is string mode)
                    await _parent.ExecuteSetStartupTypeAsync(this, mode);
            });
        }

        public void UpdateFromDto(ServiceInfoDto dto)
        {
            ServiceName = dto.ServiceName;
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.ServiceName : dto.DisplayName;
            Description = string.IsNullOrWhiteSpace(dto.Description) ? "No description provided by service binary." : dto.Description;
            ExePath = dto.ExePath;
            Publisher = string.IsNullOrWhiteSpace(dto.Publisher) ? (dto.IsMicrosoft ? "Microsoft Corporation" : "Unknown") : dto.Publisher;
            Version = string.IsNullOrWhiteSpace(dto.Version) ? "N/A" : dto.Version;
            Category = string.IsNullOrWhiteSpace(dto.Category) ? "SYSTEM" : dto.Category.ToUpperInvariant();
            ServiceType = string.IsNullOrWhiteSpace(dto.ServiceType) ? "WIN32_OWN_PROCESS" : dto.ServiceType;
            Account = string.IsNullOrWhiteSpace(dto.Account) ? "LocalSystem" : dto.Account;
            
            // SYSTEM / SECURITY RISK (How sensitive is this service to Windows?)
            Risk = string.IsNullOrWhiteSpace(dto.Risk) ? "LOW" : dto.Risk.ToUpperInvariant();

            // ACTION SAFETY (Can I safely stop this service right now?)
            ActionSafety = string.IsNullOrWhiteSpace(dto.ActionSafety) ? "UNKNOWN" : dto.ActionSafety.ToUpperInvariant();
            ActionSafetyReason = string.IsNullOrWhiteSpace(dto.ActionSafetyReason) ? "No action safety analysis available." : dto.ActionSafetyReason;

            IsDelayedStart = dto.IsDelayedStart;
            IsCritical = dto.IsCritical;
            FileExists = dto.FileExists;
            IsMicrosoft = dto.IsMicrosoft;
            DependsOn = dto.DependsOn ?? new List<string>();
            DependentServices = dto.DependentServices ?? new List<string>();

            _runningState = dto.RunningState;
            _startupType = dto.IsDelayedStart ? "Automatic (Delayed)" : dto.StartupType;

            RefreshVisualState();
        }

        public string ServiceName { get; private set; } = string.Empty;
        public string DisplayName { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public string ExePath { get; private set; } = string.Empty;
        public string Publisher { get; private set; } = "Unknown";
        public string Version { get; private set; } = "N/A";
        public string Category { get; private set; } = "SYSTEM";
        public string ServiceType { get; private set; } = "WIN32_OWN_PROCESS";
        public string Account { get; private set; } = "LocalSystem";
        public string Risk { get; private set; } = "LOW";
        public string ActionSafety { get; private set; } = "UNKNOWN";
        public string ActionSafetyReason { get; private set; } = string.Empty;
        public bool IsDelayedStart { get; private set; }
        public bool IsCritical { get; private set; }
        public bool FileExists { get; private set; }
        public bool IsMicrosoft { get; private set; }
        public List<string> DependsOn { get; private set; } = new();
        public List<string> DependentServices { get; private set; } = new();

        public string RunningState
        {
            get => _runningState;
            set { _runningState = value; OnPropertyChanged(); RefreshVisualState(); }
        }

        public string StartupType
        {
            get => _startupType;
            set { _startupType = value; OnPropertyChanged(); RefreshVisualState(); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set 
            { 
                if (_isSelected != value)
                {
                    _isSelected = value; 
                    OnPropertyChanged(); 
                    _parent.UpdateSelectionStats();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
        }
        public bool IsNotBusy => !_isBusy;

        public bool IsRunning => RunningState.Equals("Running", StringComparison.OrdinalIgnoreCase);
        public bool IsStopped => RunningState.Equals("Stopped", StringComparison.OrdinalIgnoreCase);
        public bool IsPaused => RunningState.Equals("Paused", StringComparison.OrdinalIgnoreCase);

        // Action Safety Classification Booleans
        public bool IsSafeToStop => ActionSafety.Equals("SAFE TO STOP", StringComparison.OrdinalIgnoreCase);
        public bool IsSafeWithCaution => ActionSafety.Equals("SAFE TO STOP WITH CAUTION", StringComparison.OrdinalIgnoreCase);
        public bool IsNotRecommended => ActionSafety.Equals("NOT RECOMMENDED TO STOP", StringComparison.OrdinalIgnoreCase);
        public bool IsDoNotStop => ActionSafety.Equals("DO NOT STOP", StringComparison.OrdinalIgnoreCase);
        public bool IsUnknownSafety => ActionSafety.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);

        public bool HasDependencies => DependsOn.Count > 0;
        public bool HasDependents => DependentServices.Count > 0;
        public string DependsOnSummary => HasDependencies ? string.Join(", ", DependsOn) : "None (Standalone)";
        public string DependentsSummary => HasDependents ? string.Join(", ", DependentServices) : "None (No active dependents)";

        // Visual properties
        public Brush StatusColor { get; private set; } = GetFrozenBrush(Color.FromRgb(156, 163, 175));
        public Brush StatusBadgeBackground { get; private set; } = GetFrozenBrush(Color.FromArgb(26, 156, 163, 175));
        public string StatusBadgeText => RunningState.ToUpperInvariant();

        public Brush StartupBadgeColor { get; private set; } = GetFrozenBrush(Color.FromRgb(156, 163, 175));
        public Brush CategoryColor { get; private set; } = GetFrozenBrush(Color.FromRgb(96, 165, 250));
        public string CategoryGlyph { get; private set; } = "⚙";

        // System Risk Visuals
        public Brush RiskColor { get; private set; } = GetFrozenBrush(Color.FromRgb(16, 185, 129));
        public Brush RiskBadgeBackground { get; private set; } = GetFrozenBrush(Color.FromArgb(26, 16, 185, 129));

        // Action Safety Visuals
        public Brush ActionSafetyColor { get; private set; } = GetFrozenBrush(Color.FromRgb(16, 185, 129));
        public Brush ActionSafetyBadgeBackground { get; private set; } = GetFrozenBrush(Color.FromArgb(26, 16, 185, 129));
        public string ActionSafetyBadgeText { get; private set; } = "SAFE TO STOP";

        public ICommand ToggleDetailsCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand SetStartupModeCommand { get; }

        private void RefreshVisualState()
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(IsSafeToStop));
            OnPropertyChanged(nameof(IsSafeWithCaution));
            OnPropertyChanged(nameof(IsNotRecommended));
            OnPropertyChanged(nameof(IsDoNotStop));
            OnPropertyChanged(nameof(IsUnknownSafety));

            // Status Colors
            if (IsRunning)
            {
                StatusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129)); // Emerald Green
                StatusBadgeBackground = GetFrozenBrush(Color.FromArgb(30, 16, 185, 129));
            }
            else if (IsStopped)
            {
                StatusColor = GetFrozenBrush(Color.FromRgb(156, 163, 175)); // Muted Slate Gray
                StatusBadgeBackground = GetFrozenBrush(Color.FromArgb(20, 156, 163, 175));
            }
            else if (IsPaused)
            {
                StatusColor = GetFrozenBrush(Color.FromRgb(245, 158, 11)); // Amber
                StatusBadgeBackground = GetFrozenBrush(Color.FromArgb(30, 245, 158, 11));
            }
            else
            {
                StatusColor = GetFrozenBrush(Color.FromRgb(156, 163, 175));
                StatusBadgeBackground = GetFrozenBrush(Color.FromArgb(20, 156, 163, 175));
            }
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusBadgeBackground));

            // Startup Badge Color
            StartupBadgeColor = StartupType switch
            {
                "Automatic" or "Automatic (Delayed)" => GetFrozenBrush(Color.FromRgb(16, 185, 129)),
                "Manual" => GetFrozenBrush(Color.FromRgb(96, 165, 250)),
                "Disabled" => GetFrozenBrush(Color.FromRgb(239, 68, 68)),
                _ => GetFrozenBrush(Color.FromRgb(156, 163, 175))
            };
            OnPropertyChanged(nameof(StartupBadgeColor));

            // Category & Glyph
            (CategoryColor, CategoryGlyph) = Category switch
            {
                "SECURITY" => (GetFrozenBrush(Color.FromRgb(239, 68, 68)), "🛡️"),
                "AUDIO" => (GetFrozenBrush(Color.FromRgb(236, 72, 153)), "🎵"),
                "GRAPHICS" => (GetFrozenBrush(Color.FromRgb(168, 85, 247)), "🖥️"),
                "NETWORK" => (GetFrozenBrush(Color.FromRgb(59, 130, 246)), "🌐"),
                "GAMING" => (GetFrozenBrush(Color.FromRgb(245, 158, 11)), "🎮"),
                "DRIVER" => (GetFrozenBrush(Color.FromRgb(14, 165, 233)), "🔌"),
                "THIRD-PARTY" => (GetFrozenBrush(Color.FromRgb(192, 132, 252)), "📦"),
                _ => (GetFrozenBrush(Color.FromRgb(156, 163, 175)), "⚙")
            };
            OnPropertyChanged(nameof(CategoryColor));
            OnPropertyChanged(nameof(CategoryGlyph));

            // System Risk Colors
            (RiskColor, RiskBadgeBackground) = Risk switch
            {
                "CRITICAL" => (GetFrozenBrush(Color.FromRgb(239, 68, 68)), GetFrozenBrush(Color.FromArgb(40, 239, 68, 68))),
                "HIGH" => (GetFrozenBrush(Color.FromRgb(249, 115, 22)), GetFrozenBrush(Color.FromArgb(35, 249, 115, 22))),
                "MEDIUM" => (GetFrozenBrush(Color.FromRgb(245, 158, 11)), GetFrozenBrush(Color.FromArgb(30, 245, 158, 11))),
                _ => (GetFrozenBrush(Color.FromRgb(16, 185, 129)), GetFrozenBrush(Color.FromArgb(25, 16, 185, 129)))
            };
            OnPropertyChanged(nameof(RiskColor));
            OnPropertyChanged(nameof(RiskBadgeBackground));

            // Action Safety Visuals
            (ActionSafetyColor, ActionSafetyBadgeBackground, ActionSafetyBadgeText) = ActionSafety switch
            {
                "SAFE TO STOP" => (GetFrozenBrush(Color.FromRgb(16, 185, 129)), GetFrozenBrush(Color.FromArgb(35, 16, 185, 129)), "SAFE TO STOP"),
                "SAFE TO STOP WITH CAUTION" => (GetFrozenBrush(Color.FromRgb(245, 158, 11)), GetFrozenBrush(Color.FromArgb(35, 245, 158, 11)), "SAFE WITH CAUTION"),
                "NOT RECOMMENDED TO STOP" => (GetFrozenBrush(Color.FromRgb(249, 115, 22)), GetFrozenBrush(Color.FromArgb(35, 249, 115, 22)), "NOT RECOMMENDED"),
                "DO NOT STOP" => (GetFrozenBrush(Color.FromRgb(239, 68, 68)), GetFrozenBrush(Color.FromArgb(40, 239, 68, 68)), "DO NOT STOP"),
                _ => (GetFrozenBrush(Color.FromRgb(156, 163, 175)), GetFrozenBrush(Color.FromArgb(25, 156, 163, 175)), "UNKNOWN SAFETY")
            };
            OnPropertyChanged(nameof(ActionSafetyColor));
            OnPropertyChanged(nameof(ActionSafetyBadgeBackground));
            OnPropertyChanged(nameof(ActionSafetyBadgeText));
        }

        private static SolidColorBrush GetFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public class ServiceManagerViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;

        private string _status = "● SERVICES CONFIGURATION READY";
        private Brush _statusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129));
        private bool _isScanning;
        private string _searchQuery = "";
        private string _selectedFilter = "ALL";
        private string _selectedSort = "SAFETY";

        // Primary Summary Counts
        private int _totalCount;
        private int _runningCount;
        private int _stoppedCount;
        private int _pausedCount;
        private int _automaticCount;
        private int _delayedAutoCount;
        private int _manualCount;
        private int _disabledCount;
        private int _microsoftCount;
        private int _thirdPartyCount;
        private int _lowRiskCount;
        private int _mediumRiskCount;
        private int _highRiskCount;
        private int _criticalCount;

        // Action Safety Counts
        private int _safeToStopCount;
        private int _safeWithCautionCount;
        private int _notRecommendedCount;
        private int _doNotStopCount;
        private int _unknownSafetyCount;

        // Safety Modal
        private bool _isDetailModalOpen;
        private ServiceEntryItemViewModel? _selectedDetailItem;
        private bool _isSafetyModalOpen;
        private ServiceEntryItemViewModel? _safetyModalItem;
        private string _safetyModalAction = "Stop";
        private string _safetyModalTitle = "SERVICE STOP CONFIRMATION";
        private string _safetyModalMessage = "";
        private bool _isModalActionBlocked;

        public List<ServiceEntryItemViewModel> AllItems { get; } = new();
        public ObservableCollection<ServiceEntryItemViewModel> VisibleItems { get; } = new();

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public Brush StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }
        public bool IsScanning { get => _isScanning; set { _isScanning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotScanning)); } }
        public bool IsNotScanning => !_isScanning;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    ApplyFilterAndSort();
                }
            }
        }

        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (_selectedFilter != value)
                {
                    _selectedFilter = value;
                    OnPropertyChanged();
                    ApplyFilterAndSort();
                }
            }
        }

        public string SelectedSort
        {
            get => _selectedSort;
            set
            {
                if (_selectedSort != value)
                {
                    _selectedSort = value;
                    OnPropertyChanged();
                    ApplyFilterAndSort();
                }
            }
        }

        // Primary Summary Counts
        public int TotalCount { get => _totalCount; set { _totalCount = value; OnPropertyChanged(); } }
        public int RunningCount { get => _runningCount; set { _runningCount = value; OnPropertyChanged(); } }
        public int StoppedCount { get => _stoppedCount; set { _stoppedCount = value; OnPropertyChanged(); } }
        public int PausedCount { get => _pausedCount; set { _pausedCount = value; OnPropertyChanged(); } }
        public int AutomaticCount { get => _automaticCount; set { _automaticCount = value; OnPropertyChanged(); } }
        public int DelayedAutoCount { get => _delayedAutoCount; set { _delayedAutoCount = value; OnPropertyChanged(); } }
        public int ManualCount { get => _manualCount; set { _manualCount = value; OnPropertyChanged(); } }
        public int DisabledCount { get => _disabledCount; set { _disabledCount = value; OnPropertyChanged(); } }
        public int MicrosoftCount { get => _microsoftCount; set { _microsoftCount = value; OnPropertyChanged(); } }
        public int ThirdPartyCount { get => _thirdPartyCount; set { _thirdPartyCount = value; OnPropertyChanged(); } }
        public int LowRiskCount { get => _lowRiskCount; set { _lowRiskCount = value; OnPropertyChanged(); } }
        public int MediumRiskCount { get => _mediumRiskCount; set { _mediumRiskCount = value; OnPropertyChanged(); } }
        public int HighRiskCount { get => _highRiskCount; set { _highRiskCount = value; OnPropertyChanged(); } }
        public int CriticalCount { get => _criticalCount; set { _criticalCount = value; OnPropertyChanged(); } }

        // Action Safety Counts
        public int SafeToStopCount { get => _safeToStopCount; set { _safeToStopCount = value; OnPropertyChanged(); } }
        public int SafeWithCautionCount { get => _safeWithCautionCount; set { _safeWithCautionCount = value; OnPropertyChanged(); } }
        public int NotRecommendedCount { get => _notRecommendedCount; set { _notRecommendedCount = value; OnPropertyChanged(); } }
        public int DoNotStopCount { get => _doNotStopCount; set { _doNotStopCount = value; OnPropertyChanged(); } }
        public int UnknownSafetyCount { get => _unknownSafetyCount; set { _unknownSafetyCount = value; OnPropertyChanged(); } }

        // Live Filter Counts
        public int FilterCountAll => AllItems.Count;
        public int FilterCountSafeToStop => AllItems.Count(i => i.IsSafeToStop);
        public int FilterCountSafeWithCaution => AllItems.Count(i => i.IsSafeWithCaution);
        public int FilterCountNotRecommended => AllItems.Count(i => i.IsNotRecommended);
        public int FilterCountDoNotStop => AllItems.Count(i => i.IsDoNotStop);
        public int FilterCountUnknownSafety => AllItems.Count(i => i.IsUnknownSafety);

        public int FilterCountRunning => AllItems.Count(i => i.IsRunning);
        public int FilterCountStopped => AllItems.Count(i => i.IsStopped);
        public int FilterCountAutomatic => AllItems.Count(i => i.StartupType.StartsWith("Auto", StringComparison.OrdinalIgnoreCase));
        public int FilterCountManual => AllItems.Count(i => i.StartupType == "Manual");
        public int FilterCountDisabled => AllItems.Count(i => i.StartupType == "Disabled");
        public int FilterCountMicrosoft => AllItems.Count(i => i.IsMicrosoft);
        public int FilterCountThirdParty => AllItems.Count(i => !i.IsMicrosoft);
        public int FilterCountSecurity => AllItems.Count(i => i.Category == "SECURITY");
        public int FilterCountNetwork => AllItems.Count(i => i.Category == "NETWORK");
        public int FilterCountAudio => AllItems.Count(i => i.Category == "AUDIO");
        public int FilterCountGraphics => AllItems.Count(i => i.Category == "GRAPHICS");
        public int FilterCountGaming => AllItems.Count(i => i.Category == "GAMING");
        public int FilterCountSystem => AllItems.Count(i => i.Category == "SYSTEM");
        public int FilterCountDriver => AllItems.Count(i => i.Category == "DRIVER");
        public int FilterCountHighRisk => AllItems.Count(i => i.Risk == "CRITICAL" || i.Risk == "HIGH");
        public int FilterCountLowRisk => AllItems.Count(i => i.Risk == "LOW");

        // Bulk Selection
        public bool HasSelection => AllItems.Any(i => i.IsSelected);
        public int SelectedCount => AllItems.Count(i => i.IsSelected);
        public int SelectedRunningCount => AllItems.Count(i => i.IsSelected && i.IsRunning);
        public int SelectedStoppedCount => AllItems.Count(i => i.IsSelected && i.IsStopped);
        public int SelectedProtectedCount => AllItems.Count(i => i.IsSelected && (i.IsDoNotStop || i.IsCritical));

        // Safety Modal Properties
        public bool IsDetailModalOpen { get => _isDetailModalOpen; set { _isDetailModalOpen = value; OnPropertyChanged(); } }
        public ServiceEntryItemViewModel? SelectedDetailItem { get => _selectedDetailItem; set { _selectedDetailItem = value; OnPropertyChanged(); } }

        public ICommand OpenDetailModalCommand { get; }
        public ICommand CloseDetailModalCommand { get; }

        public bool IsSafetyModalOpen { get => _isSafetyModalOpen; set { _isSafetyModalOpen = value; OnPropertyChanged(); } }
        public ServiceEntryItemViewModel? SafetyModalItem { get => _safetyModalItem; set { _safetyModalItem = value; OnPropertyChanged(); } }
        public string SafetyModalAction { get => _safetyModalAction; set { _safetyModalAction = value; OnPropertyChanged(); } }
        public string SafetyModalTitle { get => _safetyModalTitle; set { _safetyModalTitle = value; OnPropertyChanged(); } }
        public string SafetyModalMessage { get => _safetyModalMessage; set { _safetyModalMessage = value; OnPropertyChanged(); } }
        public bool IsModalActionBlocked { get => _isModalActionBlocked; set { _isModalActionBlocked = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsModalActionAllowed)); } }
        public bool IsModalActionAllowed => !_isModalActionBlocked;

        public bool HasVisibleItems => VisibleItems.Count > 0;

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand SetSortCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand StartSelectedCommand { get; }
        public ICommand StopSelectedCommand { get; }
        public ICommand ConfirmSafetyActionCommand { get; }
        public ICommand CancelSafetyModalCommand { get; }

        public ServiceManagerViewModel(IIpcClient ipc)
        {
            _ipc = ipc;

            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            OpenDetailModalCommand = new RelayCommand(p =>
            {
                if (p is ServiceEntryItemViewModel item)
                {
                    SelectedDetailItem = item;
                    IsDetailModalOpen = true;
                }
            });
            CloseDetailModalCommand = new RelayCommand(_ =>
            {
                IsDetailModalOpen = false;
                SelectedDetailItem = null;
            });
            SetFilterCommand = new RelayCommand(p => SelectedFilter = p?.ToString() ?? "ALL");
            SetSortCommand = new RelayCommand(p => SelectedSort = p?.ToString() ?? "SAFETY");
            ResetFiltersCommand = new RelayCommand(_ => { SearchQuery = ""; SelectedFilter = "ALL"; });

            SelectAllCommand = new RelayCommand(_ =>
            {
                foreach (var item in VisibleItems) item.IsSelected = true;
                UpdateSelectionStats();
            });

            DeselectAllCommand = new RelayCommand(_ =>
            {
                foreach (var item in AllItems) item.IsSelected = false;
                UpdateSelectionStats();
            });

            StartSelectedCommand = new RelayCommand(async _ => await BulkChangeStateAsync("Start"));
            StopSelectedCommand = new RelayCommand(async _ => await BulkChangeStateAsync("Stop"));

            ConfirmSafetyActionCommand = new RelayCommand(async _ =>
            {
                if (SafetyModalItem != null && !IsModalActionBlocked)
                {
                    var target = SafetyModalItem;
                    var action = SafetyModalAction;
                    IsSafetyModalOpen = false;
                    SafetyModalItem = null;
                    await ExecuteServiceActionAsync(target, action, force: true);
                }
            });

            CancelSafetyModalCommand = new RelayCommand(_ =>
            {
                IsSafetyModalOpen = false;
                SafetyModalItem = null;
            });
        }

        public override async Task OnNavigatedToAsync()
        {
            await LoadAsync();
            await base.OnNavigatedToAsync();
        }

        public async Task LoadAsync()
        {
            if (IsScanning) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsScanning = true;
                Status = "● SCANNING WINDOWS SERVICES...";
                StatusColor = GetFrozenBrush(Color.FromRgb(245, 158, 11)); // Amber
            });

            try
            {
                var r = await _ipc.SendRequestAsync(IpcMessageType.GetServices);
                if (r.Success && !string.IsNullOrEmpty(r.Data))
                {
                    var dtos = JsonSerializer.Deserialize<List<ServiceInfoDto>>(r.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dtos != null)
                    {
                        var list = dtos.Select(d => new ServiceEntryItemViewModel(d, this)).ToList();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AllItems.Clear();
                            AllItems.AddRange(list);
                            CalculateHealthStats();
                            ApplyFilterAndSort();
                            Status = $"● {AllItems.Count} SERVICES DISCOVERED";
                            StatusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129));
                        });
                    }
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Status = "⚠ SERVICE SCAN FAILED (ENGINE OFFLINE)";
                        StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = $"⚠ SCAN ERROR: {ex.Message}";
                    StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() => IsScanning = false);
            }
        }

        public async Task RequestStopServiceAsync(ServiceEntryItemViewModel item)
        {
            SafetyModalItem = item;
            SafetyModalAction = "Stop";

            if (item.IsDoNotStop)
            {
                SafetyModalTitle = "STOP BLOCKED — PROTECTED SERVICE";
                SafetyModalMessage = $"'{item.DisplayName}' is classified as DO NOT STOP.\n\nReason: {item.ActionSafetyReason}\n\nStopping this service is blocked by Error Optimizer to protect Windows operating system integrity and prevent system crashes.";
                IsModalActionBlocked = true;
                IsSafetyModalOpen = true;
                return;
            }

            if (item.IsNotRecommended)
            {
                SafetyModalTitle = "STOPPING NOT RECOMMENDED";
                SafetyModalMessage = $"'{item.DisplayName}' is an important Windows service.\n\nReason: {item.ActionSafetyReason}\n\nStopping it may cause dependent functionality or background tasks to fail. Are you sure you wish to proceed?";
                IsModalActionBlocked = false;
                IsSafetyModalOpen = true;
                return;
            }

            if (item.IsSafeWithCaution)
            {
                SafetyModalTitle = "STOP WITH CAUTION";
                SafetyModalMessage = $"'{item.DisplayName}' can be stopped, but may affect application features.\n\nReason: {item.ActionSafetyReason}\n\nDo you want to stop this service now?";
                IsModalActionBlocked = false;
                IsSafetyModalOpen = true;
                return;
            }

            if (item.IsUnknownSafety)
            {
                SafetyModalTitle = "SAFETY COULD NOT BE DETERMINED";
                SafetyModalMessage = $"Insufficient metadata was available to confirm whether '{item.DisplayName}' is safe to stop.\n\nReason: {item.ActionSafetyReason}\n\nPlease verify that no essential applications require this service before stopping.";
                IsModalActionBlocked = false;
                IsSafetyModalOpen = true;
                return;
            }

            // SAFE TO STOP: Simple confirmation
            SafetyModalTitle = "STOP SERVICE CONFIRMATION";
            SafetyModalMessage = $"Are you sure you want to stop '{item.DisplayName}' ({item.ServiceName})?\n\nAction Safety: SAFE TO STOP\nReason: {item.ActionSafetyReason}";
            IsModalActionBlocked = false;
            IsSafetyModalOpen = true;
        }

        public async Task RequestRestartServiceAsync(ServiceEntryItemViewModel item)
        {
            if (item.IsDoNotStop || item.IsCritical)
            {
                SafetyModalItem = item;
                SafetyModalAction = "Restart";
                SafetyModalTitle = "RESTART CONFIRMATION";
                SafetyModalMessage = $"'{item.DisplayName}' is a critical system service. Restarting will briefly recycle its endpoints.\n\nDo you wish to proceed with the restart?";
                IsModalActionBlocked = false;
                IsSafetyModalOpen = true;
                return;
            }

            await ExecuteServiceActionAsync(item, "Restart");
        }

        public async Task ExecuteServiceActionAsync(ServiceEntryItemViewModel item, string action, bool force = false)
        {
            item.IsBusy = true;
            Status = $"● {action.ToUpperInvariant()}ING {item.ServiceName}...";
            StatusColor = GetFrozenBrush(Color.FromRgb(245, 158, 11));

            try
            {
                var payload = JsonSerializer.Serialize(new { item.ServiceName, State = action });
                var r = await _ipc.SendRequestAsync(IpcMessageType.ChangeServiceState, payload);

                // Readback and rescan
                await Task.Delay(300);
                await LoadAsync();

                if (r.Success)
                {
                    Status = $"● {item.DisplayName} {action.ToUpperInvariant()} VERIFIED";
                    StatusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129));
                }
                else
                {
                    Status = $"⚠ FAILED TO {action.ToUpperInvariant()} {item.ServiceName}: {r.ErrorMessage}";
                    StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
                }
            }
            catch (Exception ex)
            {
                Status = $"⚠ ERROR: {ex.Message}";
                StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        public async Task ExecuteSetStartupTypeAsync(ServiceEntryItemViewModel item, string newMode)
        {
            item.IsBusy = true;
            Status = $"● CHANGING STARTUP TYPE FOR {item.ServiceName} TO {newMode.ToUpperInvariant()}...";
            StatusColor = GetFrozenBrush(Color.FromRgb(245, 158, 11));

            try
            {
                var payload = JsonSerializer.Serialize(new { item.ServiceName, State = newMode });
                var r = await _ipc.SendRequestAsync(IpcMessageType.SetServiceStartupType, payload);

                await Task.Delay(250);
                await LoadAsync();

                if (r.Success)
                {
                    Status = $"● {item.DisplayName} STARTUP TYPE ➔ {newMode.ToUpperInvariant()} VERIFIED";
                    StatusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129));
                }
                else
                {
                    Status = $"⚠ FAILED TO CHANGE STARTUP TYPE: {r.ErrorMessage}";
                    StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
                }
            }
            catch (Exception ex)
            {
                Status = $"⚠ ERROR: {ex.Message}";
                StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        private async Task BulkChangeStateAsync(string targetAction)
        {
            var eligible = AllItems.Where(i => i.IsSelected).ToList();
            if (eligible.Count == 0) return;

            // Protect DO NOT STOP services from bulk stop
            if (targetAction.Equals("Stop", StringComparison.OrdinalIgnoreCase))
            {
                var protectedItems = eligible.Where(i => i.IsDoNotStop || i.IsCritical).ToList();
                if (protectedItems.Count > 0)
                {
                    eligible = eligible.Where(i => !i.IsDoNotStop && !i.IsCritical).ToList();
                    Status = $"⚠ {protectedItems.Count} PROTECTED CRITICAL SERVICES WERE SHIELDED FROM BULK STOP";
                }
            }

            foreach (var item in eligible)
            {
                await ExecuteServiceActionAsync(item, targetAction, force: true);
            }

            await LoadAsync();
        }

        public void UpdateSelectionStats()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedRunningCount));
            OnPropertyChanged(nameof(SelectedStoppedCount));
            OnPropertyChanged(nameof(SelectedProtectedCount));
        }

        private void CalculateHealthStats()
        {
            TotalCount = AllItems.Count;
            RunningCount = AllItems.Count(i => i.IsRunning);
            StoppedCount = AllItems.Count(i => i.IsStopped);
            PausedCount = AllItems.Count(i => i.IsPaused);

            AutomaticCount = AllItems.Count(i => i.StartupType == "Automatic");
            DelayedAutoCount = AllItems.Count(i => i.StartupType.Contains("Delayed", StringComparison.OrdinalIgnoreCase));
            ManualCount = AllItems.Count(i => i.StartupType == "Manual");
            DisabledCount = AllItems.Count(i => i.StartupType == "Disabled");

            MicrosoftCount = AllItems.Count(i => i.IsMicrosoft);
            ThirdPartyCount = AllItems.Count(i => !i.IsMicrosoft);

            LowRiskCount = AllItems.Count(i => i.Risk == "LOW");
            MediumRiskCount = AllItems.Count(i => i.Risk == "MEDIUM");
            HighRiskCount = AllItems.Count(i => i.Risk == "HIGH");
            CriticalCount = AllItems.Count(i => i.Risk == "CRITICAL");

            SafeToStopCount = AllItems.Count(i => i.IsSafeToStop);
            SafeWithCautionCount = AllItems.Count(i => i.IsSafeWithCaution);
            NotRecommendedCount = AllItems.Count(i => i.IsNotRecommended);
            DoNotStopCount = AllItems.Count(i => i.IsDoNotStop);
            UnknownSafetyCount = AllItems.Count(i => i.IsUnknownSafety);

            // Notify live filter counts
            OnPropertyChanged(nameof(FilterCountAll));
            OnPropertyChanged(nameof(FilterCountSafeToStop));
            OnPropertyChanged(nameof(FilterCountSafeWithCaution));
            OnPropertyChanged(nameof(FilterCountNotRecommended));
            OnPropertyChanged(nameof(FilterCountDoNotStop));
            OnPropertyChanged(nameof(FilterCountUnknownSafety));

            OnPropertyChanged(nameof(FilterCountRunning));
            OnPropertyChanged(nameof(FilterCountStopped));
            OnPropertyChanged(nameof(FilterCountAutomatic));
            OnPropertyChanged(nameof(FilterCountManual));
            OnPropertyChanged(nameof(FilterCountDisabled));
            OnPropertyChanged(nameof(FilterCountMicrosoft));
            OnPropertyChanged(nameof(FilterCountThirdParty));
            OnPropertyChanged(nameof(FilterCountSecurity));
            OnPropertyChanged(nameof(FilterCountNetwork));
            OnPropertyChanged(nameof(FilterCountAudio));
            OnPropertyChanged(nameof(FilterCountGraphics));
            OnPropertyChanged(nameof(FilterCountGaming));
            OnPropertyChanged(nameof(FilterCountSystem));
            OnPropertyChanged(nameof(FilterCountDriver));
            OnPropertyChanged(nameof(FilterCountHighRisk));
            OnPropertyChanged(nameof(FilterCountLowRisk));
        }

        private void ApplyFilterAndSort()
        {
            IEnumerable<ServiceEntryItemViewModel> query = AllItems;

            // Search filter
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.Trim().ToLowerInvariant();
                query = query.Where(i =>
                    i.DisplayName.ToLowerInvariant().Contains(q) ||
                    i.ServiceName.ToLowerInvariant().Contains(q) ||
                    i.Publisher.ToLowerInvariant().Contains(q) ||
                    i.ExePath.ToLowerInvariant().Contains(q) ||
                    i.Description.ToLowerInvariant().Contains(q) ||
                    i.Account.ToLowerInvariant().Contains(q));
            }

            // Filter chips
            query = SelectedFilter switch
            {
                "SAFE_TO_STOP" => query.Where(i => i.IsSafeToStop),
                "SAFE_WITH_CAUTION" => query.Where(i => i.IsSafeWithCaution),
                "NOT_RECOMMENDED" => query.Where(i => i.IsNotRecommended),
                "DO_NOT_STOP" => query.Where(i => i.IsDoNotStop),
                "UNKNOWN_SAFETY" => query.Where(i => i.IsUnknownSafety),

                "RUNNING" => query.Where(i => i.IsRunning),
                "STOPPED" => query.Where(i => i.IsStopped),
                "PAUSED" => query.Where(i => i.IsPaused),
                "AUTOMATIC" => query.Where(i => i.StartupType.StartsWith("Auto", StringComparison.OrdinalIgnoreCase)),
                "MANUAL" => query.Where(i => i.StartupType == "Manual"),
                "DISABLED" => query.Where(i => i.StartupType == "Disabled"),
                "MICROSOFT" => query.Where(i => i.IsMicrosoft),
                "THIRD-PARTY" => query.Where(i => !i.IsMicrosoft),
                "SECURITY" => query.Where(i => i.Category == "SECURITY"),
                "NETWORK" => query.Where(i => i.Category == "NETWORK"),
                "AUDIO" => query.Where(i => i.Category == "AUDIO"),
                "GRAPHICS" => query.Where(i => i.Category == "GRAPHICS"),
                "GAMING" => query.Where(i => i.Category == "GAMING"),
                "SYSTEM" => query.Where(i => i.Category == "SYSTEM"),
                "DRIVER" => query.Where(i => i.Category == "DRIVER"),
                "HIGH_RISK" => query.Where(i => i.Risk == "CRITICAL" || i.Risk == "HIGH"),
                "LOW_RISK" => query.Where(i => i.Risk == "LOW"),
                _ => query
            };

            // Sorting
            query = SelectedSort switch
            {
                "SAFETY" => query.OrderBy(i => i.ActionSafety switch { "SAFE TO STOP" => 0, "SAFE TO STOP WITH CAUTION" => 1, "NOT RECOMMENDED TO STOP" => 2, "UNKNOWN" => 3, _ => 4 }).ThenBy(i => i.DisplayName),
                "STATUS" => query.OrderBy(i => i.IsRunning ? 0 : 1).ThenBy(i => i.DisplayName),
                "STARTUP" => query.OrderBy(i => i.StartupType).ThenBy(i => i.DisplayName),
                "RISK" => query.OrderBy(i => i.Risk switch { "CRITICAL" => 0, "HIGH" => 1, "MEDIUM" => 2, _ => 3 }).ThenBy(i => i.DisplayName),
                "PUBLISHER" => query.OrderBy(i => i.Publisher).ThenBy(i => i.DisplayName),
                "CATEGORY" => query.OrderBy(i => i.Category).ThenBy(i => i.DisplayName),
                _ => query.OrderBy(i => i.DisplayName)
            };

            var filtered = query.ToList();
            VisibleItems.Clear();
            foreach (var item in filtered)
            {
                VisibleItems.Add(item);
            }

            OnPropertyChanged(nameof(HasVisibleItems));
        }

        private static SolidColorBrush GetFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}

