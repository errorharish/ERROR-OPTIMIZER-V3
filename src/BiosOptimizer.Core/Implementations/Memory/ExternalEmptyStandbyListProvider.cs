#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations.Memory
{
    public class ExternalEmptyStandbyListProvider : IStandbyCleanupProvider
    {
        private static readonly Lazy<ExternalEmptyStandbyListProvider> _instance = new(() => new ExternalEmptyStandbyListProvider());
        public static ExternalEmptyStandbyListProvider Instance => _instance.Value;

        private const string KnownExeName = "EmptyStandbyList.exe";

        public bool IsAvailable => ValidateExecutable(out _);

        public string ResolvedExecutablePath => FindExecutable();

        public string FindExecutable()
        {
            // 1. Check Application Base Directory (published runtime location)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string pathInBase = Path.Combine(baseDir, KnownExeName);
            if (File.Exists(pathInBase)) return Path.GetFullPath(pathInBase);

            // 2. Check core/ subfolder
            string coreDir = Path.Combine(baseDir, "core", KnownExeName);
            if (File.Exists(coreDir)) return Path.GetFullPath(coreDir);

            // 3. Check LocalAppData
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer", KnownExeName);
            if (File.Exists(appData)) return Path.GetFullPath(appData);

            // 4. Check Current Working Directory
            string currentDir = Path.Combine(Environment.CurrentDirectory, KnownExeName);
            if (File.Exists(currentDir)) return Path.GetFullPath(currentDir);

            return string.Empty;
        }

        public bool ValidateExecutable(out string reason)
        {
            string path = FindExecutable();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                reason = "EmptyStandbyList.exe not found in application directory.";
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length < 1000)
                {
                    reason = $"EmptyStandbyList.exe file size ({fileInfo.Length} bytes) is invalid or corrupted.";
                    return false;
                }

                reason = "Executable validated and ready.";
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Validation error: {ex.Message}";
                return false;
            }
        }

        public async Task<StandbyCleanupExecutionResult> ExecuteAsync(bool lowPriorityOnly = false, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            int callerThreadId = Environment.CurrentManagedThreadId;
            bool isStaThread = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;

            LogStage("START_EMPTY_STANDBY", $"CallerThread={callerThreadId}, IsSta={isStaThread}, LowPriOnly={lowPriorityOnly}");

            var result = new StandbyCleanupExecutionResult();

            string exePath = FindExecutable();
            result.ExecutablePath = exePath;

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                result.Status = StandbyCleanupStatus.NotFound;
                result.ErrorDetails = "EmptyStandbyList.exe was not found in application path or root project path.";
                result.SummaryMessage = "EMPTY STANDBY EXECUTABLE NOT FOUND: EmptyStandbyList.exe is missing from application directory.";
                LogStage("FINAL_STATE", $"Status=NOT_FOUND, Reason={result.ErrorDetails}");
                return result;
            }

            LogStage("STATE_READ_STARTED", "Capturing before-cleanup memory snapshot...");
            var before = WindowsMemoryListProvider.Instance.GetCurrentSnapshot();
            result.Before = before;
            LogStage("STATE_READ_COMPLETED", $"Before: Standby={before.StandbyFormatted}, LowPri={before.LowPriorityStandbyFormatted}, Available={before.AvailableFormatted}");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            string args = lowPriorityOnly ? "priority0standbylist" : "standbylist";
            LogStage("PROCESS_LAUNCH_STARTED", $"Exe='{exePath}', Args='{args}'");

            var execRes = await TaskExecutionSupervisor.ExecuteProcessAsync(
                exePath,
                args,
                TimeSpan.FromSeconds(5),
                linkedCts.Token);

            result.StandardOutput = execRes.StandardOutput;
            result.StandardError = execRes.StandardError;
            result.ExitCode = execRes.ExitCode;
            result.Duration = sw.Elapsed;

            LogStage("PROCESS_EXITED", $"ExitCode={execRes.ExitCode}, State={execRes.State}, Duration={result.Duration.TotalMilliseconds:F1}ms");

            // Read post-cleanup state immediately
            LogStage("STATE_READ_AFTER_STARTED", "Capturing post-cleanup memory snapshot...");
            var after = WindowsMemoryListProvider.Instance.GetCurrentSnapshot();
            result.After = after;
            LogStage("STATE_READ_AFTER_COMPLETED", $"After: Standby={after.StandbyFormatted}, LowPri={after.LowPriorityStandbyFormatted}, Available={after.AvailableFormatted}");

            if (execRes.State == ExecutionTerminalState.Timeout)
            {
                result.Status = StandbyCleanupStatus.Timeout;
                result.ErrorDetails = "Execution timed out after 5 seconds";
                result.SummaryMessage = "STANDBY CLEANUP TIMED OUT (5s limit reached). App remains responsive.";
                LogStage("FINAL_STATE", "Status=TIMEOUT");
                return result;
            }

            if (execRes.State == ExecutionTerminalState.Cancelled)
            {
                result.Status = StandbyCleanupStatus.Cancelled;
                result.ErrorDetails = "Operation cancelled by caller";
                result.SummaryMessage = "Standby cleanup cancelled.";
                LogStage("FINAL_STATE", "Status=CANCELLED");
                return result;
            }

            if (execRes.ExitCode != 0)
            {
                result.Status = StandbyCleanupStatus.NonzeroExit;
                result.ErrorDetails = $"Exit code: {result.ExitCode}. Stderr: {result.StandardError}";
                result.SummaryMessage = $"EmptyStandbyList.exe returned non-zero exit code ({result.ExitCode}).";
                LogStage("FINAL_STATE", $"Status=NONZERO_EXIT, Code={result.ExitCode}");
            }
            else
            {
                long standbyReclaimed = result.StandbyReclaimedBytes;

                // Runtime Memory Transaction Recording
                try
                {
                    BackupManager.Instance.CaptureMemoryRuntime(
                        "EmptyStandbyList.exe Standby Purge",
                        standbyReclaimed / (1024 * 1024),
                        $"Standby: {before.StandbyFormatted}",
                        $"Standby: {after.StandbyFormatted}",
                        $"Available: {before.AvailableFormatted} -> {after.AvailableFormatted}"
                    );
                }
                catch { }

                if (standbyReclaimed > 20 * 1024 * 1024 || (before.StandbyCacheBytes > 100 * 1024 * 1024 && after.StandbyCacheBytes < 100 * 1024 * 1024))
                {
                    result.Status = StandbyCleanupStatus.SuccessVerified;
                    result.SummaryMessage = $"Standby memory reclaimed: {result.StandbyReclaimedFormatted} (Available: {before.AvailableFormatted} → {after.AvailableFormatted}).";
                }
                else
                {
                    result.Status = StandbyCleanupStatus.SuccessNoMeaningfulReclaim;
                    result.SummaryMessage = "Standby list already optimal. No meaningful standby memory to reclaim.";
                }

                LogStage("FINAL_STATE", $"Status={result.Status}, Reclaimed={result.StandbyReclaimedFormatted}, Elapsed={sw.ElapsedMilliseconds}ms");
            }

            return result;
        }

        private static void LogStage(string stage, string detail)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ErrorOptimizer",
                    "logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "standby_memory.log");
                string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {detail}";
                File.AppendAllLines(logFile, new[] { line });
                Debug.WriteLine(line);
            }
            catch { }
        }
    }
}
