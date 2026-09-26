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

        private readonly Dictionary<string, Button> _navButtons = new();
        private string _currentRoute = "";

        public MainWindow()
        {
            InitializeComponent();
            var ipcClient = new IpcClient();
            var vm = new MainViewModel(ipcClient);
            vm.NavigationChanged += OnNavigationChanged;
            DataContext = vm;
            Loaded += OnWindowLoaded;
            MouseMove += OnWindowMouseMove;
            MouseLeave += OnWindowMouseLeave;
            MouseEnter += OnWindowMouseEnter;
        }

        private void OnWindowMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            // Translate the ellipse so its center matches the mouse position
            // The Ellipse is 300x300, so we offset by -150
            CursorTranslate.X = pos.X - 150;
            CursorTranslate.Y = pos.Y - 150;
        }

        private void OnWindowMouseEnter(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(0.12, System.TimeSpan.FromMilliseconds(300));
            CursorGlow.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void OnWindowMouseLeave(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(0, System.TimeSpan.FromMilliseconds(300));
            CursorGlow.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Collect all sidebar nav buttons by their Tag
            CollectNavButtons(this);

            // Navigate to first page by default
            if (DataContext is MainViewModel vm)
                vm.NavigateCommand.Execute("Normal");

            // Animate window in
            var anim = new DoubleAnimation(0, 1, System.TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, anim);

            // Hook WM_GETMINMAXINFO so Maximized respects the taskbar work area
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private void CollectNavButtons(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button btn && btn.Tag is string tag && !string.IsNullOrEmpty(tag))
                    _navButtons[tag] = btn;
                CollectNavButtons(child);
            }
        }

        private void OnNavigationChanged(string route)
        {
            UpdateActiveNavItem(route);
            AnimatePageTransition();
        }

        private void UpdateActiveNavItem(string route)
        {
            _currentRoute = route;
            foreach (var (tag, btn) in _navButtons)
            {
                bool isActive = tag == route;
                // Update the SidebarNavItem's IsActive state
                var navItem = FindSidebarNavItem(btn);
                if (navItem != null) navItem.IsActive = isActive;

                // Active background styling via Theme.xaml
                if (isActive)
                {
                    btn.Style = FindResource("SidebarButtonActiveStyle") as Style;
                }
                else
                {
                    btn.Style = FindResource("SidebarButtonStyle") as Style;
                }
            }
        }

        private SidebarNavItem FindSidebarNavItem(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is SidebarNavItem item) return item;
                var found = FindSidebarNavItem(child);
                if (found != null) return found;
            }
            return null;
        }

        private void AnimatePageTransition()
        {
            if (PageContent == null) return;
            var fadeOut = new DoubleAnimation(0, System.TimeSpan.FromMilliseconds(80));
            var fadeIn = new DoubleAnimation(1, System.TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => PageContent.BeginAnimation(OpacityProperty, fadeIn);
            PageContent.BeginAnimation(OpacityProperty, fadeOut);
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
            if (MaxRestoreButton != null)
                MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var anim = new DoubleAnimation(1, 0, System.TimeSpan.FromMilliseconds(150));
            anim.Completed += (s, args) => Close();
            BeginAnimation(OpacityProperty, anim);
        }

        protected override void OnStateChanged(System.EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Maximized)
            {
                // Apply WindowChrome resize border padding to keep content within the window chrome boundary
                var border = SystemParameters.WindowResizeBorderThickness;
                RootBorder.Padding = new Thickness(border.Left, border.Top, border.Right, border.Bottom);
                RootBorder.BorderThickness = new Thickness(0);
                if (MaxRestoreButton != null) MaxRestoreButton.Content = "\uE923";
            }
            else
            {
                RootBorder.Padding = new Thickness(0);
                RootBorder.BorderThickness = new Thickness(1);
                if (MaxRestoreButton != null) MaxRestoreButton.Content = "\uE922";
            }
        }

        // ─── WM_GETMINMAXINFO — Constrains maximized window to the monitor's work area ───
        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            // Find the monitor this window is on
            const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(monitor, ref mi);

                // Work area = monitor area minus taskbar
                var work = mi.rcWork;
                var mon = mi.rcMonitor;
                
                mmi.ptMaxPosition.x = System.Math.Abs(work.left - mon.left);
                mmi.ptMaxPosition.y = System.Math.Abs(work.top - mon.top);
                mmi.ptMaxSize.x = System.Math.Abs(work.right - work.left);
                mmi.ptMaxSize.y = System.Math.Abs(work.bottom - work.top);
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }
}
