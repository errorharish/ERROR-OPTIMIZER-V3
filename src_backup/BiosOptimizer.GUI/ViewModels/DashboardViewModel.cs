using System;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.ViewModels
{
    public class GpuInfo : ViewModelBase
    {
        private string _name = "GPU";
        private int _percent;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public int Percent { get => _percent; set { _percent = value; OnPropertyChanged(); } }
    }

    public class DashboardViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private readonly DispatcherTimer _monitorTimer;
        private CancellationTokenSource _cts = new();

        // Score
        private double _score;
        private string _scoreLabel = "Loading...";
        private string _scoreStatus = "";
        private int _totalCount, _appliedCount, _remainingCount, _failedCount;

        // Health Metrics
        private int _healthScore, _performanceScore, _securityScore, _stabilityScore;

        // Monitor
        private int _cpuPercent, _ramPercent, _storagePercent;
        private string _cpuName = "CPU";
        private string _ramInfo = "", _systemInfo = "";
        private string _storageInfo = "";
        private string _powerPlan = "Balanced";

        private ObservableCollection<GpuInfo> _gpus = new();

        // Status
        private string _systemSummary = "Loading system information...";
        private string _statusMessage = "Ready to optimize";
        private string _lastRefresh = "";
        private string _lastOutput = "";
        private bool _isServiceOnline = false;
        private bool _isLoading = true;
        private string _retryStatus = "";

        public double Score { get => _score; private set { _score = value; OnPropertyChanged(); } }
        public string ScoreLabel { get => _scoreLabel; private set { _scoreLabel = value; OnPropertyChanged(); } }
        public string ScoreStatus { get => _scoreStatus; private set { _scoreStatus = value; OnPropertyChanged(); } }
        
        private Brush _statusColor = GetFrozenBrush(Color.FromRgb(76, 175, 80));
        public Brush StatusColor { get => _statusColor; private set { _statusColor = value; OnPropertyChanged(); } }

        public int TotalCount { get => _totalCount; private set { _totalCount = value; OnPropertyChanged(); } }
        public int AppliedCount { get => _appliedCount; private set { _appliedCount = value; OnPropertyChanged(); } }
        public int RemainingCount { get => _remainingCount; private set { _remainingCount = value; OnPropertyChanged(); } }
        public int FailedCount { get => _failedCount; private set { _failedCount = value; OnPropertyChanged(); } }

        public int HealthScore { get => _healthScore; private set { _healthScore = value; OnPropertyChanged(); } }
        public int PerformanceScore { get => _performanceScore; private set { _performanceScore = value; OnPropertyChanged(); } }
        public int SecurityScore { get => _securityScore; private set { _securityScore = value; OnPropertyChanged(); } }
        public int StabilityScore { get => _stabilityScore; private set { _stabilityScore = value; OnPropertyChanged(); } }

        public int CpuPercent { get => _cpuPercent; private set { _cpuPercent = value; OnPropertyChanged(); } }
        public ObservableCollection<GpuInfo> Gpus { get => _gpus; private set { _gpus = value; OnPropertyChanged(); } }
        public int RamPercent { get => _ramPercent; private set { _ramPercent = value; OnPropertyChanged(); } }
        public int StoragePercent { get => _storagePercent; private set { _storagePercent = value; OnPropertyChanged(); } }

        public string CpuName { get => _cpuName; private set { _cpuName = value; OnPropertyChanged(); } }
        public string RamInfo { get => _ramInfo; private set { _ramInfo = value; OnPropertyChanged(); } }
        public string SystemInfo { get => _systemInfo; private set { _systemInfo = value; OnPropertyChanged(); } }
        public string StorageInfo { get => _storageInfo; private set { _storageInfo = value; OnPropertyChanged(); } }
        public string PowerPlan { get => _powerPlan; private set { _powerPlan = value; OnPropertyChanged(); } }

        public string SystemSummary { get => _systemSummary; private set { _systemSummary = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
        public string LastRefresh { get => _lastRefresh; private set { _lastRefresh = value; OnPropertyChanged(); } }
        public string LastOutput { get => _lastOutput; private set { _lastOutput = value; OnPropertyChanged(); } }
        public string RetryStatus { get => _retryStatus; private set { _retryStatus = value; OnPropertyChanged(); } }
        public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); } }

        public Visibility IsOnlineVisibility => _isServiceOnline ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsOfflineVisibility => _isServiceOnline ? Visibility.Collapsed : Visibility.Visible;
        public Visibility HasOutput => string.IsNullOrEmpty(_lastOutput) ? Visibility.Collapsed : Visibility.Visible;

        public ICommand RefreshCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand QuickOptimizeCommand { get; }
        public ICommand RamCleanupCommand { get; }
        public ICommand TempCleanCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand StartupCommand { get; }
        public ICommand BiosCommand { get; }
        public ICommand NetworkCommand { get; }

        public DashboardViewModel(IIpcClient ipc)
        {
            _ipc = ipc;

            RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
            RetryCommand = new RelayCommand(async _ => await RetryAsync());
            QuickOptimizeCommand = new RelayCommand(async _ => await QuickOptimizeAsync());
            RamCleanupCommand = new RelayCommand(async _ => await RunCleanerAsync("RAM"));
            TempCleanCommand = new RelayCommand(async _ => await RunCleanerAsync("Temp"));
            ScanCommand = new RelayCommand(async _ => await RefreshAsync());
            StartupCommand = new RelayCommand(_ => { });
            BiosCommand = new RelayCommand(_ => { });
            NetworkCommand = new RelayCommand(_ => { });

            // Monitor timer — 3 second intervals
            _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _monitorTimer.Tick += async (s, e) => await UpdateMonitorAsync();
            _monitorTimer.Start();

            // Initial load
            _ = Task.Run(() => RefreshAsync());
        }

        private async Task RefreshAsync()
        {
            IsLoading = true;
            LastRefresh = "Refreshing...";
            Application.Current.Dispatcher.Invoke(() => StatusColor = GetFrozenBrush(Color.FromRgb(255, 152, 0))); // Amber while loading

            var response = await _ipc.SendRequestAsync(IpcMessageType.GetOptimizationScore, null, _cts.Token);

            if (response.Success && !string.IsNullOrEmpty(response.Data))
            {
                SetOnline(true);
                ParseScoreResponse(response.Data);
                Application.Current.Dispatcher.Invoke(() => StatusColor = GetFrozenBrush(Color.FromRgb(76, 175, 80))); // Green on success
                IsLoading = false;
            }
            else
            {
                SetOnline(false);
                StatusMessage = "Service offline — " + (response.ErrorMessage ?? "unknown error");
                Application.Current.Dispatcher.Invoke(() => StatusColor = GetFrozenBrush(Color.FromRgb(244, 67, 54))); // Red on error
                // Do NOT set IsLoading = false on error, keep the center text hidden
            }

            LastRefresh = "Updated " + DateTime.Now.ToString("HH:mm:ss");
            await UpdateMonitorAsync();
            await UpdateSystemInfoAsync();
        }

        private async Task RetryAsync()
        {
            RetryStatus = "Connecting...";
            await Task.Delay(500);
            await RefreshAsync();
            RetryStatus = _isServiceOnline ? "Connected!" : "Service not found. Is it running?";
        }

        private void ParseScoreResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                Score = root.TryGetProperty("Score", out var s) ? s.GetDouble() : 0;
                TotalCount = root.TryGetProperty("TotalActions", out var t) ? t.GetInt32() : 0;
                AppliedCount = root.TryGetProperty("AppliedActions", out var a) ? a.GetInt32() : 0;
                RemainingCount = root.TryGetProperty("AvailableActions", out var r) ? r.GetInt32() : 0;

                ScoreLabel = Score switch
                {
                    >= 90 => "Excellent",
                    >= 75 => "Good",
                    >= 55 => "Fair",
                    >= 35 => "Needs Work",
                    _     => "Critical"
                };
                ScoreStatus = "";
                StatusMessage = $"System is {ScoreLabel.ToLower()} — {RemainingCount} optimizations available";

                // Derive sub-scores
                HealthScore = (int)(Score * 0.95);
                PerformanceScore = (int)(Score * 1.0);
                SecurityScore = (int)(Score * 0.90);
                StabilityScore = (int)(Score * 0.98);
            }
            catch { Score = 0; ScoreLabel = "Error"; }
        }

        private T RunWmiQuery<T>(Func<T> query, T defaultValue, int timeoutMs = 2000)
        {
            try
            {
                var task = Task.Run(query);
                if (task.Wait(timeoutMs))
                    return task.Result;
            }
            catch { }
            return defaultValue;
        }

        private async Task UpdateMonitorAsync()
        {
            try
            {
                var r = await _ipc.SendRequestAsync(IpcMessageType.GetDashboard, null, _cts.Token);
                if (r.Success && !string.IsNullOrEmpty(r.Data))
                {
                    using var doc = JsonDocument.Parse(r.Data);
                    var root = doc.RootElement;
                    
                    CpuPercent = root.TryGetProperty("CpuUtilization", out var c) ? (int)c.GetDouble() : 0;
                    RamPercent = root.TryGetProperty("RamPercentage", out var rp) ? (int)rp.GetDouble() : 0;
                    
                    double usedMb = root.TryGetProperty("RamUsedMb", out var u) ? u.GetDouble() : 0;
                    double totalMb = root.TryGetProperty("RamTotalMb", out var t) ? t.GetDouble() : 0;
                    RamInfo = $"{(usedMb / 1024):F1} GB / {(totalMb / 1024):F1} GB";

                    if (root.TryGetProperty("Gpus", out var gpusElement))
                    {
                        int index = 0;
                        foreach (var gpuElem in gpusElement.EnumerateArray())
                        {
                            var name = gpuElem.TryGetProperty("Name", out var n) ? n.GetString() : "GPU";
                            var util = gpuElem.TryGetProperty("Utilization", out var ut) ? (int)ut.GetDouble() : 0;
                            
                            if (index >= Gpus.Count)
                            {
                                Application.Current.Dispatcher.Invoke(() => Gpus.Add(new GpuInfo { Name = name, Percent = util }));
                            }
                            else
                            {
                                Application.Current.Dispatcher.Invoke(() => 
                                {
                                    Gpus[index].Name = name;
                                    Gpus[index].Percent = util;
                                });
                            }
                            index++;
                        }
                        
                        // Remove extras
                        while (Gpus.Count > index)
                        {
                            Application.Current.Dispatcher.Invoke(() => Gpus.RemoveAt(Gpus.Count - 1));
                        }
                    }

                    StoragePercent = root.TryGetProperty("StoragePercentage", out var sp) ? (int)sp.GetDouble() : 0;
                    StorageInfo = root.TryGetProperty("StorageInfo", out var si) ? si.GetString() : "C:\\";
                    PowerPlan = root.TryGetProperty("PowerPlan", out var pp) ? pp.GetString() : "Balanced";
                }
            }
            catch { /* monitoring is best-effort */ }
        }

        private async Task UpdateSystemInfoAsync()
        {
            try
            {
                var r = await _ipc.SendRequestAsync(IpcMessageType.GetMachineProfile, null, _cts.Token);
                if (r.Success && r.Data != null)
                {
                    using var doc = JsonDocument.Parse(r.Data);
                    var root = doc.RootElement;
                    var mfg = root.TryGetProperty("Manufacturer", out var mf) ? mf.GetString() : "Unknown";
                    var model = root.TryGetProperty("Model", out var mo) ? mo.GetString() : "Unknown";
                    var cpu = root.TryGetProperty("CpuModel", out var cp) ? cp.GetString() : "Unknown";
                    var ram = root.TryGetProperty("TotalRamBytes", out var ra) ? ra.GetInt64() : 0;
                    var bios = root.TryGetProperty("BiosVersion", out var bv) ? bv.GetString() : "Unknown";
                    var os = root.TryGetProperty("WindowsVersion", out var ov) ? ov.GetString() : "Unknown";
                    var type = root.TryGetProperty("MachineType", out var ty) ? ty.GetString() : "Unknown";
                    
                    string gpuNames = "Unknown";
                    if (root.TryGetProperty("Gpus", out var gpus) && gpus.GetArrayLength() > 0)
                    {
                        var names = new List<string>();
                        foreach (var g in gpus.EnumerateArray())
                        {
                            if (g.TryGetProperty("Name", out var gn)) names.Add(gn.GetString());
                        }
                        gpuNames = string.Join(" | ", names);
                    }

                    SystemSummary = $"{mfg} {model} | {cpu}";
                    SystemInfo = $"SYSTEM PROFILE\n" +
                                 $"{mfg} {model}\n" +
                                 $"{cpu}\n" +
                                 $"{gpuNames}\n" +
                                 $"{(ram / (1024*1024*1024))} GB RAM\n" +
                                 $"Windows {os}\n" +
                                 $"{type}\n" +
                                 $"BIOS: {bios}";
                }
            }
            catch { }
        }

        private async Task QuickOptimizeAsync()
        {
            StatusMessage = "Applying Normal optimization...";
            var r = await _ipc.SendRequestAsync(IpcMessageType.ApplyTier, "Normal", _cts.Token);
            LastOutput = r.Success ? r.Data : r.ErrorMessage;
            OnPropertyChanged(nameof(HasOutput));
            StatusMessage = r.Success ? "✓ Optimization applied successfully" : "⚠ " + r.ErrorMessage;
            await RefreshAsync();
        }

        private async Task RunCleanerAsync(string target)
        {
            StatusMessage = $"Running {target} cleanup...";
            var scan = await _ipc.SendRequestAsync(IpcMessageType.ScanCleaner, target, _cts.Token);
            if (scan.Success)
            {
                var apply = await _ipc.SendRequestAsync(IpcMessageType.ApplyCleaner, target, _cts.Token);
                LastOutput = apply.Success ? apply.Data : apply.ErrorMessage;
                OnPropertyChanged(nameof(HasOutput));
                StatusMessage = apply.Success ? $"✓ {target} cleanup complete" : "⚠ " + apply.ErrorMessage;
            }
            else
            {
                StatusMessage = "⚠ Scan failed: " + scan.ErrorMessage;
            }
        }

        private void SetOnline(bool online)
        {
            _isServiceOnline = online;
            OnPropertyChanged(nameof(IsOnlineVisibility));
            OnPropertyChanged(nameof(IsOfflineVisibility));
        }

        public void Dispose()
        {
            _monitorTimer.Stop();
            _cts.Cancel();
        }
    }
}
