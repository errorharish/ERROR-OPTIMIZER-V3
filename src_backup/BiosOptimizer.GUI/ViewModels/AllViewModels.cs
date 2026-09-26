using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.ViewModels
{
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // DEBLOAT
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class DebloatItemViewModel : ViewModelBase
    {
        private bool _isSelected;
        private readonly Action _onSelectionChanged;

        public DebloatItemViewModel(Action onSelectionChanged = null)
        {
            _onSelectionChanged = onSelectionChanged;
            ToggleSelectionCommand = new RelayCommand(_ => 
            {
                if (Status == "SAFE TO REMOVE" || Status == "OPTIONAL")
                {
                    IsSelected = !IsSelected;
                }
            });
        }

        public ICommand ToggleSelectionCommand { get; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = "Detected";
        public bool IsSelected
        {
            get => _isSelected;
            set 
            { 
                if (_isSelected != value)
                {
                    _isSelected = value; 
                    OnPropertyChanged(); 
                    _onSelectionChanged?.Invoke();
                }
            }
        }
    }

    public class DebloatViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Ready to scan for bloatware.";
        private string _result = "";
        private bool _scanning;
        private ObservableCollection<DebloatItemViewModel> _debloatItems = new();
        private ObservableCollection<DebloatItemViewModel> _visibleDebloatItems = new();
        private bool _isExecuting;
        private double _optimizationProgress;
        private string _statusMessage = "";

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Result { get => _result; set { _result = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); } }
        public bool IsScanning { get => _scanning; set { _scanning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotScanning)); } }
        public bool IsNotScanning => !_scanning;
        public Visibility HasResult => string.IsNullOrEmpty(_result) ? Visibility.Collapsed : Visibility.Visible;
        
        public bool IsExecuting { get => _isExecuting; set { _isExecuting = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotExecuting)); } }
        public bool IsNotExecuting => !_isExecuting;
        public double OptimizationProgress { get => _optimizationProgress; set { _optimizationProgress = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }
        private string _currentFilter = "ALL";
        public string CurrentFilter 
        {
            get => _currentFilter;
            set
            {
                _currentFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
        
        public string EmptyStateMessage
        {
            get
            {
                if (DebloatItems.Count == 0) return "NO DEBLOAT TARGETS FOUND";
                return $"NO {_currentFilter} TARGETS FOUND";
            }
        }

        public Visibility HasDebloatItemsVisibility => DebloatItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasNoTargets => DebloatItems.Count == 0 && !IsScanning ? Visibility.Visible : Visibility.Collapsed;

        public ObservableCollection<DebloatItemViewModel> DebloatItems 
        { 
            get => _debloatItems; 
            set 
            { 
                _debloatItems = value; 
                OnPropertyChanged(); 
                UpdateSelectionState(); 
            } 
        }

        public ObservableCollection<DebloatItemViewModel> VisibleDebloatItems 
        { 
            get => _visibleDebloatItems; 
            set 
            { 
                _visibleDebloatItems = value; 
                OnPropertyChanged(); 
            } 
        }

        private void ApplyFilter()
        {
            // Do not run apply filter if not populated yet
            if (DebloatItems == null) return;
            
            App.Current.Dispatcher.Invoke(() =>
            {
                VisibleDebloatItems.Clear();

                foreach (var item in DebloatItems)
                {
                    bool matches = false;
                    if (string.IsNullOrEmpty(_currentFilter) || _currentFilter == "ALL") 
                    {
                        matches = true;
                    }
                    else if (_currentFilter == "SAFE" && item.Status == "SAFE TO REMOVE") matches = true;
                    else if (_currentFilter == "MEDIUM" && (item.Status == "OPTIONAL" || item.Status == "MEDIUM")) matches = true;
                    else if (_currentFilter == "HIGH RISK" && item.Status == "HIGH RISK") matches = true;
                    else if (_currentFilter == "PROTECTED" && (item.Status == "SYSTEM PACKAGE" || item.Status == "FRAMEWORK" || item.Status == "RESOURCE" || item.Status == "PROTECTED")) matches = true;

                    if (matches)
                    {
                        VisibleDebloatItems.Add(item);
                    }
                }

                OnPropertyChanged(nameof(HasNoTargets));
                OnPropertyChanged(nameof(EmptyStateMessage));
                OnPropertyChanged(nameof(HasDebloatItemsVisibility));
            });
        }

        public int SelectedCount => DebloatItems.Count(i => i.IsSelected);
        public string SelectedCountText => $"{SelectedCount} Selected";
        public bool CanApply => SelectedCount > 0;
        
        public int TotalDetected => DebloatItems.Count;
        public int SafeCount => DebloatItems.Count(i => i.Status == "SAFE TO REMOVE");
        public int MediumCount => DebloatItems.Count(i => i.Status == "OPTIONAL" || i.Status == "MEDIUM");
        public int HighRiskCount => DebloatItems.Count(i => i.Status == "HIGH RISK");
        public int ProtectedCount => DebloatItems.Count(i => i.Status == "SYSTEM PACKAGE" || i.Status == "FRAMEWORK" || i.Status == "RESOURCE" || i.Status == "PROTECTED");

        private void UpdateSelectionState()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedCountText));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(TotalDetected));
            OnPropertyChanged(nameof(SafeCount));
            OnPropertyChanged(nameof(MediumCount));
            OnPropertyChanged(nameof(HighRiskCount));
            OnPropertyChanged(nameof(ProtectedCount));
            // Removed ApplyFilter() from here because it slows down the loop and we want it to run just once at the end.
            CommandManager.InvalidateRequerySuggested();
        }

        public ICommand ScanCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand SafeOptimizeCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand SetFilterCommand { get; }

        public DebloatViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            ScanCommand = new RelayCommand(async _ => await ScanAsync());
            ApplyCommand = new RelayCommand(async _ => await ApplyAsync());
            SafeOptimizeCommand = new RelayCommand(async _ => await SafeOptimizeAsync());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            ClearAllCommand = new RelayCommand(_ => ClearAll());
            SetFilterCommand = new RelayCommand(p => CurrentFilter = p as string ?? "ALL");
            
            // Start scan natively on the UI dispatcher, relying on async/await for background network I/O
            _ = ScanAsync();
        }

        private void SelectAll()
        {
            foreach (var item in DebloatItems)
            {
                if (item.Status == "SAFE TO REMOVE" || item.Status == "OPTIONAL" || item.Status == "MEDIUM")
                {
                    item.IsSelected = true;
                }
            }
            UpdateSelectionState();
        }

        private async Task SafeOptimizeAsync()
        {
            // Select ONLY SAFE items
            foreach (var item in DebloatItems)
            {
                item.IsSelected = item.Status == "SAFE TO REMOVE";
            }
            UpdateSelectionState();
            
            // Auto apply
            if (CanApply)
            {
                await ApplyAsync();
            }
        }
        
        private void ClearAll()
        {
            foreach (var item in DebloatItems)
            {
                item.IsSelected = false;
            }
            UpdateSelectionState();
        }

        private async Task ScanAsync()
        {
            IsScanning = true;
            Status = "Scanning for bloatware...";
            
            await App.Current.Dispatcher.InvokeAsync(() => 
            {
                DebloatItems.Clear();
                VisibleDebloatItems.Clear();
                Result = "";
            });
            
            // Changed from PreviewTier to GetDebloatItems to get REAL Windows targets
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetDebloatItems, "");
            
            if (r.Success && !string.IsNullOrEmpty(r.Data))
            {
                try
                {
                    var preview = JsonSerializer.Deserialize<BiosOptimizer.IPC.Contracts.TierPreviewDto>(r.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (preview != null && preview.Actions != null && preview.Actions.Count > 0)
                    {
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var dto in preview.Actions)
                            {
                                var rawName = string.IsNullOrWhiteSpace(dto.DisplayName) ? (string.IsNullOrWhiteSpace(dto.ItemId) ? "Unknown Package" : dto.ItemId) : dto.DisplayName;
                                
                                // Friendly name resolution
                                if (rawName.StartsWith("CN=") || rawName.Length > 60 || rawName.Contains("__"))
                                {
                                    rawName = string.IsNullOrWhiteSpace(dto.ItemId) ? "Windows Component" : dto.ItemId;
                                    var dotIndex = rawName.IndexOf('.');
                                    if (dotIndex > 0 && dotIndex < rawName.Length - 1)
                                    {
                                        var nextDotIndex = rawName.IndexOf('.', dotIndex + 1);
                                        if (nextDotIndex > dotIndex)
                                            rawName = rawName.Substring(0, nextDotIndex);
                                    }
                                }

                                var item = new DebloatItemViewModel(UpdateSelectionState)
                                {
                                    Id = dto.ItemId,
                                    Name = rawName,
                                    Publisher = dto.Category,
                                    Category = dto.Category,
                                    IsSelected = false,
                                    Status = dto.Status ?? "Available"
                                };
                                DebloatItems.Add(item);
                            }
                            Status = $"Scan complete — {DebloatItems.Count} items found.";
                            
                            // Initialize UI collection properly
                            CurrentFilter = "ALL";
                            ApplyFilter();
                            UpdateSelectionState();
                        });
                    }
                    else
                    {
                        await App.Current.Dispatcher.InvokeAsync(() => { Status = "DEBLOAT PLAN UNAVAILABLE"; });
                    }
                }
                catch (Exception ex)
                {
                    await App.Current.Dispatcher.InvokeAsync(() => { Status = "DEBLOAT PLAN UNAVAILABLE"; Result = ex.Message; });
                }
            }
            else 
            {
                await App.Current.Dispatcher.InvokeAsync(() => { Status = "SCAN FAILED"; Result = r.ErrorMessage; });
            }
            
            await App.Current.Dispatcher.InvokeAsync(() => { IsScanning = false; OnPropertyChanged(nameof(HasNoTargets)); });
        }

        private async Task ApplyAsync()
        {
            if (DebloatItems.Count == 0) return;
            
            // SECURITY REQUIREMENT: Only push items that are actively selected AND classified as SAFE/OPTIONAL/MEDIUM.
            // Never trust the frontend's IsSelected blindly; perform a double validation bounds check.
            var selectedItems = DebloatItems.Where(i => i.IsSelected && (i.Status == "SAFE TO REMOVE" || i.Status == "OPTIONAL" || i.Status == "MEDIUM")).ToList();
            if (selectedItems.Count == 0)
            {
                Status = "No valid items selected for removal.";
                return;
            }

            var selectedIds = selectedItems.Select(i => i.Id).ToList();

            IsExecuting = true;
            OptimizationProgress = 0.2;
            StatusMessage = $"Applying debloat ({selectedIds.Count} items)...";
            Status = $"Applying debloat ({selectedIds.Count} items)...";
            
            var request = new DebloatRequestDto { SelectedIds = selectedIds };
            var payload = JsonSerializer.Serialize(request);
            
            try
            {
                await foreach (var r in _ipc.SendStreamingRequestAsync(IpcMessageType.ApplyDebloatItems, payload))
                {
                    if (r.Success && r.Data != null)
                    {
                        if (r.Data.Contains("ProgressUpdate"))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(r.Data);
                                int current = doc.RootElement.TryGetProperty("Current", out var cProp) ? cProp.GetInt32() : 0;
                                int total = doc.RootElement.TryGetProperty("Total", out var tProp) ? tProp.GetInt32() : 0;
                                
                                if (total > 0)
                                {
                                    OptimizationProgress = (current * 100.0) / total;
                                    StatusMessage = $"Applying debloat ({current}/{total})...";
                                    Status = StatusMessage;
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(r.Data);
                                if (doc.RootElement.TryGetProperty("FinalResult", out var finalResultProp) && finalResultProp.GetBoolean())
                                {
                                    OptimizationProgress = 100;
                                    int success = doc.RootElement.TryGetProperty("Success", out var sProp) ? sProp.GetInt32() : 0;
                                    int failed = doc.RootElement.TryGetProperty("Failed", out var fProp) ? fProp.GetInt32() : 0;
                                    StatusMessage = $"Optimization complete. ({success} removed, {failed} failed/skipped)";
                                    Status = StatusMessage;
                                    Result = $"Debloat complete. Applied: {success}, Failed/Skipped: {failed}";
                                }
                            }
                            catch { }
                        }
                    }
                }
                
                await Task.Delay(500);
                IsExecuting = false;
                await ScanAsync();
            }
            catch (Exception ex)
            {
                IsExecuting = false;
                Status = "Error: " + ex.Message;
                Result = ex.Message;
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // BIOS SAFE
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class BiosSafeViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _capabilities = "Loading BIOS provider information...";
        private string _status = "Detecting hardware...";
        private string _result = "";

        public string Capabilities { get => _capabilities; set { _capabilities = value; OnPropertyChanged(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Result { get => _result; set { _result = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); } }
        public Visibility HasResult => string.IsNullOrEmpty(_result) ? Visibility.Collapsed : Visibility.Visible;

        public ICommand LoadCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand RestoreCommand { get; }

        public BiosSafeViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            LoadCommand = new RelayCommand(async _ => await LoadAsync());
            ApplyCommand = new RelayCommand(async _ => await ApplyAsync());
            RestoreCommand = new RelayCommand(async _ => await RestoreAsync());
            _ = Task.Run(() => LoadAsync());
        }

        private async Task LoadAsync()
        {
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetBiosCapabilities);
            Capabilities = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? "BIOS provider detected" : "BIOS provider unavailable";
        }

        private async Task ApplyAsync()
        {
            Status = "Applying BIOS safe settings...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.ApplyBiosSetting);
            Result = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? "✓ Applied" : "⚠ " + r.ErrorMessage;
        }

        private async Task RestoreAsync()
        {
            Status = "Restoring BIOS settings...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.RestoreBackup);
            Status = r.Success ? "✓ Restored" : "⚠ " + r.ErrorMessage;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MAX PERFORMANCE
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class MaxPerformanceViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Maximum Performance mode";
        private string _result = "";
        private string _preview = "";


        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Result { get => _result; set { _result = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); } }
        public string Preview { get => _preview; set { _preview = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPreview)); } }
        public Visibility HasResult => string.IsNullOrEmpty(_result) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility HasPreview => string.IsNullOrEmpty(_preview) ? Visibility.Collapsed : Visibility.Visible;

        public ICommand PreviewCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand RestoreCommand { get; }

        public MaxPerformanceViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            PreviewCommand = new RelayCommand(async _ => await PreviewAsync());
            ApplyCommand = new RelayCommand(async _ => await ApplyAsync());
            RestoreCommand = new RelayCommand(async _ => await RestoreAsync());
        }

        private async Task PreviewAsync()
        {
            Status = "Loading preview...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.PreviewTier, "MaximumPerformance");
            Preview = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? "Preview loaded" : "⚠ " + r.ErrorMessage;
        }

        private async Task ApplyAsync()
        {
            Status = "Applying maximum performance settings...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.ApplyTier, "MaximumPerformance");
            Result = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? "✓ Maximum Performance applied" : "⚠ " + r.ErrorMessage;
        }

        private async Task RestoreAsync()
        {
            Status = "Restoring...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.RestoreTier, "MaximumPerformance");
            Status = r.Success ? "✓ Restored" : "⚠ " + r.ErrorMessage;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SYSTEM INFO
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class SystemInfoViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _info = "Loading system information...";
        private string _status = "Connecting...";

        public string Info { get => _info; set { _info = value; OnPropertyChanged(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        
        // Structured properties
        public string CpuName { get; private set; } = "Unknown";
        public string CpuCores { get; private set; } = "Unknown";
        public string Motherboard { get; private set; } = "Unknown";
        public string Memory { get; private set; } = "Unknown";
        public string GpuName { get; private set; } = "Unknown";
        public string BiosInfo { get; private set; } = "Unknown";
        public string WindowsInfo { get; private set; } = "Unknown";
        public string HagsStatus { get; private set; } = "Unknown";
        public string SecureBootStatus { get; private set; } = "Unknown";
        public string TpmStatus { get; private set; } = "Unknown";

        public ICommand RefreshCommand { get; }

        public SystemInfoViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            _ = Task.Run(() => LoadAsync());
        }

        private async Task LoadAsync()
        {
            Status = "Loading...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetMachineProfile);
            if (r.Success) 
            { 
                ParseInfo(r.Data);
                Status = "System info loaded"; 
            }
            else 
            { 
                Info = r.ErrorMessage; 
                Status = "Service offline"; 
            }
        }
        
        private void ParseInfo(string data)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(data);
                var root = doc.RootElement;
                CpuName = root.TryGetProperty("CpuModel", out var cp) ? cp.GetString() : "Unknown";
                CpuCores = root.TryGetProperty("CpuCores", out var cc) ? cc.GetInt32().ToString() : "0";
                Motherboard = root.TryGetProperty("BaseboardModel", out var mb) ? mb.GetString() : "Unknown";
                
                long ram = root.TryGetProperty("TotalRamBytes", out var ra) ? ra.GetInt64() : 0;
                Memory = $"{ram / (1024*1024*1024)} GB";

                if (root.TryGetProperty("Gpus", out var gpus) && gpus.GetArrayLength() > 0)
                {
                    GpuName = gpus[0].TryGetProperty("Name", out var n) ? n.GetString() : "Unknown";
                }
                else
                {
                    GpuName = "Unknown";
                }

                BiosInfo = root.TryGetProperty("BiosVersion", out var bv) ? bv.GetString() : "Unknown";
                WindowsInfo = root.TryGetProperty("WindowsVersion", out var wv) ? wv.GetString() : "Unknown";
                
                // Formulate raw info
                var mfg = root.TryGetProperty("Manufacturer", out var mf) ? mf.GetString() : "Unknown";
                var model = root.TryGetProperty("Model", out var mo) ? mo.GetString() : "Unknown";
                
                Info = $"SYSTEM PROFILE\n" +
                       $"Manufacturer: {mfg}\n" +
                       $"Model: {model}\n" +
                       $"OS: Windows {WindowsInfo}\n" +
                       $"CPU: {CpuName}\n" +
                       $"RAM: {Memory}\n" +
                       $"GPU: {GpuName}\n" +
                       $"BIOS: {BiosInfo}";
            }
            catch { Info = data; }

            OnPropertyChanged(nameof(CpuName));
            OnPropertyChanged(nameof(CpuCores));
            OnPropertyChanged(nameof(Motherboard));
            OnPropertyChanged(nameof(Memory));
            OnPropertyChanged(nameof(GpuName));
            OnPropertyChanged(nameof(BiosInfo));
            OnPropertyChanged(nameof(WindowsInfo));
            OnPropertyChanged(nameof(HagsStatus));
            OnPropertyChanged(nameof(SecureBootStatus));
            OnPropertyChanged(nameof(TpmStatus));
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // STARTUP MANAGER
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class StartupEntryViewModel : ViewModelBase
    {
        public string Source { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        
        private int _stateInt;
        public int StateInt 
        { 
            get => _stateInt; 
            set 
            { 
                _stateInt = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusText));
            } 
        }
        
        public bool IsEnabled => StateInt == 0;
        public string StatusText => IsEnabled ? "Enabled" : "Disabled";
        public Brush StatusColor => IsEnabled ? GetFrozenBrush(Color.FromRgb(76, 175, 80)) : GetFrozenBrush(Color.FromRgb(158, 158, 158));
        
        public ICommand ToggleCommand { get; set; }
    }

    public class StartupManagerViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Loading...";
        private Brush _statusColor = GetFrozenBrush(Color.FromRgb(158, 158, 158)); // Neutral
        private ObservableCollection<StartupEntryViewModel> _itemsList = new();

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); UpdateStatusColor(); } }
        public Brush StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }
        public ObservableCollection<StartupEntryViewModel> ItemsList { get => _itemsList; set { _itemsList = value; OnPropertyChanged(); } }
        
        private void UpdateStatusColor()
        {
            if (Status.Contains("❌") || Status.Contains("Failed") || Status.Contains("offline") || Status.Contains("error"))
                StatusColor = GetFrozenBrush(Color.FromRgb(244, 67, 54)); // Red
            else if (Status.Contains("Loading") || Status.Contains("..."))
                StatusColor = GetFrozenBrush(Color.FromRgb(255, 152, 0)); // Amber
            else
                StatusColor = GetFrozenBrush(Color.FromRgb(76, 175, 80)); // Green
        }
        
        public ICommand RefreshCommand { get; }

        public StartupManagerViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            _ = Task.Run(() => LoadAsync());
        }

        private async Task LoadAsync()
        {
            Application.Current.Dispatcher.Invoke(() => Status = "Loading startup items...");
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetStartupItems);
            if (r.Success && !string.IsNullOrEmpty(r.Data)) 
            { 
                try
                {
                    // The payload is List<StartupEntryState> where State is enum StartupState (0 = Enabled, 1 = Disabled)
                    var dtos = JsonSerializer.Deserialize<List<JsonElement>>(r.Data);
                    if (dtos != null)
                    {
                        var newItems = new List<StartupEntryViewModel>();
                        foreach (var el in dtos)
                        {
                            var vm = new StartupEntryViewModel
                            {
                                Source = el.GetProperty("Source").GetString() ?? "",
                                Path = el.GetProperty("Path").GetString() ?? "",
                                ValueName = el.GetProperty("ValueName").GetString() ?? "",
                                StateInt = el.GetProperty("State").GetInt32()
                            };
                            
                            vm.ToggleCommand = new RelayCommand(async _ => await ToggleEntry(vm));
                            newItems.Add(vm);
                        }
                        
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ItemsList.Clear();
                            foreach (var item in newItems) ItemsList.Add(item);
                            Status = $"Startup items loaded ({ItemsList.Count})";
                        });
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => Status = "❌ Failed to parse startup items: " + ex.Message);
                }
            }
            else 
            { 
                Application.Current.Dispatcher.Invoke(() => Status = "❌ Service offline or error: " + r.ErrorMessage);
            }
        }
        
        private async Task ToggleEntry(StartupEntryViewModel vm)
        {
            var targetState = !vm.IsEnabled;
            var payload = JsonSerializer.Serialize(new { vm.Source, vm.Path, vm.ValueName });
            var msgType = targetState ? IpcMessageType.EnableStartupItem : IpcMessageType.DisableStartupItem;
            
            var r = await _ipc.SendRequestAsync(msgType, payload);
            if (r.Success)
            {
                await LoadAsync();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => Status = $"❌ Failed to toggle {vm.ValueName}: {r.ErrorMessage}");
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SERVICE MANAGER
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class ServiceInfoViewModel : ViewModelBase
    {
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
        
        private string _startupType = string.Empty;
        public string StartupType 
        { 
            get => _startupType; 
            set 
            { 
                _startupType = value; 
                OnPropertyChanged(); 
            } 
        }
        
        private string _runningState = string.Empty;
        public string RunningState 
        { 
            get => _runningState; 
            set 
            { 
                _runningState = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusColor));
            } 
        }

        public string Risk { get; set; } = string.Empty;
        
        public bool IsRunning => RunningState == "Running";
        public string StatusText => IsRunning ? "Stop" : "Start";
        
        public ICommand ToggleCommand { get; set; }
        public ICommand ToggleStartupTypeCommand { get; set; }
        
        public Brush StatusColor => RunningState switch
        {
            "Running" => GetFrozenBrush(Color.FromRgb(76, 175, 80)),
            "Stopped" => GetFrozenBrush(Color.FromRgb(244, 67, 54)),
            _ => GetFrozenBrush(Color.FromRgb(158, 158, 158))
        };
    }

    public class ServiceManagerViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Loading...";
        private Brush _statusColor = GetFrozenBrush(Color.FromRgb(158, 158, 158)); // Neutral
        private ObservableCollection<ServiceInfoViewModel> _servicesList = new();
        
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); UpdateStatusColor(); } }
        public Brush StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }
        public ObservableCollection<ServiceInfoViewModel> ServicesList { get => _servicesList; set { _servicesList = value; OnPropertyChanged(); } }
        
        private void UpdateStatusColor()
        {
            if (Status.Contains("❌") || Status.Contains("Failed") || Status.Contains("offline") || Status.Contains("error"))
                StatusColor = GetFrozenBrush(Color.FromRgb(244, 67, 54)); // Red
            else if (Status.Contains("Loading") || Status.Contains("..."))
                StatusColor = GetFrozenBrush(Color.FromRgb(255, 152, 0)); // Amber
            else
                StatusColor = GetFrozenBrush(Color.FromRgb(76, 175, 80)); // Green
        }
        
        public ICommand RefreshCommand { get; }

        public ServiceManagerViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            _ = Task.Run(() => LoadAsync());
        }

        private async Task LoadAsync()
        {
            Application.Current.Dispatcher.Invoke(() => Status = "Loading services...");
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetServices);
            if (r.Success && !string.IsNullOrEmpty(r.Data)) 
            { 
                try
                {
                    var dtos = JsonSerializer.Deserialize<List<ServiceInfoDto>>(r.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dtos != null)
                    {
                        var newItems = new List<ServiceInfoViewModel>();
                        foreach (var dto in dtos)
                        {
                            var vm = new ServiceInfoViewModel
                            {
                                ServiceName = dto.ServiceName,
                                DisplayName = string.IsNullOrEmpty(dto.DisplayName) ? dto.ServiceName : dto.DisplayName,
                                Description = dto.Description,
                                ExePath = dto.ExePath,
                                StartupType = dto.StartupType,
                                RunningState = dto.RunningState,
                                Risk = dto.Risk
                            };
                            
                            vm.ToggleCommand = new RelayCommand(async _ => await ToggleServiceState(vm));
                            vm.ToggleStartupTypeCommand = new RelayCommand(async _ => await ToggleStartupType(vm));
                            
                            newItems.Add(vm);
                        }
                        
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ServicesList.Clear();
                            foreach (var item in newItems) ServicesList.Add(item);
                            Status = $"Services loaded ({ServicesList.Count})";
                        });
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => Status = "❌ Failed to parse services: " + ex.Message);
                }
            }
            else 
            { 
                Application.Current.Dispatcher.Invoke(() => Status = "❌ Service offline or error: " + r.ErrorMessage);
            }
        }

        private async Task ToggleServiceState(ServiceInfoViewModel vm)
        {
            string targetState = vm.IsRunning ? "Stop" : "Start";
            var payload = JsonSerializer.Serialize(new { vm.ServiceName, State = targetState });
            
            var r = await _ipc.SendRequestAsync(IpcMessageType.ChangeServiceState, payload);
            if (r.Success)
            {
                await LoadAsync();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => Status = $"❌ Failed to {targetState.ToLower()} {vm.ServiceName}: {r.ErrorMessage}");
            }
        }

        private async Task ToggleStartupType(ServiceInfoViewModel vm)
        {
            string targetState = vm.StartupType == "Automatic" ? "Manual" 
                               : vm.StartupType == "Manual" ? "Disabled" 
                               : "Automatic";
            
            var payload = JsonSerializer.Serialize(new { vm.ServiceName, State = targetState });
            
            var r = await _ipc.SendRequestAsync(IpcMessageType.SetServiceStartupType, payload);
            if (r.Success)
            {
                await LoadAsync();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => Status = $"❌ Failed to change startup type for {vm.ServiceName}: {r.ErrorMessage}");
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // NETWORK
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class NetworkViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Ready";
        private string _adapterName = "Ethernet";
        private string _connectionType = "Wired";
        private string _latency = "Measuring...";
        private string _downloadSpeed = "0 KB/s";
        private string _uploadSpeed = "0 KB/s";
        private bool _isOptimizing;
        
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string AdapterName { get => _adapterName; set { _adapterName = value; OnPropertyChanged(); } }
        public string ConnectionType { get => _connectionType; set { _connectionType = value; OnPropertyChanged(); } }
        public string Latency { get => _latency; set { _latency = value; OnPropertyChanged(); } }
        public string DownloadSpeed { get => _downloadSpeed; set { _downloadSpeed = value; OnPropertyChanged(); } }
        public string UploadSpeed { get => _uploadSpeed; set { _uploadSpeed = value; OnPropertyChanged(); } }
        public bool IsOptimizing { get => _isOptimizing; set { _isOptimizing = value; OnPropertyChanged(); } }

        public ICommand RefreshCommand { get; }
        public ICommand OptimizeAllCommand { get; }
        
        // Individual commands for actions
        public ICommand TcpAutoTuningCommand { get; }
        public ICommand DnsFlushCommand { get; }
        public ICommand CongestionControlCommand { get; }
        public ICommand NetworkThrottlingCommand { get; }

        public NetworkViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            OptimizeAllCommand = new RelayCommand(async _ => await OptimizeAllAsync());
            
            TcpAutoTuningCommand = new RelayCommand(async _ => await ApplyActionAsync("TCP Auto-Tuning", "TcpAutoTuning"));
            DnsFlushCommand = new RelayCommand(async _ => await ApplyActionAsync("DNS Resolver Flush", "DnsFlush"));
            CongestionControlCommand = new RelayCommand(async _ => await ApplyActionAsync("Congestion Control", "CongestionControl"));
            NetworkThrottlingCommand = new RelayCommand(async _ => await ApplyActionAsync("Network Throttling", "NetworkThrottling"));
            
            _ = Task.Run(() => LoadAsync());
        }

        private async Task LoadAsync()
        {
            Status = "Loading network info...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetNetworkStatus);
            if (r.Success && !string.IsNullOrEmpty(r.Data))
            {
                // Parse the real adapter data returned by the service
                Status = "Network info loaded";
                var lines = r.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Adapter 1: "))
                        AdapterName = trimmed.Substring(11).Trim();
                    else if (trimmed.StartsWith("Type: "))
                        ConnectionType = trimmed.Substring(6).Trim();
                    else if (trimmed.StartsWith("Speed: "))
                        DownloadSpeed = trimmed.Substring(7).Trim();
                    else if (trimmed.StartsWith("IP: "))
                    {
                        // Display IP in DownloadSpeed slot if speed not available
                    }
                }

                // Async ping for real latency
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var ping = new System.Net.NetworkInformation.Ping();
                        var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                        var latencyText = reply.Status == System.Net.NetworkInformation.IPStatus.Success
                            ? $"{reply.RoundtripTime} ms"
                            : "Timeout";
                        Application.Current?.Dispatcher.Invoke(() => Latency = latencyText);
                    }
                    catch
                    {
                        Application.Current?.Dispatcher.Invoke(() => Latency = "N/A");
                    }
                });
            }
            else
            {
                Status = "Service offline";
            }
        }

        private async Task ApplyActionAsync(string name, string actionId)
        {
            Status = $"Applying {name}...";
            IsOptimizing = true;
            // Using ApplyTier since ApplyAction doesn't exist in the IPC contract yet
            var r = await _ipc.SendRequestAsync(IpcMessageType.ApplyTier, actionId); 
            Status = r.Success ? $"✓ {name} optimized" : $"⚠ {name} failed: " + r.ErrorMessage;
            IsOptimizing = false;
        }

        private async Task OptimizeAllAsync()
        {
            Status = "Applying all network optimizations...";
            IsOptimizing = true;
            var r = await _ipc.SendRequestAsync(IpcMessageType.ApplyTier, "Normal"); // Example mapped to Normal for network items
            Status = r.Success ? "✓ All network optimizations applied" : "⚠ " + r.ErrorMessage;
            IsOptimizing = false;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // STORAGE
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // STORAGE
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class DriveInfoViewModel : ViewModelBase
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string CapacityText { get; set; } = string.Empty;
        public string UsedText { get; set; } = string.Empty;
        public string FreeText { get; set; } = string.Empty;
        public double UsagePercentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public Brush StatusColor { get; set; } = GetFrozenBrush(Color.FromRgb(76, 175, 80));
    }

    public class StorageViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Ready";
        private Brush _statusColor = GetFrozenBrush(Color.FromRgb(76, 175, 80));
        private string _cleanResult = "";
        private bool _isScanning;
        private bool _isCleaning;
        
        public ObservableCollection<DriveInfoViewModel> Drives { get; } = new();

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); UpdateStatusColor(); } }
        public Brush StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }
        
        private void UpdateStatusColor()
        {
            if (Status.Contains("❌") || Status.Contains("Failed") || Status.Contains("offline") || Status.Contains("error") || Status.Contains("No drives"))
                StatusColor = GetFrozenBrush(Color.FromRgb(244, 67, 54)); // Red
            else if (Status.Contains("Loading") || Status.Contains("Scanning") || Status.Contains("..."))
                StatusColor = GetFrozenBrush(Color.FromRgb(255, 152, 0)); // Amber
            else
                StatusColor = GetFrozenBrush(Color.FromRgb(76, 175, 80)); // Green
        }
        public string CleanResult { get => _cleanResult; set { _cleanResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCleanResult)); } }
        public Visibility HasCleanResult => string.IsNullOrEmpty(_cleanResult) ? Visibility.Collapsed : Visibility.Visible;
        
        public bool IsScanning { get => _isScanning; set { _isScanning = value; OnPropertyChanged(); } }
        public bool IsCleaning { get => _isCleaning; set { _isCleaning = value; OnPropertyChanged(); } }

        public ICommand RefreshCommand { get; }
        public ICommand ScanTempCommand { get; }
        public ICommand DeepCleanupCommand { get; }
        public ICommand ScanLargeFilesCommand { get; }
        public ICommand ScanDuplicatesCommand { get; }
        public ICommand CleanRecycleBinCommand { get; }

        public StorageViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            ScanTempCommand = new RelayCommand(async _ => await ScanAsync("Temporary Files"));
            DeepCleanupCommand = new RelayCommand(async _ => await CleanAsync("Deep Cleanup"));
            ScanLargeFilesCommand = new RelayCommand(async _ => await ScanAsync("Large Files"));
            ScanDuplicatesCommand = new RelayCommand(async _ => await ScanAsync("Duplicate Files"));
            CleanRecycleBinCommand = new RelayCommand(async _ => await CleanAsync("Recycle Bin"));
            
            _ = Task.Run(() => LoadAsync());
        }

        private async Task LoadAsync()
        {
            await UiDispatcher.RunAsync(() => Status = "Loading drives...");
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetStorageStatus);
            if (r.Success && !string.IsNullOrEmpty(r.Data))
            {
                try
                {
                    // Service now returns a JSON array of drive objects
                    var docs = System.Text.Json.JsonDocument.Parse(r.Data);
                    var newDrives = new List<DriveInfoViewModel>();
                    foreach (var el in docs.RootElement.EnumerateArray())
                    {
                        var pct = el.TryGetProperty("UsagePercentage", out var up) ? up.GetDouble() : 0;
                        var usedGb = el.TryGetProperty("UsedGb", out var ug) ? ug.GetDouble() : 0;
                        var totalGb = el.TryGetProperty("TotalGb", out var tg) ? tg.GetDouble() : 0;
                        var freeGb = el.TryGetProperty("FreeGb", out var fg) ? fg.GetDouble() : 0;
                        var name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";
                        var fmt = el.TryGetProperty("Format", out var f) ? f.GetString() ?? "" : "";
                        var statusStr = pct > 85 ? "Warning" : "Healthy";
                        Brush statusBrush = pct > 85
                            ? GetFrozenBrush(Color.FromRgb(255, 152, 0))
                            : GetFrozenBrush(Color.FromRgb(76, 175, 80));

                        newDrives.Add(new DriveInfoViewModel
                        {
                            Name = name.TrimEnd('\\'),
                            Format = fmt,
                            CapacityText = $"{totalGb:F1} GB",
                            UsedText = $"{usedGb:F1} GB",
                            FreeText = $"{freeGb:F1} GB",
                            UsagePercentage = pct,
                            Status = statusStr,
                            StatusColor = statusBrush
                        });
                    }

                    await UiDispatcher.RunAsync(() =>
                    {
                        Drives.Clear();
                        foreach (var d in newDrives) Drives.Add(d);
                        Status = Drives.Count == 0 ? "No drives detected" : "Storage info loaded";
                    });
                }
                catch (Exception ex)
                {
                    await UiDispatcher.RunAsync(() => Status = "❌ Failed to parse drive data: " + ex.Message);
                }
            }
            else
            {
                await UiDispatcher.RunAsync(() => Status = r.Success ? "❌ No data returned" : "❌ Service offline");
            }
        }

        private async Task ScanAsync(string target)
        {
            Status = $"Scanning {target}...";
            IsScanning = true;
            var r = await _ipc.SendRequestAsync(IpcMessageType.ScanCleaner, target);
            CleanResult = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? $"{target} scan complete" : "⚠ " + r.ErrorMessage;
            IsScanning = false;
        }

        private async Task CleanAsync(string target)
        {
            Status = $"Cleaning {target}...";
            IsCleaning = true;
            var r = await _ipc.SendRequestAsync(IpcMessageType.ApplyCleaner, target);
            CleanResult = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? $"✓ {target} clean complete" : "⚠ " + r.ErrorMessage;
            IsCleaning = false;
            await LoadAsync(); // Refresh drives after cleanup
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // TOOLS
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class ToolsViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Tools ready";
        private string _result = "";

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Result { get => _result; set { _result = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); } }
        public Visibility HasResult => string.IsNullOrEmpty(_result) ? Visibility.Collapsed : Visibility.Visible;

        public ICommand RamCleanCommand { get; }
        public ICommand TempCleanCommand { get; }
        public ICommand DiagnosticsCommand { get; }
        public ICommand BiosInfoCommand { get; }

        public ToolsViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            RamCleanCommand = new RelayCommand(async _ => await RunAsync(IpcMessageType.ApplyCleaner, "RAM", "RAM cleanup"));
            TempCleanCommand = new RelayCommand(async _ => await RunAsync(IpcMessageType.ApplyCleaner, "Temp", "Temp cleanup"));
            DiagnosticsCommand = new RelayCommand(async _ => await RunAsync(IpcMessageType.GetHardwareInfo, null, "Diagnostics"));
            BiosInfoCommand = new RelayCommand(async _ => await RunAsync(IpcMessageType.GetBiosCapabilities, null, "BIOS info"));
        }

        private async Task RunAsync(IpcMessageType type, string payload, string label)
        {
            Status = $"Running {label}...";
            var r = await _ipc.SendRequestAsync(type, payload);
            Result = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? $"✓ {label} complete" : $"⚠ {label}: " + r.ErrorMessage;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // BACKUP & RESTORE
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class BackupRestoreViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private string _status = "Backup & Restore ready";
        private string _backups = "";
        private string _result = "";

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Backups { get => _backups; set { _backups = value; OnPropertyChanged(); } }
        public string Result { get => _result; set { _result = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); } }
        public Visibility HasResult => string.IsNullOrEmpty(_result) ? Visibility.Collapsed : Visibility.Visible;

        public ICommand LoadBackupsCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand RestoreAllCommand { get; }

        public BackupRestoreViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            LoadBackupsCommand = new RelayCommand(async _ => await LoadAsync());
            RestoreCommand = new RelayCommand(async _ => await RestoreAsync());
            RestoreAllCommand = new RelayCommand(async _ => await RestoreAllSystemAsync());
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            Status = "Loading snapshots...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.GetBackups);
            Backups = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? "Snapshots loaded" : "⚠ " + r.ErrorMessage;
        }

        private async Task RestoreAsync()
        {
            Status = "Restoring last profile snapshot...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.RestoreBackup);
            Result = r.Success ? r.Data : r.ErrorMessage;
            Status = r.Success ? "✓ Restored" : "⚠ " + r.ErrorMessage;
        }

        private async Task RestoreAllSystemAsync()
        {
            Status = "Restoring all pre-execution registry and service states...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.RestoreAllSystem);
            Result = r.Success ? "Restored all system states." : "Failed: " + r.ErrorMessage;
            Status = r.Success ? "✓ Full System Restore Complete" : "⚠ " + r.ErrorMessage;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SETTINGS
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private bool _animationsEnabled = true;
        private bool _reducedMotion = false;
        private double _glassOpacity = 0.92;

        public bool AnimationsEnabled
        {
            get => _animationsEnabled;
            set { _animationsEnabled = value; OnPropertyChanged(); }
        }

        public bool ReducedMotion
        {
            get => _reducedMotion;
            set { _reducedMotion = value; OnPropertyChanged(); }
        }

        public double GlassOpacity
        {
            get => _glassOpacity;
            set { _glassOpacity = value; OnPropertyChanged(); }
        }

        public SettingsViewModel(IIpcClient ipc) { _ipc = ipc; }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ABOUT
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public class AboutViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        public string Version { get; }
        public string BuildDate =>
            System.IO.File.GetLastWriteTime(
                System.Reflection.Assembly.GetExecutingAssembly().Location
            ).ToString("yyyy-MM-dd");
        public string Description =>
            "Error Optimizer is a comprehensive Windows system optimization platform " +
            "with a production-quality backend and WPF frontend.\n\n" +
            "Backend: BiosOptimizer.Core, Detection, Safety, CLI\n" +
            "Frontend: BiosOptimizer.GUI (WPF)\n" +
            "Service: BiosOptimizer.Service (Windows Service)\n" +
            "IPC: Named Pipe (\\\\.\\pipe\\BiosOptimizer_IPC_v2)";

        public AboutViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var attr = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            Version = attr.Length > 0
                ? ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion
                : asm.GetName().Version?.ToString() ?? "Unknown";
        }
    }
}
