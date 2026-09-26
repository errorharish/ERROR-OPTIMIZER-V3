using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;

namespace BiosOptimizer.GUI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase _currentView;
        private readonly IIpcClient _ipc;
        private readonly Dictionary<string, Func<ViewModelBase>> _viewFactory;
        private readonly Dictionary<string, ViewModelBase> _viewCache;
        private readonly System.Windows.Threading.DispatcherTimer _healthTimer;

        public event Action<string> NavigationChanged;

        public ViewModelBase CurrentView
        {
            get => _currentView;
            private set { _currentView = value; OnPropertyChanged(); }
        }

        public ICommand NavigateCommand { get; }
        
        // â•â• Global Toast Overlay â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private bool _showToast;
        private string _toastTitle = "";
        private string _toastMessage = "";
        
        public bool ShowToast { get => _showToast; set { _showToast = value; OnPropertyChanged(); } }
        public string ToastTitle { get => _toastTitle; set { _toastTitle = value; OnPropertyChanged(); } }
        public string ToastMessage { get => _toastMessage; set { _toastMessage = value; OnPropertyChanged(); } }
        
        // â•â• Premium Result Overlay â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private bool _showResultOverlay;
        private string _resultType = "Success"; // "Success", "Partial", "Failed"
        private string _resultTitle = "";
        private string _resultSubtitle = "";
        private int _resultOptimizedCount;
        private int _resultAlreadyOptimizedCount;
        private int _resultNotAvailableCount;
        private int _resultNotApplicableCount;
        private int _resultBlockedCount;
        private int _resultFailedCount;
        private string _resultPrimaryReason = "";
        
        public bool ShowResultOverlay { get => _showResultOverlay; set { _showResultOverlay = value; OnPropertyChanged(); } }
        public string ResultType { get => _resultType; set { _resultType = value; OnPropertyChanged(); } }
        public string ResultTitle { get => _resultTitle; set { _resultTitle = value; OnPropertyChanged(); } }
        public string ResultSubtitle { get => _resultSubtitle; set { _resultSubtitle = value; OnPropertyChanged(); } }
        public int ResultOptimizedCount { get => _resultOptimizedCount; set { _resultOptimizedCount = value; OnPropertyChanged(); } }
        public int ResultAlreadyOptimizedCount { get => _resultAlreadyOptimizedCount; set { _resultAlreadyOptimizedCount = value; OnPropertyChanged(); } }
        public int ResultNotAvailableCount { get => _resultNotAvailableCount; set { _resultNotAvailableCount = value; OnPropertyChanged(); } }
        public int ResultNotApplicableCount { get => _resultNotApplicableCount; set { _resultNotApplicableCount = value; OnPropertyChanged(); } }
        public int ResultBlockedCount { get => _resultBlockedCount; set { _resultBlockedCount = value; OnPropertyChanged(); } }
        public int ResultFailedCount { get => _resultFailedCount; set { _resultFailedCount = value; OnPropertyChanged(); } }
        public string ResultPrimaryReason { get => _resultPrimaryReason; set { _resultPrimaryReason = value; OnPropertyChanged(); } }
        
        public void ShowGlobalToast(string title, string message)
        {
            ToastTitle = title;
            ToastMessage = message;
            ShowToast = true;
            _ = HideToastAsync();
        }
        
        public void ShowOptimizationResult(string type, string title, string subtitle, int optimized, int alreadyOptimized, int notAvailable, int notApplicable, int blocked, int failed, string primaryReason)
        {
            ResultType = type;
            ResultTitle = title;
            ResultSubtitle = subtitle;
            ResultOptimizedCount = optimized;
            ResultAlreadyOptimizedCount = alreadyOptimized;
            ResultNotAvailableCount = notAvailable;
            ResultNotApplicableCount = notApplicable;
            ResultBlockedCount = blocked;
            ResultFailedCount = failed;
            ResultPrimaryReason = primaryReason;
            ShowResultOverlay = true;
            _ = HideResultOverlayAsync();
        }
        
        private async Task HideToastAsync()
        {
            await Task.Delay(5000);
            ShowToast = false;
        }

        private async Task HideResultOverlayAsync()
        {
            await Task.Delay(5000);
            ShowResultOverlay = false;
        }

        // â”€â”€ Build IDs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private string _guiBuildId = "Checking...";
        public string GuiBuildId { get => _guiBuildId; set { _guiBuildId = value; OnPropertyChanged(); } }

        private string _serviceBuildId = "Connecting...";
        public string ServiceBuildId
        {
            get => _serviceBuildId;
            set
            {
                _serviceBuildId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVersionMismatch));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
            }
        }

        // â”€â”€ Service connection state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Three strict states: Connecting | Connected | Offline
        private enum ConnectionState { Connecting, Connected, Offline }
        private ConnectionState _connectionState = ConnectionState.Connecting;

        private void SetConnectionState(ConnectionState state)
        {
            _connectionState = state;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
        }

        public bool IsVersionMismatch
            => _connectionState == ConnectionState.Connected
            && ServiceBuildId != "Connecting..."
            && ServiceBuildId != "Offline"
            && ServiceBuildId != "Protocol Mismatch"
            && ServiceBuildId != GuiBuildId;

        public string StatusText
        {
            get
            {
                return _connectionState switch
                {
                    ConnectionState.Connecting => "Connecting to Service...",
                    ConnectionState.Offline    => "Service Offline",
                    ConnectionState.Connected  => IsVersionMismatch
                                                    ? "Version mismatch â€” restarting backend..."
                                                    : "Service Connected",
                    _ => "Unknown"
                };
            }
        }

        public System.Windows.Media.Brush StatusBrush
        {
            get
            {
                return _connectionState switch
                {
                    ConnectionState.Connecting => System.Windows.Media.Brushes.Orange,
                    ConnectionState.Offline    => System.Windows.Media.Brushes.Red,
                    ConnectionState.Connected  => IsVersionMismatch
                                                    ? System.Windows.Media.Brushes.Red
                                                    : (System.Windows.Media.Brush)System.Windows.Application.Current.Dispatcher.Invoke(() => { var b = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); b.Freeze(); return b; }),
                    _ => System.Windows.Media.Brushes.Gray
                };
            }
        }

        public MainViewModel(IIpcClient ipc)
        {
            _ipc = ipc;

            _viewFactory = new Dictionary<string, Func<ViewModelBase>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dashboard"]      = () => new DashboardViewModel(ipc),
                ["Normal"]         = () => new TierViewModel(ipc, "Normal",             "Normal",          "Safe optimizations for everyday use."),
                ["Pro"]            = () => new TierViewModel(ipc, "Pro",                "Pro",             "Advanced optimizations for power users."),
                ["Ultimate"]       = () => new TierViewModel(ipc, "Ultimate",           "Ultimate",        "Maximum system overhaul. High risk."),
                ["Debloat"]        = () => new DebloatViewModel(ipc),
                ["BiosSafe"]       = () => new TierViewModel(ipc, "BiosSafe",           "BIOS Safe",       "Configure and secure detected BIOS settings."),
                ["MaxPerformance"] = () => new TierViewModel(ipc, "MaximumPerformance", "Max Performance", "Extreme Windows tuning for maximum FPS & responsiveness."),
                ["SystemInfo"]     = () => new SystemInfoViewModel(ipc),
                ["StartupManager"] = () => new StartupManagerViewModel(ipc),
                ["ServiceManager"] = () => new ServiceManagerViewModel(ipc),
                ["Network"]        = () => new NetworkViewModel(ipc),
                ["Storage"]        = () => new StorageViewModel(ipc),
                ["Tools"]          = () => new ToolsViewModel(ipc),
                ["BackupRestore"]  = () => new BackupRestoreViewModel(ipc),
                ["Settings"]       = () => new SettingsViewModel(ipc),
                ["About"]          = () => new AboutViewModel(ipc),
                ["ProcessReduction"] = () => new ProcessReductionViewModel(ipc),
                ["InputOptimizer"] = () => new InputOptimizerViewModel(ipc), ["RegistryTweak"] = () => new RegistryTweakViewModel(ipc),
            };

            _viewCache = new Dictionary<string, ViewModelBase>(StringComparer.OrdinalIgnoreCase);

            NavigateCommand = new RelayCommand(route =>
            {
                var key = route?.ToString() ?? "Dashboard";
                if (_viewFactory.TryGetValue(key, out var factory))
                {
                    if (!_viewCache.TryGetValue(key, out var vm))
                    {
                        vm = factory();
                        _viewCache[key] = vm;
                    }
                    CurrentView = vm;
                    NavigationChanged?.Invoke(key);
                }
            });

            // Resolve GUI build ID from assembly attributes
            var attr = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            GuiBuildId = attr != null && attr.Length > 0
                ? ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion
                : System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

            // â”€â”€ AUTHORITATIVE STARTUP HANDSHAKE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // We do NOT mark "Service Connected" until WaitForReadyAsync
            // completes a real GetVersion round-trip. This prevents the
            // "Connected but timeout" bug.
            _ = StartupHandshakeAsync();

            // Periodic health check every 5 s to detect backend crashes/restarts
            _healthTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _healthTimer.Tick += async (s, e) => await CheckVersionAsync();
            _healthTimer.Start();
        }

        /// <summary>
        /// Blocks until the backend is provably ready (max 30 s), then
        /// sets the status to Connected/Offline and navigates to Dashboard.
        /// </summary>
        private async Task StartupHandshakeAsync()
        {
            SetConnectionState(ConnectionState.Connecting);

            bool ready = await _ipc.WaitForReadyAsync(TimeSpan.FromSeconds(30));

            if (ready)
            {
                ServiceBuildId = _ipc.ServiceBuildId;
                SetConnectionState(ConnectionState.Connected);

                if (IsVersionMismatch)
                {
                    await HandleVersionMismatch();
                    return;
                }
            }
            else
            {
                ServiceBuildId = "Offline";
                SetConnectionState(ConnectionState.Offline);
            }

            // Auto-navigate to Dashboard once backend is confirmed
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                NavigateCommand.Execute("Dashboard"));
        }

        /// <summary>
        /// Periodic health check â€” updates status without blocking navigation.
        /// </summary>
        private async Task CheckVersionAsync()
        {
            var res = await _ipc.SendRequestAsync(BiosOptimizer.IPC.Contracts.IpcMessageType.GetVersion, "");
            if (res.Success && !string.IsNullOrEmpty(res.Data))
            {
                try
                {
                    var handshake = System.Text.Json.JsonSerializer
                        .Deserialize<BiosOptimizer.IPC.Contracts.IpcHandshakeResponse>(res.Data);

                    if (handshake?.ProtocolVersion == "v2")
                    {
                        ServiceBuildId = handshake.BuildId;
                        SetConnectionState(ConnectionState.Connected);

                        if (IsVersionMismatch)
                            await HandleVersionMismatch();
                    }
                    else
                    {
                        ServiceBuildId = "Protocol Mismatch";
                        SetConnectionState(ConnectionState.Offline);
                    }
                }
                catch
                {
                    ServiceBuildId = "Offline";
                    SetConnectionState(ConnectionState.Offline);
                }
            }
            else
            {
                ServiceBuildId = "Offline";
                SetConnectionState(ConnectionState.Offline);
            }
        }

        private async Task HandleVersionMismatch()
        {
            ServiceBuildId = "Application components are out of sync. Restarting backend...";
            SetConnectionState(ConnectionState.Connecting);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (System.Windows.Application.Current is App app)
                    app.RestartBackend();
            });

            // Re-poll after restart
            bool ready = await _ipc.WaitForReadyAsync(TimeSpan.FromSeconds(20));
            if (ready)
            {
                ServiceBuildId = _ipc.ServiceBuildId;
                SetConnectionState(ConnectionState.Connected);
            }
            else
            {
                ServiceBuildId = "Offline";
                SetConnectionState(ConnectionState.Offline);
            }
        }
    }
}



