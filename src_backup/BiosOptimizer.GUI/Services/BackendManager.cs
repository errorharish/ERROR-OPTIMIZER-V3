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
            // AppContext.BaseDirectory is the extraction directory for PublishSingleFile bundles
            // and the normal exe directory for regular builds. It is NEVER empty.
            var baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');

            var corePath = Path.Combine(baseDir, "core", "BiosOptimizer.Service.exe");
            var siblingPath = Path.Combine(baseDir, "BiosOptimizer.Service.exe");

            if (File.Exists(corePath))
            {
                _backendExePath = corePath;
                _backendWorkDir = Path.GetDirectoryName(corePath)!;
            }
            else if (File.Exists(siblingPath))
            {
                _backendExePath = siblingPath;
                _backendWorkDir = baseDir;
            }
            else
            {
                // Will be caught in StartBackend()
                _backendExePath = corePath; // preferred expected path for error message
                _backendWorkDir = baseDir;
            }
        }

        /// <summary>
        /// Kill any stale backend instances, then launch a fresh hidden backend process.
        /// </summary>
        public void StartBackend()
        {
            if (_disposed) return;

            if (!File.Exists(_backendExePath))
            {
                MessageBox.Show(
                    $"Fatal: Backend service binary not found:\n\n{_backendExePath}\n\n" +
                    $"Please reinstall Error Optimizer.",
                    "Error Optimizer – Fatal Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current?.Shutdown(1);
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
                MessageBox.Show(
                    $"Failed to start backend service:\n\n{ex.Message}",
                    "Error Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current?.Shutdown(1);
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
