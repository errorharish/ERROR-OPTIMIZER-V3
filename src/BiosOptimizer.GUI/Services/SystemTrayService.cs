#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.Diagnostics;
using Application = System.Windows.Application;

namespace BiosOptimizer.GUI.Services
{
    public sealed class SystemTrayService : IDisposable
    {
        private static readonly Lazy<SystemTrayService> _instance = new(() => new SystemTrayService());
        public static SystemTrayService Instance => _instance.Value;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        private NotifyIcon? _notifyIcon;
        private Window? _mainWindow;
        private Icon? _appIcon;
        private ToolStripMenuItem? _statusMenuItem;
        private readonly object _lock = new();
        private bool _isInitialized = false;

        public bool IsTrayActive => _notifyIcon != null && _notifyIcon.Visible;

        public void Initialize(Window mainWindow)
        {
            lock (_lock)
            {
                if (_isInitialized) return;
                _mainWindow = mainWindow;

                try
                {
                    LoadApplicationIcon();

                    var contextMenu = new ContextMenuStrip();
                    contextMenu.ShowImageMargin = false;
                    contextMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());

                    var openItem = new ToolStripMenuItem("Open Error Optimizer", null, (s, e) => RestoreMainWindow())
                    {
                        Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
                    };
                    contextMenu.Items.Add(openItem);

                    contextMenu.Items.Add(new ToolStripSeparator());

                    _statusMenuItem = new ToolStripMenuItem("Status: Optimization Active (Ready)")
                    {
                        Enabled = false
                    };
                    contextMenu.Items.Add(_statusMenuItem);

                    var optimizeItem = new ToolStripMenuItem("Run Smart Auto-Optimize", null, (s, e) =>
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await SmartAutoOptimizeScheduler.Instance.RunManualOrScheduledAsync("TRAY_MANUAL");
                                ShowNotification("Auto Optimization Completed", "System parameters evaluated and optimized in background.");
                            }
                            catch { }
                        });
                    });
                    contextMenu.Items.Add(optimizeItem);

                    var settingsItem = new ToolStripMenuItem("Settings", null, (s, e) =>
                    {
                        RestoreMainWindow();
                        if (_mainWindow?.DataContext is BiosOptimizer.GUI.ViewModels.MainViewModel mainVm)
                        {
                            mainVm.NavigateCommand.Execute("Settings");
                        }
                    });
                    contextMenu.Items.Add(settingsItem);

                    contextMenu.Items.Add(new ToolStripSeparator());

                    var exitItem = new ToolStripMenuItem("Exit Error Optimizer", null, (s, e) => ExitApplication());
                    contextMenu.Items.Add(exitItem);

                    _notifyIcon = new NotifyIcon
                    {
                        Icon = _appIcon ?? SystemIcons.Application,
                        Text = "Error Optimizer V3 — Background Active",
                        ContextMenuStrip = contextMenu,
                        Visible = true
                    };

                    _notifyIcon.DoubleClick += (s, e) => RestoreMainWindow();
                    _notifyIcon.MouseClick += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Left)
                        {
                            RestoreMainWindow();
                        }
                    };

                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var helper = new WindowInteropHelper(_mainWindow);
                            IntPtr handle = helper.EnsureHandle();
                            if (handle != IntPtr.Zero)
                            {
                                var source = HwndSource.FromHwnd(handle);
                                source?.AddHook(WndProc);
                            }
                        }
                        catch { }
                    });

                    _isInitialized = true;
                    ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 07", "System Tray NotifyIcon initialized and visible");
                }
                catch (Exception ex)
                {
                    ApplicationCrashLogger.Instance.LogCrash(ex, "SystemTrayService.Initialize", false);
                }
            }
        }

        private void LoadApplicationIcon()
        {
            // 1. Try loading from running executable
            try
            {
                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    _appIcon = Icon.ExtractAssociatedIcon(exePath);
                    if (_appIcon != null) return;
                }
            }
            catch { }

            // 2. Try loading from packaged application directory assets
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string assetPath = Path.Combine(baseDir, "assets", "Error Optimizer V3.ico");
                if (File.Exists(assetPath))
                {
                    _appIcon = new Icon(assetPath);
                    if (_appIcon != null) return;
                }
            }
            catch { }

            // 3. Fallback: WPF Resource Stream
            try
            {
                var iconUri = new Uri("pack://application:,,,/Assets/app_logo.ico", UriKind.Absolute);
                var streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo?.Stream != null)
                {
                    using (streamInfo.Stream)
                    {
                        _appIcon = new Icon(streamInfo.Stream);
                        if (_appIcon != null) return;
                    }
                }
            }
            catch { }

            // Default safe fallback
            _appIcon = SystemIcons.Application;
        }

        public void UpdateStatus(string statusText, string state = "READY")
        {
            lock (_lock)
            {
                if (_notifyIcon == null) return;

                try
                {
                    string tip = $"Error Optimizer V3 — {statusText}";
                    if (tip.Length > 63) tip = tip.Substring(0, 63);
                    _notifyIcon.Text = tip;

                    if (_statusMenuItem != null)
                    {
                        _statusMenuItem.Text = $"Status: {statusText}";
                    }
                }
                catch { }
            }
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            lock (_lock)
            {
                if (_notifyIcon == null) return;
                try
                {
                    _notifyIcon.ShowBalloonTip(3000, title, message, icon);
                }
                catch { }
            }
        }

        public void RestoreMainWindow()
        {
            if (_mainWindow == null) return;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (!_mainWindow.IsVisible)
                {
                    _mainWindow.Show();
                }

                if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
                {
                    _mainWindow.WindowState = System.Windows.WindowState.Normal;
                }

                _mainWindow.ShowInTaskbar = true;
                _mainWindow.Activate();
                _mainWindow.Focus();

                var helper = new WindowInteropHelper(_mainWindow);
                IntPtr hWnd = helper.EnsureHandle();
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, 9); // SW_RESTORE
                    SetForegroundWindow(hWnd);
                }

                AdaptiveResourceGovernor.Instance.SetWindowVisibility(true);
            });
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TASKBARCREATED && WM_TASKBARCREATED != 0)
            {
                lock (_lock)
                {
                    if (_notifyIcon != null && _isInitialized)
                    {
                        try
                        {
                            _notifyIcon.Visible = false;
                            _notifyIcon.Visible = true;
                        }
                        catch { }
                    }
                }
            }
            return IntPtr.Zero;
        }

        public void HideToTray()
        {
            if (_mainWindow == null) return;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.Hide();
                _mainWindow.WindowState = System.Windows.WindowState.Minimized;
                AdaptiveResourceGovernor.Instance.SetWindowVisibility(false);
            });
        }

        public void ExitApplication()
        {
            App.IsExiting = true;
            Dispose();

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current is App app)
                    {
                        try
                        {
                            app.BackendManager?.Dispose();
                        }
                        catch { }
                    }

                    Application.Current.Shutdown();
                });
            }
            else
            {
                Environment.Exit(0);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_notifyIcon != null)
                {
                    try
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                    }
                    catch { }
                    _notifyIcon = null;
                }

                if (_appIcon != null)
                {
                    try { _appIcon.Dispose(); } catch { }
                    _appIcon = null;
                }

                _isInitialized = false;
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(45, 45, 55);
            public override Color MenuItemBorder => Color.FromArgb(70, 70, 85);
            public override Color MenuBorder => Color.FromArgb(50, 50, 60);
            public override Color ToolStripDropDownBackground => Color.FromArgb(24, 24, 30);
            public override Color ImageMarginGradientBegin => Color.FromArgb(24, 24, 30);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(24, 24, 30);
            public override Color ImageMarginGradientEnd => Color.FromArgb(24, 24, 30);
            public override Color SeparatorDark => Color.FromArgb(50, 50, 60);
            public override Color SeparatorLight => Color.FromArgb(30, 30, 38);
        }
    }
}
