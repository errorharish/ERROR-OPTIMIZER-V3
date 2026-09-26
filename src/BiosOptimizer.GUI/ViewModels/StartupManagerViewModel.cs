using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.ViewModels
{
    public class StartupEntryItemViewModel : ViewModelBase
    {
        private readonly Action _onStateOrSelectionChanged;

        public StartupEntryItemViewModel(Action onStateOrSelectionChanged)
        {
            _onStateOrSelectionChanged = onStateOrSelectionChanged;
        }

        public string Source { get; set; } = string.Empty;
        public string FriendlySource { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Publisher { get; set; } = "Unknown";
        public string ExePath { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public string Version { get; set; } = "N/A";
        public string Category { get; set; } = "USER APPLICATION";
        public string Impact { get; set; } = "UNKNOWN";
        public string ImpactMethod { get; set; } = "NOT MEASURED";
        public bool IsSystemItem { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool RequiresRestart { get; set; }
        public bool FileExists { get; set; }

        private int _stateInt;
        public int StateInt
        {
            get => _stateInt;
            set
            {
                if (_stateInt != value)
                {
                    _stateInt = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsEnabled));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(StatusBadgeBackground));
                    OnPropertyChanged(nameof(ActionButtonText));
                }
            }
        }

        public bool IsEnabled => StateInt == 0;
        public string StatusText => IsEnabled ? "ENABLED" : "DISABLED";
        public string ActionButtonText => IsEnabled ? "DISABLE" : "ENABLE";

        public Brush StatusColor => IsEnabled 
            ? GetFrozenBrush(Color.FromRgb(16, 185, 129)) // Emerald Green
            : GetFrozenBrush(Color.FromRgb(156, 163, 175)); // Muted Gray

        public Brush StatusBadgeBackground => IsEnabled
            ? GetFrozenBrush(Color.FromArgb(30, 16, 185, 129))
            : GetFrozenBrush(Color.FromArgb(20, 255, 255, 255));

        public Brush CategoryColor => Category switch
        {
            "SECURITY" => GetFrozenBrush(Color.FromRgb(239, 68, 68)),   // Red
            "DRIVER" => GetFrozenBrush(Color.FromRgb(56, 189, 248)),     // Sky Blue
            "SYSTEM" => GetFrozenBrush(Color.FromRgb(245, 158, 11)),     // Amber
            _ => GetFrozenBrush(Color.FromRgb(168, 85, 247))             // Purple / User App
        };

        public Brush ImpactColor => Impact switch
        {
            "HIGH" => GetFrozenBrush(Color.FromRgb(239, 68, 68)),      // Red
            "MEDIUM" => GetFrozenBrush(Color.FromRgb(245, 158, 11)),   // Amber
            "LOW" => GetFrozenBrush(Color.FromRgb(16, 185, 129)),      // Green
            _ => GetFrozenBrush(Color.FromRgb(156, 163, 175))          // Muted
        };

        private ImageSource? _appIcon;
        public ImageSource? AppIcon
        {
            get => _appIcon;
            set { _appIcon = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAppIcon)); }
        }

        public bool HasAppIcon => _appIcon != null;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    _onStateOrSelectionChanged?.Invoke();
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ToggleCommand { get; set; }
        public ICommand ToggleDetailsCommand { get; set; }
    }

    public class StartupManagerViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;

        private string _status = "Ready";
        private Brush _statusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129));
        private bool _isScanning;
        private string _searchQuery = "";
        private string _selectedFilter = "ALL";
        private string _selectedSort = "NAME";

        // Health & Stats
        private int _totalCount;
        private int _enabledCount;
        private int _disabledCount;
        private int _highImpactCount;
        private int _mediumImpactCount;
        private int _lowImpactCount;
        private int _unknownImpactCount;
        private int _systemCount;
        private int _userCount;
        private int _sourcesScannedCount = 8;

        // Safety Modal
        private bool _isDetailModalOpen;
        private StartupEntryItemViewModel? _selectedDetailItem;
        private bool _isSafetyModalOpen;
        private StartupEntryItemViewModel? _safetyModalItem;

        public List<StartupEntryItemViewModel> AllItems { get; } = new();
        public ObservableCollection<StartupEntryItemViewModel> VisibleItems { get; } = new();

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

        // Summary Metric Properties
        public int TotalCount { get => _totalCount; set { _totalCount = value; OnPropertyChanged(); } }
        public int EnabledCount { get => _enabledCount; set { _enabledCount = value; OnPropertyChanged(); } }
        public int DisabledCount { get => _disabledCount; set { _disabledCount = value; OnPropertyChanged(); } }
        public int HighImpactCount { get => _highImpactCount; set { _highImpactCount = value; OnPropertyChanged(); } }
        public int MediumImpactCount { get => _mediumImpactCount; set { _mediumImpactCount = value; OnPropertyChanged(); } }
        public int LowImpactCount { get => _lowImpactCount; set { _lowImpactCount = value; OnPropertyChanged(); } }
        public int UnknownImpactCount { get => _unknownImpactCount; set { _unknownImpactCount = value; OnPropertyChanged(); } }
        public int SystemCount { get => _systemCount; set { _systemCount = value; OnPropertyChanged(); } }
        public int UserCount { get => _userCount; set { _userCount = value; OnPropertyChanged(); } }
        public int SourcesScannedCount { get => _sourcesScannedCount; set { _sourcesScannedCount = value; OnPropertyChanged(); } }

        public string HealthRatingText => EnabledCount == 0 
            ? "OPTIMAL CONFIGURATION" 
            : (HighImpactCount > 2 && EnabledCount > 4 ? "ATTENTION RECOMMENDED" : "HEALTHY STARTUP");

        public Brush HealthRatingBrush => EnabledCount == 0 
            ? GetFrozenBrush(Color.FromRgb(16, 185, 129)) // Emerald Green
            : (HighImpactCount > 2 && EnabledCount > 4 
                ? GetFrozenBrush(Color.FromRgb(245, 158, 11)) // Amber
                : GetFrozenBrush(Color.FromRgb(16, 185, 129)));

        public string ImpactDistributionSummary => $"{HighImpactCount} High • {MediumImpactCount} Medium • {LowImpactCount} Low • {UnknownImpactCount} Unmeasured";

        public string PerformanceAdvice => EnabledCount == 0 
            ? "No non-essential applications are launching with Windows. Startup latency is at hardware baseline." 
            : $"{EnabledCount} applications launch with Windows ({HighImpactCount} High Impact). Disable non-essential startup entries to accelerate boot latency.";

        // Selection & Bulk Toolbar Properties
        public int SelectedCount => AllItems.Count(i => i.IsSelected);
        public int SelectedEnabledCount => AllItems.Count(i => i.IsSelected && i.IsEnabled);
        public int SelectedDisabledCount => AllItems.Count(i => i.IsSelected && !i.IsEnabled);
        public bool HasSelection => SelectedCount > 0;
        public bool HasVisibleItems => VisibleItems.Count > 0;

        // Dynamic Filter Counts
        public int FilterCountAll => AllItems.Count;
        public int FilterCountEnabled => AllItems.Count(i => i.IsEnabled);
        public int FilterCountDisabled => AllItems.Count(i => !i.IsEnabled);
        public int FilterCountHigh => AllItems.Count(i => i.Impact == "HIGH");
        public int FilterCountMedium => AllItems.Count(i => i.Impact == "MEDIUM");
        public int FilterCountLow => AllItems.Count(i => i.Impact == "LOW");
        public int FilterCountSystem => AllItems.Count(i => i.IsSystemItem);
        public int FilterCountUser => AllItems.Count(i => !i.IsSystemItem);
        public int FilterCountUnknown => AllItems.Count(i => i.Impact == "UNKNOWN");

        // Safety Modal Properties
        public bool IsDetailModalOpen { get => _isDetailModalOpen; set { _isDetailModalOpen = value; OnPropertyChanged(); } }
        public StartupEntryItemViewModel? SelectedDetailItem { get => _selectedDetailItem; set { _selectedDetailItem = value; OnPropertyChanged(); } }

        public ICommand OpenDetailModalCommand { get; }
        public ICommand CloseDetailModalCommand { get; }

        public bool IsSafetyModalOpen { get => _isSafetyModalOpen; set { _isSafetyModalOpen = value; OnPropertyChanged(); } }
        public StartupEntryItemViewModel? SafetyModalItem { get => _safetyModalItem; set { _safetyModalItem = value; OnPropertyChanged(); } }

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand SetSortCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand EnableSelectedCommand { get; }
        public ICommand DisableSelectedCommand { get; }
        public ICommand ConfirmSafetyDisableCommand { get; }
        public ICommand CancelSafetyModalCommand { get; }

        public StartupManagerViewModel(IIpcClient ipc)
        {
            _ipc = ipc;

            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            OpenDetailModalCommand = new RelayCommand(p =>
            {
                if (p is StartupEntryItemViewModel item)
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
            SetSortCommand = new RelayCommand(p => SelectedSort = p?.ToString() ?? "NAME");
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

            EnableSelectedCommand = new RelayCommand(async _ => await BulkChangeStateAsync(true));
            DisableSelectedCommand = new RelayCommand(async _ => await BulkChangeStateAsync(false));

            ConfirmSafetyDisableCommand = new RelayCommand(async _ =>
            {
                if (SafetyModalItem != null)
                {
                    var target = SafetyModalItem;
                    IsSafetyModalOpen = false;
                    SafetyModalItem = null;
                    await ExecuteToggleAsync(target, force: true);
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
            IsScanning = true;
            Status = "Scanning Windows startup configurations...";
            StatusColor = GetFrozenBrush(Color.FromRgb(245, 158, 11)); // Amber

            try
            {
                var r = await _ipc.SendRequestAsync(IpcMessageType.GetStartupItems);
                if (r.Success && !string.IsNullOrEmpty(r.Data))
                {
                    var dtos = JsonSerializer.Deserialize<List<JsonElement>>(r.Data);
                    if (dtos != null)
                    {
                        var parsedItems = new List<StartupEntryItemViewModel>();

                        foreach (var el in dtos)
                        {
                            var vm = new StartupEntryItemViewModel(UpdateSelectionStats)
                            {
                                Source = el.TryGetProperty("Source", out var src) ? src.GetString() ?? "" : "",
                                FriendlySource = el.TryGetProperty("FriendlySource", out var fsrc) ? fsrc.GetString() ?? "" : "",
                                Path = el.TryGetProperty("Path", out var p) ? p.GetString() ?? "" : "",
                                ValueName = el.TryGetProperty("ValueName", out var vn) ? vn.GetString() ?? "" : "",
                                DisplayName = el.TryGetProperty("DisplayName", out var dn) ? dn.GetString() ?? "" : "",
                                Publisher = el.TryGetProperty("Publisher", out var pub) ? pub.GetString() ?? "Unknown" : "Unknown",
                                ExePath = el.TryGetProperty("ExePath", out var ep) ? ep.GetString() ?? "" : "",
                                CommandLine = el.TryGetProperty("CommandLine", out var cl) ? cl.GetString() ?? "" : "",
                                Version = el.TryGetProperty("Version", out var ver) ? ver.GetString() ?? "N/A" : "N/A",
                                Category = el.TryGetProperty("Category", out var cat) ? cat.GetString() ?? "USER APPLICATION" : "USER APPLICATION",
                                Impact = el.TryGetProperty("Impact", out var imp) ? imp.GetString() ?? "UNKNOWN" : "UNKNOWN",
                                ImpactMethod = el.TryGetProperty("ImpactMethod", out var im) ? im.GetString() ?? "NOT MEASURED" : "NOT MEASURED",
                                IsSystemItem = el.TryGetProperty("IsSystemItem", out var isi) && isi.GetBoolean(),
                                RequiresAdmin = el.TryGetProperty("RequiresAdmin", out var ra) && ra.GetBoolean(),
                                RequiresRestart = el.TryGetProperty("RequiresRestart", out var rr) && rr.GetBoolean(),
                                FileExists = el.TryGetProperty("FileExists", out var fe) && fe.GetBoolean(),
                                StateInt = el.TryGetProperty("State", out var st) ? st.GetInt32() : 0
                            };

                            if (string.IsNullOrWhiteSpace(vm.DisplayName)) vm.DisplayName = vm.ValueName;
                            if (string.IsNullOrWhiteSpace(vm.FriendlySource)) vm.FriendlySource = vm.Source;

                            // Extract high resolution app icon safely
                            vm.AppIcon = IconCacheHelper.GetOrExtractIcon(vm.ExePath);

                            vm.ToggleCommand = new RelayCommand(async _ => await RequestToggleAsync(vm));
                            vm.ToggleDetailsCommand = new RelayCommand(_ => vm.IsExpanded = !vm.IsExpanded);

                            parsedItems.Add(vm);
                        }

                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            AllItems.Clear();
                            AllItems.AddRange(parsedItems);

                            RecalculateStats();
                            ApplyFilterAndSort();

                            Status = $"STARTUP CONFIGURATION READY ({TotalCount} entries found across {SourcesScannedCount} sources)";
                            StatusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129)); // Green
                        });
                    }
                }
                else
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Status = $"❌ Service unavailable: {r.ErrorMessage}";
                        StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68)); // Red
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Status = $"❌ Startup scan error: {ex.Message}";
                    StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
                });
            }
            finally
            {
                IsScanning = false;
            }
        }

        private async Task RequestToggleAsync(StartupEntryItemViewModel vm)
        {
            // Safety confirmation for critical system items when disabling
            if (vm.IsEnabled && vm.IsSystemItem)
            {
                SafetyModalItem = vm;
                IsSafetyModalOpen = true;
                return;
            }

            await ExecuteToggleAsync(vm, force: false);
        }

        private async Task ExecuteToggleAsync(StartupEntryItemViewModel vm, bool force)
        {
            bool targetState = !vm.IsEnabled;
            string requestedAction = targetState ? "Enabling" : "Disabling";
            Status = $"{requestedAction} '{vm.DisplayName}'...";
            StatusColor = GetFrozenBrush(Color.FromRgb(245, 158, 11));

            var payload = JsonSerializer.Serialize(new { vm.Source, vm.Path, vm.ValueName });
            var msgType = targetState ? IpcMessageType.EnableStartupItem : IpcMessageType.DisableStartupItem;

            var r = await _ipc.SendRequestAsync(msgType, payload);
            if (r.Success)
            {
                // Real Readback Verification: Rescan to confirm Windows state
                await LoadAsync();
                var rechecked = AllItems.FirstOrDefault(i => i.Source == vm.Source && i.Path == vm.Path && i.ValueName == vm.ValueName);
                if (rechecked != null && rechecked.IsEnabled == targetState)
                {
                    Status = $"✓ Verified: '{vm.DisplayName}' is now {rechecked.StatusText}";
                    StatusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129));
                }
            }
            else
            {
                Status = $"❌ Failed to change state for '{vm.DisplayName}': {r.ErrorMessage}";
                StatusColor = GetFrozenBrush(Color.FromRgb(239, 68, 68));
            }
        }

        private async Task BulkChangeStateAsync(bool enable)
        {
            var targets = AllItems.Where(i => i.IsSelected && i.IsEnabled != enable).ToList();
            if (targets.Count == 0) return;

            string actionName = enable ? "Enabling" : "Disabling";
            Status = $"{actionName} {targets.Count} selected startup entries...";
            StatusColor = GetFrozenBrush(Color.FromRgb(245, 158, 11));

            int successCount = 0;
            foreach (var target in targets)
            {
                var payload = JsonSerializer.Serialize(new { target.Source, target.Path, target.ValueName });
                var msgType = enable ? IpcMessageType.EnableStartupItem : IpcMessageType.DisableStartupItem;
                var r = await _ipc.SendRequestAsync(msgType, payload);
                if (r.Success) successCount++;
            }

            await LoadAsync();
            Status = $"✓ Bulk action complete: {successCount} of {targets.Count} items updated.";
            StatusColor = GetFrozenBrush(Color.FromRgb(16, 185, 129));
        }

        private void RecalculateStats()
        {
            TotalCount = AllItems.Count;
            EnabledCount = AllItems.Count(i => i.IsEnabled);
            DisabledCount = AllItems.Count(i => !i.IsEnabled);
            HighImpactCount = AllItems.Count(i => i.Impact == "HIGH");
            MediumImpactCount = AllItems.Count(i => i.Impact == "MEDIUM");
            LowImpactCount = AllItems.Count(i => i.Impact == "LOW");
            UnknownImpactCount = AllItems.Count(i => i.Impact == "UNKNOWN");
            SystemCount = AllItems.Count(i => i.IsSystemItem);
            UserCount = AllItems.Count(i => !i.IsSystemItem);

            OnPropertyChanged(nameof(HealthRatingText));
            OnPropertyChanged(nameof(HealthRatingBrush));
            OnPropertyChanged(nameof(ImpactDistributionSummary));
            OnPropertyChanged(nameof(PerformanceAdvice));
            OnPropertyChanged(nameof(FilterCountAll));
            OnPropertyChanged(nameof(FilterCountEnabled));
            OnPropertyChanged(nameof(FilterCountDisabled));
            OnPropertyChanged(nameof(FilterCountHigh));
            OnPropertyChanged(nameof(FilterCountMedium));
            OnPropertyChanged(nameof(FilterCountLow));
            OnPropertyChanged(nameof(FilterCountSystem));
            OnPropertyChanged(nameof(FilterCountUser));
            OnPropertyChanged(nameof(FilterCountUnknown));
            UpdateSelectionStats();
        }

        private void UpdateSelectionStats()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedEnabledCount));
            OnPropertyChanged(nameof(SelectedDisabledCount));
            OnPropertyChanged(nameof(HasSelection));
        }

        private void ApplyFilterAndSort()
        {
            var query = AllItems.AsEnumerable();

            // 1. Category / Status Filter
            query = SelectedFilter switch
            {
                "ENABLED" => query.Where(i => i.IsEnabled),
                "DISABLED" => query.Where(i => !i.IsEnabled),
                "HIGH" => query.Where(i => i.Impact == "HIGH"),
                "MEDIUM" => query.Where(i => i.Impact == "MEDIUM"),
                "LOW" => query.Where(i => i.Impact == "LOW"),
                "SYSTEM" => query.Where(i => i.IsSystemItem),
                "USER" => query.Where(i => !i.IsSystemItem),
                "UNKNOWN" => query.Where(i => i.Impact == "UNKNOWN"),
                _ => query
            };

            // 2. Search Query Matching
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                string s = SearchQuery.Trim();
                query = query.Where(i => (i.DisplayName != null && i.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                         (i.ValueName != null && i.ValueName.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                         (i.Publisher != null && i.Publisher.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                         (i.ExePath != null && i.ExePath.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                         (i.FriendlySource != null && i.FriendlySource.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                         (i.CommandLine != null && i.CommandLine.Contains(s, StringComparison.OrdinalIgnoreCase)));
            }

            // 3. Sorting
            query = SelectedSort switch
            {
                "STATUS" => query.OrderBy(i => i.StateInt).ThenBy(i => i.DisplayName),
                "IMPACT" => query.OrderBy(i => i.Impact switch { "HIGH" => 0, "MEDIUM" => 1, "LOW" => 2, _ => 3 }).ThenBy(i => i.DisplayName),
                "PUBLISHER" => query.OrderBy(i => i.Publisher).ThenBy(i => i.DisplayName),
                "SOURCE" => query.OrderBy(i => i.FriendlySource).ThenBy(i => i.DisplayName),
                _ => query.OrderBy(i => i.DisplayName)
            };

            VisibleItems.Clear();
            foreach (var item in query)
            {
                VisibleItems.Add(item);
            }

            OnPropertyChanged(nameof(HasVisibleItems));
        }
    }

    public static class IconCacheHelper
    {
        private static readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(
            string lpszFile,
            int nIconIndex,
            int cxIcon,
            int cyIcon,
            IntPtr[] phicon,
            int[] piconid,
            uint nIcons,
            uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;

        public static ImageSource? GetOrExtractIcon(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;

            lock (_cache)
            {
                if (_cache.TryGetValue(exePath, out var cached)) return cached;
            }

            try
            {
                // 1. Try High-Resolution PrivateExtractIcons at 48x48 (High-DPI sharp rendering)
                var phicon = new IntPtr[1];
                var piconid = new int[1];
                uint extracted = PrivateExtractIcons(exePath, 0, 48, 48, phicon, piconid, 1, 0);

                if (extracted > 0 && phicon[0] != IntPtr.Zero)
                {
                    var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        phicon[0],
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();

                    DestroyIcon(phicon[0]);

                    lock (_cache)
                    {
                        _cache[exePath] = bitmap;
                    }
                    return bitmap;
                }

                // 2. Fallback to Shell32 Large Icon
                var shinfo = new SHFILEINFO();
                SHGetFileInfo(exePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

                if (shinfo.hIcon != IntPtr.Zero)
                {
                    var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();

                    DestroyIcon(shinfo.hIcon);

                    lock (_cache)
                    {
                        _cache[exePath] = bitmap;
                    }
                    return bitmap;
                }
            }
            catch { }

            lock (_cache)
            {
                _cache[exePath] = null;
            }
            return null;
        }
    }
}