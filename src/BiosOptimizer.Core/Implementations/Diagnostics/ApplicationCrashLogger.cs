using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace BiosOptimizer.Core.Implementations.Diagnostics
{
    public sealed class ApplicationCrashLogger
    {
        private static readonly Lazy<ApplicationCrashLogger> _instance = new(() => new ApplicationCrashLogger());
        public static ApplicationCrashLogger Instance => _instance.Value;

        private readonly string _logDir;
        private readonly string _crashHistoryFile;
        private readonly string _stateFile;
        private readonly object _lock = new();

        public string LastStartupTask { get; set; } = "None";
        public string LastNavigationRoute { get; set; } = "None";
        public string ActiveProfile { get; set; } = "Normal";

        private ApplicationCrashLogger()
        {
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer", "logs");
            _crashHistoryFile = Path.Combine(_logDir, "crash_history.json");
            _stateFile = Path.Combine(_logDir, "last_app_state.json");

            try
            {
                if (!Directory.Exists(_logDir))
                {
                    Directory.CreateDirectory(_logDir);
                }
            }
            catch { }
        }

        public void RecordStartupTask(string taskName)
        {
            LastStartupTask = taskName;
            PersistAppState();
        }

        public void LogStartupPhaseStart(string phase)
        {
            try
            {
                LastStartupTask = $"START {phase}";
                PersistAppState();
                lock (_lock)
                {
                    string startupLog = Path.Combine(_logDir, "startup.log");
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] START {phase}\r\n";
                    File.AppendAllText(startupLog, line);
                }
            }
            catch { }
        }

        public void LogStartupPhaseSuccess(string phase)
        {
            try
            {
                LastStartupTask = $"SUCCESS {phase}";
                PersistAppState();
                lock (_lock)
                {
                    string startupLog = Path.Combine(_logDir, "startup.log");
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SUCCESS {phase}\r\n";
                    File.AppendAllText(startupLog, line);
                }
            }
            catch { }
        }

        public void LogStartupPhaseFailed(string phase, Exception? ex = null)
        {
            try
            {
                LastStartupTask = $"FAILED {phase}";
                PersistAppState();
                lock (_lock)
                {
                    string startupLog = Path.Combine(_logDir, "startup.log");
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FAILED {phase}\r\nException:\r\n{ex?.ToString() ?? "Unknown error"}\r\n";
                    File.AppendAllText(startupLog, line);
                }
            }
            catch { }
        }

        public void LogStartupStage(string stageCode, string description)
        {
            try
            {
                LastStartupTask = $"{stageCode} {description}";
                PersistAppState();

                lock (_lock)
                {
                    string stageLog = Path.Combine(_logDir, "startup_stages.log");
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stageCode}] {description}\r\n";
                    File.AppendAllText(stageLog, line);
                }
            }
            catch { }
        }

        public void RecordNavigation(string routeName)
        {
            LastNavigationRoute = routeName;
            PersistAppState();
        }

        private void PersistAppState()
        {
            try
            {
                lock (_lock)
                {
                    var state = new
                    {
                        Timestamp = DateTime.UtcNow.ToString("O"),
                        LastStartupTask,
                        LastNavigationRoute,
                        ActiveProfile
                    };
                    string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_stateFile, json);
                }
            }
            catch { }
        }

        public bool ShouldEnterSafeMode()
        {
            try
            {
                lock (_lock)
                {
                    if (!File.Exists(_crashHistoryFile)) return false;

                    string json = File.ReadAllText(_crashHistoryFile);
                    var history = JsonSerializer.Deserialize<List<string>>(json);
                    if (history == null || history.Count < 2) return false;

                    var recentCrashes = history
                        .Select(h => DateTime.TryParse(h, out var dt) ? dt : DateTime.MinValue)
                        .Where(dt => (DateTime.UtcNow - dt).TotalMinutes <= 3.0)
                        .ToList();

                    return recentCrashes.Count >= 2;
                }
            }
            catch
            {
                return false;
            }
        }

        public void ClearCrashHistory()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(_crashHistoryFile))
                    {
                        File.Delete(_crashHistoryFile);
                    }
                }
            }
            catch { }
        }

        public void LogCrash(Exception ex, string subsystem, bool isFatal = false)
        {
            if (ex == null) return;

            try
            {
                lock (_lock)
                {
                    // Update crash history
                    var history = new List<string>();
                    if (File.Exists(_crashHistoryFile))
                    {
                        try
                        {
                            var existing = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_crashHistoryFile));
                            if (existing != null) history.AddRange(existing);
                        }
                        catch { }
                    }

                    history.Add(DateTime.UtcNow.ToString("O"));
                    if (history.Count > 20) history.RemoveRange(0, history.Count - 20);

                    try
                    {
                        File.WriteAllText(_crashHistoryFile, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    catch { }

                    // Log Detailed Crash Report
                    var sb = new StringBuilder();
                    sb.AppendLine("================================================================================");
                    sb.AppendLine($"[CRASH EVENT] Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | Subsystem: {subsystem} | Fatal: {isFatal}");
                    sb.AppendLine("================================================================================");
                    sb.AppendLine($"App Version     : {typeof(ApplicationCrashLogger).Assembly.GetName().Version?.ToString() ?? "3.0.0"}");
                    sb.AppendLine($"OS Version      : {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
                    sb.AppendLine($"CLR Version     : {Environment.Version}");
                    sb.AppendLine($"Process ID      : {Environment.ProcessId}");
                    sb.AppendLine($"Thread ID       : {Thread.CurrentThread.ManagedThreadId} (ThreadPool: {Thread.CurrentThread.IsThreadPoolThread})");
                    sb.AppendLine($"Last StartupTask: {LastStartupTask}");
                    sb.AppendLine($"Last Navigation : {LastNavigationRoute}");
                    sb.AppendLine($"Active Profile  : {ActiveProfile}");

                    try
                    {
                        var hw = HardwareProfiler.GetQuickProfile();
                        sb.AppendLine($"Hardware        : CPU={hw.CpuModel} ({hw.CpuLogicalCores} cores) | RAM={hw.RamTotalGb:F1}GB (Avail: {hw.RamAvailableGb:F1}GB) | GPU={hw.GpuName}");
                    }
                    catch { }

                    sb.AppendLine("--------------------------------------------------------------------------------");
                    sb.AppendLine("EXCEPTION HIERARCHY:");
                    AppendExceptionDetails(sb, ex, 0);
                    sb.AppendLine("================================================================================\r\n");

                    string logFile = Path.Combine(_logDir, isFatal ? "fatal_crash.log" : "subsystem_errors.log");
                    File.AppendAllText(logFile, sb.ToString());
                }
            }
            catch { }
        }

        private static void AppendExceptionDetails(StringBuilder sb, Exception ex, int depth)
        {
            if (ex == null) return;
            string indent = new string(' ', depth * 2);

            sb.AppendLine($"{indent}[Depth {depth}] {ex.GetType().FullName}: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.TargetSite?.Name))
            {
                sb.AppendLine($"{indent}TargetSite: {ex.TargetSite.DeclaringType?.FullName}.{ex.TargetSite.Name}");
            }
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.AppendLine($"{indent}StackTrace:\r\n{ex.StackTrace}");
            }

            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    AppendExceptionDetails(sb, inner, depth + 1);
                }
            }
            else if (ex.InnerException != null)
            {
                AppendExceptionDetails(sb, ex.InnerException, depth + 1);
            }
        }
    }
}
