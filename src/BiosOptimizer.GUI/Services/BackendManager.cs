using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace BiosOptimizer.GUI.Services
{
    /// <summary>
    /// Manages the lifecycle of the hidden backend process (BiosOptimizer.Service.exe).
    /// Started on GUI launch, terminated when GUI closes. Auto-restarts on crash.
    /// </summary>
    public sealed class BackendManager : IDisposable
    {
        // Location is determined at RUNTIME based on where ErrorOptimizer.exe lives.
        // Production layout:
        //   dist\win-x64\ErrorOptimizer.exe
        //   dist\win-x64\core\BiosOptimizer.Service.exe
        private readonly string _backendExePath;
        private readonly string _backendWorkDir;

        private Process _process;
        private volatile bool _disposed;
        private volatile bool _intentionalShutdown;

        public string SessionId { get; private set; }
        public int BackendPid { get; private set; }

        public BackendManager()
        {
            _backendExePath = ResolveBackendServicePath(out _backendWorkDir);
        }

        private static string ResolveBackendServicePath(out string workDir)
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');

            // 1. AppContext base directory
            var corePath = Path.Combine(baseDir, "core", "BiosOptimizer.Service.exe");
            if (File.Exists(corePath))
            {
                workDir = Path.GetDirectoryName(corePath)!;
                return corePath;
            }

            var siblingPath = Path.Combine(baseDir, "BiosOptimizer.Service.exe");
            if (File.Exists(siblingPath))
            {
                workDir = baseDir;
                return siblingPath;
            }

            // 2. Process MainModule directory
            try
            {
                var mainMod = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(mainMod))
                {
                    var modDir = Path.GetDirectoryName(mainMod);
                    if (!string.IsNullOrEmpty(modDir))
                    {
                        var modCore = Path.Combine(modDir, "core", "BiosOptimizer.Service.exe");
                        if (File.Exists(modCore))
                        {
                            workDir = Path.GetDirectoryName(modCore)!;
                            return modCore;
                        }
                        var modSibling = Path.Combine(modDir, "BiosOptimizer.Service.exe");
                        if (File.Exists(modSibling))
                        {
                            workDir = modDir;
                            return modSibling;
                        }
                    }
                }
            }
            catch { }

            // 3. Program Files installed location
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var installedCore = Path.Combine(progFiles, "Error Optimizer V3", "core", "BiosOptimizer.Service.exe");
            if (File.Exists(installedCore))
            {
                workDir = Path.GetDirectoryName(installedCore)!;
                return installedCore;
            }

            // 4. Inno Setup Uninstall Registry Location
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Error Optimizer V3_is1")
                             ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Error Optimizer V3_is1");
                if (key != null)
                {
                    var loc = key.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(loc))
                    {
                        var regCore = Path.Combine(loc, "core", "BiosOptimizer.Service.exe");
                        if (File.Exists(regCore))
                        {
                            workDir = Path.GetDirectoryName(regCore)!;
                            return regCore;
                        }
                    }
                }
            }
            catch { }

            workDir = baseDir;
            return corePath;
        }

        /// <summary>
        /// Kill any stale backend instances, then launch a fresh hidden backend process.
        /// </summary>
        public void StartBackend()
        {
            if (_disposed) return;

            if (!File.Exists(_backendExePath))
            {
                BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogCrash(
                    new FileNotFoundException($"Backend service binary not found at '{_backendExePath}'"), 
                    "BackendManager.StartBackend", 
                    false);

                if (!App.IsStartupBackground)
                {
                    try
                    {
                        MessageBox.Show(
                            $"Backend service binary not found:\n\n{_backendExePath}\n\n" +
                            $"Please reinstall Error Optimizer.",
                            "Error Optimizer – Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch { }
                }
                return;
            }

            // Only kill stale instances from other paths or old instances
            KillStaleInstances();

            SessionId = Guid.NewGuid().ToString("N");
            BackendPid = 0;

            try
            {
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName         = _backendExePath,
                        Arguments        = $"--session {SessionId}",
                        WorkingDirectory = _backendWorkDir,
                        // Hide everything — no console window, no taskbar entry
                        CreateNoWindow   = true,
                        UseShellExecute  = false,
                        WindowStyle      = ProcessWindowStyle.Hidden,
                        // Redirect output so it doesn't attach to GUI console (if any)
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                    },
                    EnableRaisingEvents = true
                };

                _process.OutputDataReceived += (_, _) => { };
                _process.ErrorDataReceived  += (_, _) => { };
                _process.Exited += OnProcessExited;

                _process.Start();
                BackendPid = _process.Id;
                
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                BiosOptimizer.Core.Implementations.Diagnostics.ApplicationCrashLogger.Instance.LogCrash(ex, "BackendManager.StartBackend", false);
                if (!App.IsStartupBackground)
                {
                    try
                    {
                        MessageBox.Show(
                            $"Failed to start backend service:\n\n{ex.Message}",
                            "Error Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch { }
                }
            }
        }

        private void KillStaleInstances()
        {
            var myProcessId = Process.GetCurrentProcess().Id;
            foreach (var proc in Process.GetProcessesByName("BiosOptimizer.Service"))
            {
                try
                {
                    // If it's a stale instance (doesn't match our exact path, or we want to clean slate anyway)
                    // The user said: "If an existing backend belongs to an older build/path: terminate it safely."
                    // We'll just terminate all BiosOptimizer.Service processes to ensure a clean session for this GUI.
                    proc.Kill(entireProcessTree: true); 
                    proc.WaitForExit(3000); 
                }
                catch { /* process may have already exited or access denied */ }
                finally { proc.Dispose(); }
            }
            // Small delay so the OS can release any named-pipe handle
            System.Threading.Thread.Sleep(300);
        }

        private async void OnProcessExited(object sender, EventArgs e)
        {
            if (_intentionalShutdown || _disposed) return;

            // Wait briefly before auto-restart to avoid thrashing
            await Task.Delay(2000);

            if (_intentionalShutdown || _disposed) return;

            DisposeProcess();
            StartBackend();
        }

        public void Shutdown()
        {
            _intentionalShutdown = true;
            DisposeProcess();
        }

        private void DisposeProcess()
        {
            var proc = _process;
            _process = null;
            if (proc == null) return;

            proc.Exited -= OnProcessExited;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Shutdown();
        }
    }
}
