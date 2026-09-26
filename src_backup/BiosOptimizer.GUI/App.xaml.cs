using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI
{
    public partial class App : Application
    {
        public BackendManager BackendManager { get; private set; }
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);
        private static int _errorCount = 0;

        public App()
        {
            SetupExceptionHandling();
            try { SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2); } catch { }
        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                File.AppendAllText(@"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\crash.txt", "AppDomain: " + e.ExceptionObject?.ToString() + "\n");
                LogCrash(e.ExceptionObject as Exception, "AppDomain", true);
            };
            this.DispatcherUnhandledException += (s, e) => { 
                File.AppendAllText(@"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\crash.txt", "Dispatcher: " + e.Exception.ToString() + "\n");
                LogCrash(e.Exception, "Dispatcher", false); 
                e.Handled = true; 
            };
            TaskScheduler.UnobservedTaskException += (s, e) => { 
                File.AppendAllText(@"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\crash.txt", "TaskScheduler: " + e.Exception.ToString() + "\n");
                LogCrash(e.Exception, "TaskScheduler", false); 
                e.SetObserved(); 
            };
        }

        private void LogCrash(Exception ex, string source, bool isFatal)
        {
            if (ex == null) return;
            
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiosOptimizer", "logs");
                Directory.CreateDirectory(logDir);
                
                var logFile = isFatal ? "startup-crash.log" : "runtime-crash.log";
                var fullPath = Path.Combine(logDir, logFile);
                
                var msg = $"[{DateTime.Now:O}] CRASH SOURCE: {source}\r\n" +
                          $"Exception: {ex.GetType().Name}\r\n" +
                          $"Message: {ex.Message}\r\n" +
                          $"TargetSite: {ex.TargetSite}\r\n" +
                          $"Stack Trace:\r\n{ex.StackTrace}\r\n";
                          
                if (ex.InnerException != null)
                {
                    msg += $"\r\nInner Exception: {ex.InnerException.Message}\r\n{ex.InnerException.StackTrace}\r\n";
                }
                
                File.AppendAllText(fullPath, msg + "\r\n----------------------\r\n");
                
                if (Interlocked.Increment(ref _errorCount) == 1 && isFatal)
                {
                    MessageBox.Show($"ERROR OPTIMIZER EXPERIENCED A FATAL ERROR\n\nReason:\n{ex.Message}\n\nLog:\n{fullPath}", 
                                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(1);
                }
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            VerifyIconResources();
            
            BackendManager = new BackendManager();
            _ = Task.Run(() => BackendManager.StartBackend());
        }

        private void VerifyIconResources()
        {
            var requiredIcons = new[] { 
                "IconNormal", "IconPro", "IconUltimate", "IconDebloat", "IconBiosSafe", "IconMaxPerf",
                "IconDashboard", "IconProcess", "IconInput", "IconSliders", "IconSystemInfo", "IconStartup",
                "IconServiceMgr", "IconNetwork", "IconStorage", "IconTools", "IconSettings"
            };

            var fallbackIcon = "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"; // IconNormal
            if (this.Resources.Contains("IconNormal"))
            {
                fallbackIcon = this.Resources["IconNormal"] as string ?? fallbackIcon;
            }

            foreach (var icon in requiredIcons)
            {
                if (!this.Resources.Contains(icon))
                {
                    this.Resources.Add(icon, fallbackIcon);
                    var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiosOptimizer", "logs");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(Path.Combine(logDir, "runtime-crash.log"), $"[{DateTime.Now:O}] WARNING: Missing resource '{icon}'. Fallback applied.\r\n");
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            BackendManager?.Dispose();
            base.OnExit(e);
        }

        public void RestartBackend()
        {
            BackendManager?.Shutdown();
            _ = Task.Run(() => BackendManager.StartBackend());
        }
    }
}
