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

        // ── 10-Second Startup About Intro Flow ──────────────────────────
        private bool _isStartupIntroActive = false;
        public bool IsStartupIntroActive
        {
            get => _isStartupIntroActive;
            set { _isStartupIntroActive = value; OnPropertyChanged(); }
        }

        private bool _isStartupIntroLocked = false;
        public bool IsStartupIntroLocked
        {
            get => _isStartupIntroLocked;
            set
            {
                if (_isStartupIntroLocked != value)
                {
                    _isStartupIntroLocked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNavigationUnlocked));
                    UiDispatcher.Run(() => System.Windows.Input.CommandManager.InvalidateRequerySuggested());
                }
            }
        }

        public bool IsNavigationUnlocked => !_isStartupIntroLocked;

        private int _startupIntroCountdown = 10;
        public int StartupIntroCountdown
        {
            get => _startupIntroCountdown;
            set
            {
                _startupIntroCountdown = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartupIntroCountdownText));
            }
        }

        public string StartupIntroCountdownText => $"Starting Dashboard in {_startupIntroCountdown}s...";

        private System.Windows.Threading.DispatcherTimer? _startupIntroTimer;

        public void StartStartupIntroSequence()
        {
            if (App.IsStartupBackground)
            {
                IsStartupIntroLocked = false;
                IsStartupIntroActive = false;
                NavigateCommand.Execute("Dashboard");
                return;
            }

            IsStartupIntroActive = true;
            IsStartupIntroLocked = true;
            StartupIntroCountdown = 10;

            _startupIntroTimer?.Stop();
            _startupIntroTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _startupIntroTimer.Tick += (s, e) =>
            {
                StartupIntroCountdown--;
                if (StartupIntroCountdown <= 0)
                {
                    _startupIntroTimer.Stop();
                    _startupIntroTimer = null;
                    IsStartupIntroLocked = false;
                    IsStartupIntroActive = false;

                    // Automatically navigate to Dashboard
                    UiDispatcher.Run(() =>
                    {
                        NavigateCommand.Execute("Dashboard");
                    });
                }
            };

            _startupIntroTimer.Start();
        }

        

        public Action<Exception> _showGlobalErrorAction;

        public void ShowGlobalError(Exception ex)

        {

            _showGlobalErrorAction?.Invoke(ex);

        }

        

        // ------ Global Toast Overlay ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        private bool _showToast;

        private string _toastTitle = "";

        private string _toastMessage = "";

        

        public bool ShowToast { get => _showToast; set { _showToast = value; OnPropertyChanged(); } }

        public string ToastTitle { get => _toastTitle; set { _toastTitle = value; OnPropertyChanged(); } }

        public string ToastMessage { get => _toastMessage; set { _toastMessage = value; OnPropertyChanged(); } }

        

        // ------ Premium Result Overlay ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

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

        // ------ Build IDs ------------------------------------------------------------------------------------------------------------------------------------------

        private string _guiBuildId = "Checking...";

        public string GuiBuildId { get => _guiBuildId; set { _guiBuildId = value; OnPropertyChanged(); } }
        public string ProcessPath => Environment.ProcessPath ?? AppContext.BaseDirectory;


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

        // - Service connection state -
        // Four states: Connecting | Connected | Degraded | Offline
        public IpcConnectionState ConnectionState => _ipc.ConnectionState;

        private void SetConnectionState(IpcConnectionState state)
        {
            _ipc.SetConnectionState(state);
            OnPropertyChanged(nameof(ConnectionState));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
        }

        public bool IsVersionMismatch
            => ConnectionState == IpcConnectionState.Connected
            && ServiceBuildId != "Connecting..."
            && ServiceBuildId != "Offline"
            && ServiceBuildId != "Protocol Mismatch"
            && !IpcClient.AreBuildIdsCompatible(GuiBuildId, ServiceBuildId);

        public string StatusText
        {
            get
            {
                return ConnectionState switch
                {
                    IpcConnectionState.Connecting => "Connecting to Service...",
                    IpcConnectionState.Degraded   => "Service Degraded - running offline",
                    IpcConnectionState.Offline    => "Service Offline",
                    IpcConnectionState.Connected  => IsVersionMismatch
                                                    ? "Version mismatch - restarting backend..."
                                                    : "Service Connected",
                    _ => "Unknown"
                };
            }
        }

        public System.Windows.Media.Brush StatusBrush
        {
            get
            {
                return ConnectionState switch
                {
                    IpcConnectionState.Connecting => System.Windows.Media.Brushes.Orange,
                    IpcConnectionState.Degraded   => System.Windows.Media.Brushes.Orange,
                    IpcConnectionState.Offline    => System.Windows.Media.Brushes.Red,
                    IpcConnectionState.Connected  => IsVersionMismatch
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

                ["Dashboard"] = () =>
                {
                    var dashVm = new DashboardViewModel(ipc);
                    dashVm.RequestNavigate += route => NavigateCommand.Execute(route);
                    return dashVm;
                },

                ["Normal"]         = () => new TierViewModel(ipc, "Normal",             "Normal",          "Safe optimizations for everyday use."),

                ["Pro"]            = () => new TierViewModel(ipc, "Pro",                "Pro",             "Advanced optimizations for power users."),

                ["Ultimate"]       = () => new TierViewModel(ipc, "Ultimate",           "Ultimate",        "Maximum system overhaul. High risk."),

                ["Debloat"]        = () => new TierViewModel(ipc, "Debloat", "Debloat", "Removes telemetry, diagnostic tracking, and background Windows bloatware."),

                ["BiosSafe"]       = () => new TierViewModel(ipc, "BiosSafe", "BIOS Safe", "Safe firmware-aware analysis and UEFI hardware optimization."),

                ["MaxPerformance"] = () => new TierViewModel(ipc, "MaximumPerformance", "Maximum Performance", "Extreme optimizations for gaming and rendering."),

                ["V4"] = () => new V4ViewModel(),

                ["AiOptimization"] = () => new AiOptimizationViewModel(ipc),

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

                ["OneClick"]       = () =>
                {
                    var vm = new OneClickOptimizationViewModel(ipc);
                    vm.RequestNavigate += route => UiDispatcher.Run(() => _ = NavigateAsync(route));
                    return vm;
                },

                ["InputOptimizer"] = () => new InputOptimizerViewModel(ipc), 
                ["RegistryTweak"]  = () => new RegistryTweakViewModel(ipc),
                ["RegistryValues"] = () => new RegistryValuesViewModel(ipc),
                ["RegistryValuesRam"] = () => new RegistryValuesViewModel(ipc),
                ["RegistryValuesGpu"] = () => new RegistryValuesViewModel(ipc),
                ["GpuRegistryValues"] = () => new RegistryValuesViewModel(ipc),
                ["AiRamLimiter"]   = () => new AiOptimizationViewModel(ipc),
                ["AiWorkload"]     = () => new AiOptimizationViewModel(ipc),
                ["AiPowerPlan"]    = () => new AiOptimizationViewModel(ipc),
            };

            _viewCache = new Dictionary<string, ViewModelBase>(StringComparer.OrdinalIgnoreCase);

            NavigateCommand = new RelayCommand(async route => await NavigateAsync(route?.ToString() ?? "Dashboard"), route => !IsStartupIntroLocked || string.Equals(route?.ToString(), "About", StringComparison.OrdinalIgnoreCase));

            // Global error handler for Dispatcher exceptions (preventing blank pages)
            _showGlobalErrorAction = (ex) => 
            {
                var errorVm = new ErrorViewModel(
                    error: $"UI Rendering Error: {ex.GetType().Name}",
                    stack: $"{ex.Message}\n\n{ex.StackTrace}",
                    retryAction: () => NavigateCommand.Execute("Dashboard")
                );
                
                // We MUST set it safely via UiDispatcher
                UiDispatcher.Run(() => 
                {
                    CurrentView = errorVm;
                    NavigationChanged?.Invoke("Error");
                });
            };

            // Resolve GUI build ID from assembly attributes
            var attr = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);

            GuiBuildId = attr != null && attr.Length > 0
                ? ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion
                : System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

            // - AUTHORITATIVE STARTUP HANDSHAKE -
            // We do NOT mark "Service Connected" until WaitForReadyAsync
            // completes a real GetVersion round-trip. This prevents the
            // "Connected but timeout" bug.
            _ = StartupHandshakeAsync();

            // Subscribe to authoritative connection state changes
            _ipc.ConnectionStateChanged += state =>
            {
                UiDispatcher.Run(async () =>
                {
                    OnPropertyChanged(nameof(ConnectionState));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBrush));
                    if (state == IpcConnectionState.Connected && CurrentView != null)
                    {
                        try { await CurrentView.OnNavigatedToAsync(); } catch { }
                    }
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
            };

            // Periodic health check every 5 s to detect backend crashes/restarts
            _healthTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            _healthTimer.Tick += async (s, e) => await CheckVersionAsync();
            _healthTimer.Start();

            BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupPhaseStart("NAVIGATION INITIALIZATION");
            BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 06", "Navigation initialized");
            BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("NAVIGATION INITIALIZATION");
        }

        private readonly System.Threading.SemaphoreSlim _navLock = new(1, 1);

        public async Task NavigateAsync(string key)
        {
            if (!await _navLock.WaitAsync(250))
            {
                System.Diagnostics.Debug.WriteLine($"[NAV] Discarding re-entrant navigation click: {key}");
                return;
            }

            try
            {
                // 10-Second Startup Intro Guard
                if (IsStartupIntroLocked && !key.Equals("About", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"[NAV LOCKED] Navigation to '{key}' is locked during 10s startup intro ({StartupIntroCountdown}s remaining).");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[NAV] Click Route: {key}");
                BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.RecordNavigation(key);

                if (key.Equals("Dashboard", StringComparison.OrdinalIgnoreCase))
                {
                    BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupPhaseStart("DASHBOARD INITIALIZATION");
                    BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 07", "Dashboard shell initialized");
                    BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("DASHBOARD INITIALIZATION");
                }

                ViewModelBase? vm = null;

                if (key.Equals("RegistryValuesRam", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_viewCache.TryGetValue("RegistryValues", out vm))
                    {
                        vm = _viewFactory["RegistryValues"]();
                        _viewCache["RegistryValues"] = vm;
                    }
                    if (vm is RegistryValuesViewModel rv)
                    {
                        rv.SelectRamTabCommand.Execute(null);
                    }
                }
                else if (key.Equals("RegistryValuesGpu", StringComparison.OrdinalIgnoreCase) || key.Equals("GpuRegistryValues", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_viewCache.TryGetValue("RegistryValues", out vm))
                    {
                        vm = _viewFactory["RegistryValues"]();
                        _viewCache["RegistryValues"] = vm;
                    }
                    if (vm is RegistryValuesViewModel rv)
                    {
                        rv.SelectGpuTabCommand.Execute(null);
                    }
                }
                else if (key.Equals("AiRamLimiter", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_viewCache.TryGetValue("AiOptimization", out vm))
                    {
                        vm = _viewFactory["AiOptimization"]();
                        _viewCache["AiOptimization"] = vm;
                    }
                    if (vm is AiOptimizationViewModel ai)
                    {
                        ai.SelectRamLimiterTabCommand.Execute(null);
                    }
                }
                else if (key.Equals("AiWorkload", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_viewCache.TryGetValue("AiOptimization", out vm))
                    {
                        vm = _viewFactory["AiOptimization"]();
                        _viewCache["AiOptimization"] = vm;
                    }
                    if (vm is AiOptimizationViewModel ai)
                    {
                        ai.SelectWorkloadTabCommand.Execute(null);
                    }
                }
                else if (key.Equals("AiPowerPlan", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_viewCache.TryGetValue("AiOptimization", out vm))
                    {
                        vm = _viewFactory["AiOptimization"]();
                        _viewCache["AiOptimization"] = vm;
                    }
                    if (vm is AiOptimizationViewModel ai)
                    {
                        ai.SelectPowerPlanTabCommand.Execute(null);
                    }
                }
                else if (_viewFactory.TryGetValue(key, out var factory))
                {
                    if (!_viewCache.TryGetValue(key, out vm))
                    {
                        vm = factory();
                        _viewCache[key] = vm;
                    }
                }

                if (vm != null)
                {
                    var oldView = CurrentView;
                    if (oldView != null)
                    {
                        try
                        {
                            await oldView.OnNavigatedFromAsync();
                        }
                        catch { }
                    }

                    CurrentView = vm;
                    System.Diagnostics.Debug.WriteLine($"[NAVIGATION] Route = {key} | ViewModel = {vm?.GetType().Name} | Build = {GuiBuildId}");
                    NavigationChanged?.Invoke(key);

                    try
                    {
                        await vm.OnNavigatedToAsync();
                    }
                    catch (Exception navToEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NAVIGATION WARNING] OnNavigatedToAsync threw for {key}: {navToEx.Message}");
                        BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogCrash(navToEx, $"Navigation.{key}.OnNavigatedTo", isFatal: false);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[NAV] Route '{key}' failed to resolve!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NAVIGATION ERROR] {ex.Message}");
                BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogCrash(ex, $"Navigation.{key}", isFatal: false);

                var oldView = CurrentView;
                if (oldView != null)
                {
                    try { await oldView.OnNavigatedFromAsync(); } catch { }
                }

                CurrentView = new ErrorViewModel(
                    error: $"Page Load Error: {key}",
                    stack: $"{ex.Message}\n\n{ex.StackTrace}",
                    retryAction: () => NavigateCommand.Execute(key)
                );
                NavigationChanged?.Invoke("Error");
            }
            finally
            {
                _navLock.Release();
            }
        }

        /// <summary>

        /// Blocks until the backend is provably ready (max 30 s), then

        /// sets the status to Connected/Offline and navigates to Dashboard.

        /// </summary>

        private async Task StartupHandshakeAsync()
        {
            SetConnectionState(IpcConnectionState.Connecting);

            // 15s is enough - if service doesn't respond, show DEGRADED (not Offline)
            // so pages are usable even without service.
            bool ready = await _ipc.WaitForReadyAsync(TimeSpan.FromSeconds(15));

            if (ready)
            {
                ServiceBuildId = _ipc.ServiceBuildId;
                SetConnectionState(IpcConnectionState.Connected);

                if (IsVersionMismatch)
                {
                    await HandleVersionMismatch();
                    return;
                }
            }
            else
            {
                // DEGRADED - pages still navigate, service features show offline state
                ServiceBuildId = "Degraded";
                SetConnectionState(IpcConnectionState.Degraded);
            }
        }

        /// <summary>
        /// Periodic health check - updates status without blocking navigation.
        /// </summary>
        private int _failedHealthChecks = 0;

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
                        _failedHealthChecks = 0;
                        ServiceBuildId = handshake.BuildId;
                        SetConnectionState(IpcConnectionState.Connected);
                        if (IsVersionMismatch)
                            await HandleVersionMismatch();
                    }
                    else
                    {
                        ServiceBuildId = "Protocol Mismatch";
                        SetConnectionState(IpcConnectionState.Offline);
                    }
                }
                catch
                {
                    _failedHealthChecks++;
                    if (_failedHealthChecks >= 3)
                    {
                        ServiceBuildId = "Offline";
                        SetConnectionState(IpcConnectionState.Offline);
                    }
                    else
                    {
                        SetConnectionState(IpcConnectionState.Degraded);
                    }
                }
            }
            else
            {
                _failedHealthChecks++;
                if (_failedHealthChecks >= 3)
                {
                    ServiceBuildId = "Offline";
                    SetConnectionState(IpcConnectionState.Offline);
                }
                else
                {
                    SetConnectionState(IpcConnectionState.Degraded);
                }
            }
        }

        private async Task HandleVersionMismatch()
        {
            ServiceBuildId = "Application components are out of sync. Restarting backend...";

            SetConnectionState(IpcConnectionState.Connecting);

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
                SetConnectionState(IpcConnectionState.Connected);
            }
            else
            {
                ServiceBuildId = "Offline";
                SetConnectionState(IpcConnectionState.Offline);
            }
        }

    }

}

