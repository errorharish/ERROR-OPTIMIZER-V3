#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations
{
    public enum ExecutionTerminalState
    {
        Completed,
        Failed,
        Cancelled,
        Timeout,
        StallDetected
    }

    public class SupervisedExecutionResult
    {
        public ExecutionTerminalState State { get; set; } = ExecutionTerminalState.Failed;
        public int ExitCode { get; set; } = -1;
        public int ProcessId { get; set; } = 0;
        public bool ProcessStarted { get; set; } = false;
        public string Command { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public string ErrorSummary { get; set; } = string.Empty;
        public string DiagnosedCause { get; set; } = string.Empty;
        public bool IsTimedOut => State == ExecutionTerminalState.Timeout;
        public bool IsCancelled => State == ExecutionTerminalState.Cancelled;
        public bool Success => State == ExecutionTerminalState.Completed && ExitCode == 0;
    }

    public static class TaskExecutionSupervisor
    {
        /// <summary>
        /// Resolves the absolute path to a native Windows System32 / Sysnative binary,
        /// preventing WOW64 file-system redirection failures on 64-bit systems.
        /// </summary>
        public static string ResolveNativeSystemBinary(string fileName)
        {
            if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            {
                return fileName;
            }

            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            // If running 32-bit process on 64-bit OS, access native 64-bit System32 via Sysnative
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                string sysnative = Path.Combine(windir, "Sysnative", fileName);
                if (File.Exists(sysnative)) return sysnative;
            }

            string system32 = Path.Combine(windir, "System32", fileName);
            if (File.Exists(system32)) return system32;

            return fileName;
        }

        /// <summary>
        /// Executes an external process under strict supervision with async stdout/stderr streaming,
        /// automatic stdin closing, hard timeout enforcement, and guaranteed process tree termination.
        /// </summary>
        public static async Task<SupervisedExecutionResult> ExecuteProcessAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken externalCt = default,
            Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null,
            Action<string>? onHeartbeat = null,
            Action<int, string>? onProcessStarted = null)
        {
            var sw = Stopwatch.StartNew();
            var resolvedFile = ResolveNativeSystemBinary(fileName);
            var result = new SupervisedExecutionResult
            {
                Command = $"{resolvedFile} {arguments}".Trim()
            };

            CancellationTokenSource? timeoutCts = null;
            CancellationTokenSource linkedCts;

            if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan && timeout < TimeSpan.FromDays(1))
            {
                timeoutCts = new CancellationTokenSource(timeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, timeoutCts.Token);
            }
            else
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            }

            var ct = linkedCts.Token;

            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = resolvedFile,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process? proc = null;
            DateTime lastActivity = DateTime.UtcNow;

            try
            {
                proc = new Process { StartInfo = psi };

                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        lastActivity = DateTime.UtcNow;
                        stdoutSb.AppendLine(e.Data);
                        onOutputLine?.Invoke(e.Data);
                    }
                };

                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        lastActivity = DateTime.UtcNow;
                        stderrSb.AppendLine(e.Data);
                        onErrorLine?.Invoke(e.Data);
                    }
                };

                if (!proc.Start())
                {
                    result.State = ExecutionTerminalState.Failed;
                    result.ProcessStarted = false;
                    result.ErrorSummary = $"Failed to launch process: {resolvedFile}";
                    result.Duration = sw.Elapsed;
                    return result;
                }

                result.ProcessStarted = true;
                result.ProcessId = proc.Id;

                try { onProcessStarted?.Invoke(proc.Id, Path.GetFileName(resolvedFile)); } catch { }

                // Immediately close standard input to prevent any prompt hanging
                try { proc.StandardInput.Close(); } catch { }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Periodic Watchdog & Heartbeat Loop (Process State Rule: status remains RUNNING while active)
                var waitTask = proc.WaitForExitAsync(ct);
                while (!waitTask.IsCompleted)
                {
                    var completed = await Task.WhenAny(waitTask, Task.Delay(500, ct));
                    if (completed == waitTask) break;

                    onHeartbeat?.Invoke($"RUNNING (PID: {result.ProcessId}, Elapsed: {sw.Elapsed.TotalSeconds:F0}s, LastActivity: {(DateTime.UtcNow - lastActivity).TotalSeconds:F1}s ago)");
                }

                await waitTask;

                result.ExitCode = proc.ExitCode;
                result.State = proc.ExitCode == 0 ? ExecutionTerminalState.Completed : ExecutionTerminalState.Failed;
                result.StandardOutput = stdoutSb.ToString();
                result.StandardError = stderrSb.ToString();
                result.Duration = sw.Elapsed;
                return result;
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(proc);

                result.StandardOutput = stdoutSb.ToString();
                result.StandardError = stderrSb.ToString();
                result.Duration = sw.Elapsed;

                if (timeoutCts != null && timeoutCts.IsCancellationRequested)
                {
                    result.State = ExecutionTerminalState.Timeout;
                    result.ErrorSummary = $"Execution timed out after {timeout.TotalSeconds:F0} seconds.";
                }
                else
                {
                    result.State = ExecutionTerminalState.Cancelled;
                    result.ErrorSummary = "Execution cancelled by user.";
                }

                return result;
            }
            catch (Exception ex)
            {
                KillProcessTree(proc);

                result.State = ExecutionTerminalState.Failed;
                result.ErrorSummary = ex.Message;
                result.StandardOutput = stdoutSb.ToString();
                result.StandardError = stderrSb.ToString();
                result.Duration = sw.Elapsed;
                return result;
            }
            finally
            {
                proc?.Dispose();
                timeoutCts?.Dispose();
                linkedCts.Dispose();
            }
        }

        private static void KillProcessTree(Process? proc)
        {
            if (proc == null) return;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }
    }
}
