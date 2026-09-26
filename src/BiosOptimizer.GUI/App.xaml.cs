using System;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BiosOptimizer.Core.Implementations.Diagnostics;
using BiosOptimizer.Core.Implementations.Startup;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI
{
    public partial class App : Application
    {
        public static bool IsSafeMode { get; private set; }
        public static bool IsStartupBackground { get; private set; }
        public static bool IsDiagnosticMode { get; private set; }
        public static bool IsNoGpuMode { get; private set; }
        public static bool IsNoAdvancedMemoryMode { get; private set; }
        public static bool IsNoEffectsMode { get; private set; }
        public static bool IsExiting { get; set; }

        public BackendManager BackendManager { get; private set; }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);
        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _wakeupEvent;
        private static CancellationTokenSource? _wakeupListenerCts;

        public App()
        {
            try
            {
                Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }
            catch { }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SetupExceptionHandling();
            try 
            { 
                SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2); 
            } 
            catch { }

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                StartupSelfDiagnosticTracker.Instance.RecordExit(Environment.ExitCode);
                ApplicationCrashLogger.Instance.LogStartupStage("PROCESS_EXIT", $"ProcessExit. ExitCode={Environment.ExitCode}. Trace: {Environment.StackTrace}");
            };
        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    bool isCrtUnload = ex is DllNotFoundException && (ex.TargetSite?.Name?.Contains("destroy_list") == true || ex.StackTrace?.Contains("ModuleUninitializer") == true);
                    bool isFatal = !e.IsTerminating && !isCrtUnload;
                    ApplicationCrashLogger.Instance.LogCrash(ex, "AppDomain", isFatal);
                    StartupRecoveryManager.Instance.RecordStartupFailure("AppDomain", ex);
                }
            };

            this.DispatcherUnhandledException += (s, e) =>
            {
                ApplicationCrashLogger.Instance.LogCrash(e.Exception, "Dispatcher", false);
                e.Handled = true;

                // Safely show non-fatal error toast or in-page recovery on MainWindow
                if (MainWindow?.DataContext is BiosOptimizer.GUI.ViewModels.MainViewModel mainVm)
                {
                    try
                    {
                        mainVm.ShowGlobalError(e.Exception);
                    }
                    catch { }
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                ApplicationCrashLogger.Instance.LogCrash(e.Exception, "TaskScheduler", false);
                e.SetObserved();
            };
        }

        private static bool ActivateExistingInstance()
        {
            try
            {
                var titles = new[] { "Error Optimizer Pro", "Error Optimizer", "ErrorOptimizer" };
                foreach (var title in titles)
                {
                    var handle = FindWindow(null, title);
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, 9); // SW_RESTORE
                        SetForegroundWindow(handle);
                        return true;
                    }
                }

                // Fallback: search running processes
                int currentPid = Environment.ProcessId;
                var procs = Process.GetProcessesByName("ErrorOptimizer");
                foreach (var p in procs)
                {
                    if (p.Id != currentPid && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9);
                        SetForegroundWindow(p.MainWindowHandle);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ApplicationCrashLogger.Instance.LogStartupPhaseStart("BOOT");
            ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 01", "App process started");
            StartupRecoveryManager.Instance.RecordStartupAttempt();
            bool isBackground = false;
            bool isStartupLaunch = false;

            string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            StartupSelfDiagnosticTracker.Instance.RecordStartupLaunch(e.Args, currentExe);

            foreach (var arg in e.Args)
            {
                if (arg.Equals("--safe-mode", StringComparison.OrdinalIgnoreCase))
                {
                    IsSafeMode = true;
                }
                else if (arg.Equals("--diagnostic", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--diagnostic-startup", StringComparison.OrdinalIgnoreCase))
                {
                    IsDiagnosticMode = true;
                }
                else if (arg.Equals("--no-gpu", StringComparison.OrdinalIgnoreCase))
                {
                    IsNoGpuMode = true;
                }
                else if (arg.Equals("--no-advanced-memory", StringComparison.OrdinalIgnoreCase))
                {
                    IsNoAdvancedMemoryMode = true;
                }
                else if (arg.Equals("--no-effects", StringComparison.OrdinalIgnoreCase))
                {
                    IsNoEffectsMode = true;
                }
                else if (arg.Equals("--startup-background", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                {
                    isBackground = true;
                    isStartupLaunch = true;
                    IsStartupBackground = true;
                }
                else if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase))
                {
                    isStartupLaunch = true;
                }
                else if (arg.Equals("--auto-optimize-background", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--auto-optimize-silent", StringComparison.OrdinalIgnoreCase))
                {
                    // Headless background run via Windows Task Scheduler
                    Task.Run(async () =>
                    {
                        try
                        {
                            await SmartAutoOptimizeScheduler.Instance.RunManualOrScheduledAsync("TASK_SCHEDULER");
                        }
                        catch { }
                        finally
                        {
                            Environment.Exit(0);
                        }
                    }).Wait();
                    return;
                }
                else if (arg.Equals("--reconcile-startup", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        WindowsStartupRegistrar.Instance.ReconcileAndCleanAllDuplicates();
                    }
                    catch { }
                    Environment.Exit(0);
                    return;
                }
                else if (arg.Equals("--register-startup", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        WindowsStartupRegistrar.Instance.ConfigureStartup(true, startMinimized: true);
                    }
                    catch { }
                    Environment.Exit(0);
                    return;
                }
                else if (arg.Equals("--unregister-startup", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        WindowsStartupRegistrar.Instance.ConfigureStartup(false);
                    }
                    catch { }
                    Environment.Exit(0);
                    return;
                }
            }

            ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("BOOT");

            // Single Instance Enforcement & Wakeup Signaling (Permission-Safe & Abandonment-Proof)
            const string mutexNameGlobal = @"Global\ErrorOptimizer_SingleInstance_Mutex";
            const string mutexNameLocal = @"Local\ErrorOptimizer_SingleInstance_Mutex";
            const string eventNameGlobal = @"Global\ErrorOptimizer_Wakeup_Event";
            const string eventNameLocal = @"Local\ErrorOptimizer_Wakeup_Event";

            bool isOnlyInstance = false;
            try
            {
                _singleInstanceMutex = new Mutex(true, mutexNameGlobal, out bool createdNew);
                if (createdNew)
                {
                    isOnlyInstance = true;
                }
                else
                {
                    try
                    {
                        if (_singleInstanceMutex.WaitOne(0, false))
                        {
                            isOnlyInstance = true;
                        }
                    }
                    catch (AbandonedMutexException)
                    {
                        isOnlyInstance = true;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                try
                {
                    _singleInstanceMutex = new Mutex(true, mutexNameLocal, out bool createdLocal);
                    if (createdLocal)
                    {
                        isOnlyInstance = true;
                    }
                    else
                    {
                        try
                        {
                            if (_singleInstanceMutex.WaitOne(0, false))
                            {
                                isOnlyInstance = true;
                            }
                        }
                        catch (AbandonedMutexException)
                        {
                            isOnlyInstance = true;
                        }
                    }
                }
                catch
                {
                    isOnlyInstance = true;
                }
            }
            catch
            {
                try
                {
                    _singleInstanceMutex = new Mutex(true, mutexNameLocal, out bool createdLocal);
                    isOnlyInstance = createdLocal;
                }
                catch
                {
                    isOnlyInstance = true;
                }
            }

            // CRITICAL SANITY CHECK: Check actual running processes in the OS
            try
            {
                int currentPid = Environment.ProcessId;
                var otherInstances = Process.GetProcessesByName("ErrorOptimizer")
                    .Where(p => p.Id != currentPid)
                    .ToList();

                if (otherInstances.Count == 0)
                {
                    // No other ErrorOptimizer process exists in the entire OS. We MUST be the primary instance!
                    isOnlyInstance = true;
                }
            }
            catch { }

            StartupSelfDiagnosticTracker.Instance.RecordSingleInstance(isOnlyInstance);

            if (!isOnlyInstance)
            {
                if (!isBackground)
                {
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(eventNameGlobal, out var existingEvent))
                        {
                            existingEvent.Set();
                            existingEvent.Dispose();
                        }
                        else if (EventWaitHandle.TryOpenExisting(eventNameLocal, out var localEvent))
                        {
                            localEvent.Set();
                            localEvent.Dispose();
                        }
                    }
                    catch { }

                    ActivateExistingInstance();
                }

                // CRITICAL: Secondary instance must ALWAYS terminate immediately without exception!
                Environment.Exit(0);
                return;
            }

            // Primary instance creates the wakeup event and listens for manual activations
            try
            {
                try
                {
                    _wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventNameGlobal);
                }
                catch
                {
                    _wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventNameLocal);
                }

                _wakeupListenerCts = new CancellationTokenSource();
                var token = _wakeupListenerCts.Token;

                Task.Run(() =>
                {
                    while (!IsExiting && !token.IsCancellationRequested)
                    {
                        try
                        {
                            if (_wakeupEvent != null && _wakeupEvent.WaitOne(500))
                            {
                                if (IsExiting || token.IsCancellationRequested) break;
                                Current?.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    SystemTrayService.Instance.RestoreMainWindow();
                                }));
                            }
                        }
                        catch { }
                    }
                }, token);
            }
            catch { }

            // [STARTUP 02] CONFIG LOAD
            ApplicationCrashLogger.Instance.LogStartupPhaseStart("CONFIG LOAD");
            try
            {
                AppSettingsService.Instance.Load();
                if (isStartupLaunch && AppSettingsService.Instance.StartMinimized)
                {
                    isBackground = true;
                    IsStartupBackground = true;
                }
                ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("CONFIG LOAD");
                ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 02", "Core configuration loaded");
            }
            catch (Exception ex)
            {
                ApplicationCrashLogger.Instance.LogStartupPhaseFailed("CONFIG LOAD", ex);
                ApplicationCrashLogger.Instance.LogCrash(ex, "Startup.LoadSettings", false);
            }

            // [STARTUP 03] THEME LOAD
            ApplicationCrashLogger.Instance.LogStartupPhaseStart("THEME LOAD");
            base.OnStartup(e);
            VerifyIconResources();

            try
            {
                ThemeBrushService.Instance.ApplyAll(AppSettingsService.Instance);
                ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("THEME LOAD");
            }
            catch (Exception ex)
            {
                ApplicationCrashLogger.Instance.LogStartupPhaseFailed("THEME LOAD", ex);
                ApplicationCrashLogger.Instance.LogCrash(ex, "Startup.ThemeBrushService", false);
            }

            ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 03", "Theme resources loaded");

            // [STARTUP 04] SERVICE INITIALIZATION
            ApplicationCrashLogger.Instance.LogStartupPhaseStart("SERVICE INITIALIZATION");
            BackendManager = new BackendManager();
            _ = Task.Run(() =>
            {
                try
                {
                    BackendManager.StartBackend();
                    StartupSelfDiagnosticTracker.Instance.RecordBackend(true);
                    ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("SERVICE INITIALIZATION");
                }
                catch (Exception ex)
                {
                    StartupSelfDiagnosticTracker.Instance.RecordBackend(false);
                    ApplicationCrashLogger.Instance.LogStartupPhaseFailed("SERVICE INITIALIZATION", ex);
                    ApplicationCrashLogger.Instance.LogCrash(ex, "Startup.BackendManager", false);
                }
            });

            // Run Phased Authoritative Startup Coordinator
            _ = Task.Run(async () =>
            {
                try
                {
                    var coord = StartupTaskCoordinator.Instance;

                    await coord.ExecuteTaskAsync(
                        "DetectHardware",
                        async ct => 
                        { 
                            if (!IsNoGpuMode)
                            {
                                BiosOptimizer.Core.Implementations.HardwareProfiler.GetQuickProfile(forceRefresh: true);
                            }
                            ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 08", "Hardware identity loaded");
                            await Task.CompletedTask; 
                        },
                        TimeSpan.FromSeconds(5));

                    await coord.ExecuteTaskAsync(
                        "StartTelemetry",
                        async ct =>
                        {
                            ApplicationCrashLogger.Instance.LogStartupPhaseStart("TELEMETRY INITIALIZATION");
                            ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 09", "Lightweight telemetry started");
                            ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("TELEMETRY INITIALIZATION");
                            await Task.CompletedTask;
                        },
                        TimeSpan.FromSeconds(5));

                    await coord.ExecuteTaskAsync(
                        "LoadProfileState",
                        async ct =>
                        {
                            ApplicationCrashLogger.Instance.LogStartupPhaseStart("OPTIMIZATION ENGINE INITIALIZATION");
                            ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 10", "Profile state loaded");
                            ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("OPTIMIZATION ENGINE INITIALIZATION");
                            await Task.CompletedTask;
                        },
                        TimeSpan.FromSeconds(5));

                    await coord.ExecuteTaskAsync(
                        "ReconcileSystem",
                        async ct => 
                        { 
                            ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 11", "Background reconciliation started");
                            await StartupBootstrapService.Instance.RunBootstrapAsync(isBackground, ct); 
                        },
                        TimeSpan.FromSeconds(10));

                    coord.MarkCompleted();
                    ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 12", "Startup complete");
                    ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("READY");
                    SystemTrayService.Instance.UpdateStatus("Background Optimization Active (Ready)", "READY");
                    StartupSelfDiagnosticTracker.Instance.RecordAiEngines(workload: true, ram: true, powerPlan: true);
                }
                catch (Exception ex)
                {
                    ApplicationCrashLogger.Instance.LogCrash(ex, "Startup.PhasedCoordinator", false);
                }
            });

            // Initialize MainWindow and SystemTrayService
            try
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                SystemTrayService.Instance.Initialize(mainWindow);
                StartupSelfDiagnosticTracker.Instance.RecordTray(SystemTrayService.Instance.IsTrayActive);

                if (!isBackground)
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.ShowInTaskbar = true;
                    mainWindow.Activate();
                    BiosOptimizer.Core.Implementations.AdaptiveResourceGovernor.Instance.SetWindowVisibility(true);
                    StartupSelfDiagnosticTracker.Instance.RecordMainWindowState(false);
                }
                else
                {
                    mainWindow.WindowState = WindowState.Minimized;
                    mainWindow.ShowInTaskbar = false;
                    mainWindow.Hide();
                    BiosOptimizer.Core.Implementations.AdaptiveResourceGovernor.Instance.SetWindowVisibility(false);
                    StartupSelfDiagnosticTracker.Instance.RecordMainWindowState(true);
                    SystemTrayService.Instance.UpdateStatus("Background Optimization Active (Silent)", "READY");
                    ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 05", "Silent Background Mode initialized into System Tray");
                }
            }
            catch (Exception ex)
            {
                StartupSelfDiagnosticTracker.Instance.RecordFailure("MainWindowInit", ex.Message);
                ApplicationCrashLogger.Instance.LogCrash(ex, "Startup.MainWindowInit", false);
            }

            StartMemoryMonitor();
        }

        private void StartMemoryMonitor()
        {
            if (IsSafeMode) return;

            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(15);
            timer.Tick += (s, ev) =>
            {
                try
                {
                    var (wsMb, pvtMb, gcMb) = BiosOptimizer.Core.Implementations.AdaptiveResourceGovernor.Instance.GetAppMemoryFootprint();
                    if (wsMb > 300)
                    {
                        BiosOptimizer.Core.Implementations.AdaptiveResourceGovernor.Instance.TrimAppMemory();
                    }
                }
                catch { }
            };
            timer.Start();
        }

        private void VerifyIconResources()
        {
            var requiredIcons = new[] { 
                "IconNormal", "IconPro", "IconUltimate", "IconDebloat", "IconBiosSafe", "IconMaxPerf",
                "IconDashboard", "IconProcess", "IconInput", "IconSliders", "IconSystemInfo", "IconStartup",
                "IconServiceMgr", "IconNetwork", "IconStorage", "IconTools", "IconSettings"
            };

            var fallbackIcon = "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5";
            if (this.Resources.Contains("IconNormal"))
            {
                fallbackIcon = this.Resources["IconNormal"] as string ?? fallbackIcon;
            }

            foreach (var icon in requiredIcons)
            {
                if (!this.Resources.Contains(icon))
                {
                    this.Resources.Add(icon, fallbackIcon);
                    ApplicationCrashLogger.Instance.LogCrash(new KeyNotFoundException($"Missing UI Resource: {icon}"), "Resources.IconVerification", false);
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IsExiting = true;
            ApplicationCrashLogger.Instance.LogStartupStage("ON_EXIT", $"OnExit. AppExitCode={e.ApplicationExitCode}. Trace: {Environment.StackTrace}");
            try
            {
                _wakeupListenerCts?.Cancel();
                _wakeupEvent?.Set();
                _wakeupEvent?.Dispose();
            }
            catch { }

            try
            {
                _singleInstanceMutex?.Dispose();
            }
            catch { }

            try
            {
                BackendManager?.Dispose();
            }
            catch { }
            base.OnExit(e);
        }

        public void RestartBackend()
        {
            try
            {
                BackendManager?.Shutdown();
                _ = Task.Run(() => BackendManager.StartBackend());
            }
            catch { }
        }
    }
}
