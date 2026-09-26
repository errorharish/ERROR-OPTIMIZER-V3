using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels;

namespace BiosOptimizer.GUI
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, RadioButton> _navButtons = new();
        private string _currentRoute = "";

        public MainWindow() {
            InitializeComponent();
            var ipcClient = new IpcClient();
            var vm = new MainViewModel(ipcClient);
            vm.NavigationChanged += OnNavigationChanged;
            DataContext = vm;
            Loaded += OnWindowLoaded;
            
            BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 04", "MainWindow constructed");

            Closing += OnWindowClosing;
            StateChanged += OnWindowStateChanged;
            IsVisibleChanged += OnWindowIsVisibleChanged;

            if (!App.IsSafeMode && !App.IsNoEffectsMode)
            {
                PreviewMouseMove += OnWindowMouseMove;
                PreviewMouseDown += OnWindowMouseDown;
                PreviewMouseUp += OnWindowMouseUp;
                MouseLeave += OnWindowMouseLeave;
                MouseEnter += OnWindowMouseEnter;
                Activated += OnWindowActivated;
                Deactivated += OnWindowDeactivated;
            }

            // Phase C: Subscribe to settings changes for live effect
            AppSettingsService.Instance.SettingsChanged += OnSettingsChanged;
            UpdateCursorState();
        }

        private void UpdateCursorState()
        {
            if (App.IsSafeMode || App.IsNoEffectsMode)
            {
                ForceCursor = false;
                Cursor = Cursors.Arrow;
                if (CursorOverlay != null) CursorOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (AppSettingsService.Instance.CursorEffect)
            {
                ForceCursor = true;
                Cursor = Cursors.None;
                if (CursorOverlay != null) CursorOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                ForceCursor = false;
                Cursor = Cursors.Arrow;
                if (CursorOverlay != null) CursorOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void OnSettingsChanged()
        {
            UpdateCursorState();
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            UpdateCursorState();
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            ForceCursor = false;
            Cursor = Cursors.Arrow;
            CursorOverlay?.HandleMouseLeave();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            bool isMinimized = WindowState == WindowState.Minimized;
            BiosOptimizer.Core.Implementations.AdaptiveResourceGovernor.Instance.SetWindowVisibility(!isMinimized);
            if (isMinimized)
            {
                CursorOverlay?.PauseRendering();
                DynamicBackground?.PauseRendering();
            }
            else
            {
                CursorOverlay?.ResumeRendering();
                DynamicBackground?.ResumeRendering();
                UpdateCursorState();
            }
        }

        private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            bool isVisible = IsVisible && WindowState != WindowState.Minimized;
            BiosOptimizer.Core.Implementations.AdaptiveResourceGovernor.Instance.SetWindowVisibility(isVisible);
            if (!isVisible)
            {
                CursorOverlay?.PauseRendering();
                DynamicBackground?.PauseRendering();
            }
            else
            {
                CursorOverlay?.ResumeRendering();
                DynamicBackground?.ResumeRendering();
                UpdateCursorState();
            }
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsExiting)
            {
                e.Cancel = true;
                Hide();
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
                BiosOptimizer.Core.Implementations.AdaptiveResourceGovernor.Instance.SetWindowVisibility(false);
                CursorOverlay?.PauseRendering();
                DynamicBackground?.PauseRendering();
                SystemTrayService.Instance.UpdateStatus("Background Optimization Active", "READY");
                return;
            }

            ForceCursor = false;
            Cursor = Cursors.Arrow;
            Mouse.OverrideCursor = null;
        }

        private void OnWindowMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                var pos = e.GetPosition(this);
                DynamicBackground?.HandleMouseMove(pos);
                GlobalProximityGlowEngine.Instance.ProcessMouseMove(pos, this);
                if (AppSettingsService.Instance.CursorEffect)
                {
                    CursorOverlay?.HandleMouseMove(pos, e.OriginalSource as DependencyObject);
                }
            }
            catch { }
        }

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            CursorOverlay?.HandleMouseDown(pos, e.ChangedButton);
        }

        private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            CursorOverlay?.HandleMouseUp(pos, e.ChangedButton);
        }

        private void OnWindowMouseEnter(object sender, MouseEventArgs e)
        {
            UpdateCursorState();
            var pos = e.GetPosition(this);
            CursorOverlay?.HandleMouseEnter(pos);
        }

        private void OnWindowMouseLeave(object sender, MouseEventArgs e)
        {
            ForceCursor = false;
            Cursor = Cursors.Arrow;
            CursorOverlay?.HandleMouseLeave();
            GlobalProximityGlowEngine.Instance.ResetAll();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupPhaseStart("MAIN WINDOW SHOW");
                BiosOptimizer.Core.Implementations.Startup.StartupRecoveryManager.Instance.RecordStartupSuccess();
                BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupStage("STARTUP 05", "MainWindow shown");
                BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogStartupPhaseSuccess("MAIN WINDOW SHOW");
            }
            catch { }

            // Collect all sidebar nav buttons by their Tag
            CollectNavButtons(this);

            if (App.IsSafeMode || App.IsNoEffectsMode)
            {
                CursorOverlay.Visibility = Visibility.Collapsed;
            }

            // Startup Navigation: Open About page first with 10-second intro period
            if (DataContext is MainViewModel vm)
            {
                vm.NavigateCommand.Execute("About");
                vm.StartStartupIntroSequence();
            }

            if (!App.IsSafeMode)
            {
                if (App.IsStartupBackground)
                {
                    this.WindowState = WindowState.Minimized;
                }
                else
                {
                    // Animate window in
                    var anim = new DoubleAnimation(0, 1, System.TimeSpan.FromMilliseconds(300));
                    BeginAnimation(OpacityProperty, anim);
                }
            }
            else 
            {
                this.Opacity = 1;
            }

            // Hook WM_GETMINMAXINFO so Maximized respects the taskbar work area
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);

            SizeChanged += (s, e) => UpdateRootClip();
            UpdateRootClip();
        }

        private void CollectNavButtons(DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is RadioButton rb && rb.CommandParameter is string route && !string.IsNullOrEmpty(route))
                    _navButtons[route] = rb;
                if (child is DependencyObject depObj)
                    CollectNavButtons(depObj);
            }
        }

        private int _currentTransitionId = 0;

        private void OnNavigationChanged(string route)
        {
            UpdateActiveNavItem(route);
            if (DataContext is MainViewModel vm)
            {
                PageContent?.TransitionTo(vm.CurrentView);
            }
        }

        private void UpdateActiveNavItem(string route)
        {
            _currentRoute = route;
            string mappedRoute = route switch
            {
                "RegistryValuesRam" => "RegistryValues",
                "RegistryValuesGpu" => "RegistryValues",
                "GpuRegistryValues" => "RegistryValues",
                "AiRamLimiter" => "AiOptimization",
                "AiWorkload" => "AiOptimization",
                "AiPowerPlan" => "AiOptimization",
                _ => route
            };

            if (_navButtons.TryGetValue(mappedRoute, out var rb))
            {
                rb.IsChecked = true;
            }
        }

        private void PerformPageTransition(object? newContent)
        {
            if (PageContent == null) return;

            var motion = UI3DMotionEngine.Instance;
            if (!motion.IsTransitionsEnabled)
            {
                PageContent.BeginAnimation(OpacityProperty, null);
                if (PageContent.RenderTransform is TransformGroup tg)
                {
                    tg.Children.OfType<TranslateTransform>().FirstOrDefault()?.BeginAnimation(TranslateTransform.YProperty, null);
                    tg.Children.OfType<ScaleTransform>().FirstOrDefault()?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    tg.Children.OfType<ScaleTransform>().FirstOrDefault()?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
                PageContent.Content = newContent;
                PageContent.Opacity = 1;
                return;
            }

            int transitionId = unchecked(++_currentTransitionId);

            if (PageContent.RenderTransform is not TransformGroup)
            {
                var group = new TransformGroup();
                group.Children.Add(new ScaleTransform(1, 1));
                group.Children.Add(new TranslateTransform(0, 0));
                PageContent.RenderTransform = group;
                PageContent.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var transformGroup = (TransformGroup)PageContent.RenderTransform;
            var scaleTransform = (ScaleTransform)transformGroup.Children[0];
            var translateTransform = (TranslateTransform)transformGroup.Children[1];

            // Abort any ongoing animations immediately
            PageContent.BeginAnimation(OpacityProperty, null);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);

            var outDuration = motion.GetDuration(DurationCategory.PageTransitionOut);
            var inDuration = motion.GetDuration(DurationCategory.PageTransitionIn);
            var ease = motion.GetEasing(EasingCategory.Decelerate);

            // If there is no previous content, directly fade in
            if (PageContent.Content == null)
            {
                PageContent.Content = newContent;
                PageContent.Opacity = 0;
                translateTransform.Y = 8;
                scaleTransform.ScaleX = 0.985;
                scaleTransform.ScaleY = 0.985;

                var fadeInDirect = new DoubleAnimation(0, 1, new Duration(inDuration)) { EasingFunction = ease };
                var slideInDirect = new DoubleAnimation(8, 0, new Duration(inDuration)) { EasingFunction = ease };
                var scaleInDirect = new DoubleAnimation(0.985, 1.0, new Duration(inDuration)) { EasingFunction = ease };

                PageContent.BeginAnimation(OpacityProperty, fadeInDirect);
                translateTransform.BeginAnimation(TranslateTransform.YProperty, slideInDirect);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleInDirect);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleInDirect);
                return;
            }

            // Step 1: Smooth spatial pull-back + fade out old page
            var fadeOut = new DoubleAnimation(PageContent.Opacity, 0, new Duration(outDuration)) { EasingFunction = ease };
            var slideOut = new DoubleAnimation(translateTransform.Y, -8, new Duration(outDuration)) { EasingFunction = ease };
            var scaleOut = new DoubleAnimation(scaleTransform.ScaleX, 0.985, new Duration(outDuration)) { EasingFunction = ease };

            fadeOut.Completed += (s, e) =>
            {
                // Guard against stale transition if user clicked another tab rapidly
                if (transitionId != _currentTransitionId) return;

                // Step 2: Swap content strictly after fade out
                PageContent.Content = newContent;

                // Prepare initial position for new content
                translateTransform.Y = 10;
                scaleTransform.ScaleX = 0.985;
                scaleTransform.ScaleY = 0.985;

                // Step 3: Spatial push forward + fade in new page
                var fadeIn = new DoubleAnimation(0, 1, new Duration(inDuration)) { EasingFunction = ease };
                var slideIn = new DoubleAnimation(10, 0, new Duration(inDuration)) { EasingFunction = ease };
                var scaleIn = new DoubleAnimation(0.985, 1.0, new Duration(inDuration)) { EasingFunction = ease };

                fadeIn.Completed += (s2, e2) =>
                {
                    if (transitionId != _currentTransitionId) return;
                    PageContent.BeginAnimation(OpacityProperty, null);
                    PageContent.Opacity = 1;
                    translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                    translateTransform.Y = 0;
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scaleTransform.ScaleX = 1;
                    scaleTransform.ScaleY = 1;
                };

                PageContent.BeginAnimation(OpacityProperty, fadeIn);
                translateTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            };

            PageContent.BeginAnimation(OpacityProperty, fadeOut);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        }

        // Window chrome events
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();

        protected override void OnStateChanged(System.EventArgs e)
        {
            base.OnStateChanged(e);
            if (MaxRestoreButton != null)
            {
                MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "\xE923" : "\xE922";
            }
            UpdateRootClip();
        }

        private void UpdateRootClip()
        {
            if (RootBorder == null) return;
            if (WindowState == WindowState.Maximized)
            {
                RootBorder.Clip = null;
                RootBorder.CornerRadius = new CornerRadius(0);
                RootBorder.BorderThickness = new Thickness(0);
            }
            else
            {
                RootBorder.CornerRadius = new CornerRadius(14);
                RootBorder.BorderThickness = new Thickness(1.2);
                if (ActualWidth > 0 && ActualHeight > 0)
                {
                    RootBorder.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 14, 14);
                }
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var monitorInfo = new MONITORINFO();
                    GetMonitorInfo(monitor, monitorInfo);
                    var rcWorkArea = monitorInfo.rcWork;
                    var rcMonitorArea = monitorInfo.rcMonitor;
                    
                    mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                    mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                    mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                    mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
                }
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO { public POINT ptReserved; public POINT ptMaxSize; public POINT ptMaxPosition; public POINT ptMinTrackSize; public POINT ptMaxTrackSize; }

        [StructLayout(LayoutKind.Sequential)]
        private class MONITORINFO { public int cbSize = Marshal.SizeOf(typeof(MONITORINFO)); public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left; public int top; public int right; public int bottom; }

        [DllImport("user32")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        private const int MONITOR_DEFAULTTONEAREST = 2;
    }
}

