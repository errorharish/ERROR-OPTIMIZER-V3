using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations.Diagnostics;

namespace BiosOptimizer.Core.Implementations.Startup
{
    public enum StartupTaskState
    {
        Pending,
        Running,
        Completed,
        Failed,
        Skipped
    }

    public class StartupTaskInfo
    {
        public string Name { get; set; } = string.Empty;
        public StartupTaskState State { get; set; } = StartupTaskState.Pending;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool IsCritical { get; set; }
    }

    public sealed class StartupTaskCoordinator
    {
        private static readonly Lazy<StartupTaskCoordinator> _instance = new(() => new StartupTaskCoordinator());
        public static StartupTaskCoordinator Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, StartupTaskInfo> _tasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public event Action<StartupTaskInfo>? TaskStateChanged;
        public bool IsStartupCompleted { get; private set; }
        public bool IsDegradedStartup { get; private set; }

        private StartupTaskCoordinator()
        {
            RegisterDefaultTasks();
        }

        private void RegisterDefaultTasks()
        {
            string[] defaultTaskNames = new[]
            {
                "LoadSettings",
                "LoadTheme",
                "DetectHardware",
                "LoadCachedProfileState",
                "StartTelemetry",
                "ReconcileSystem",
                "InitializeOptionalEngines"
            };

            foreach (var name in defaultTaskNames)
            {
                _tasks[name] = new StartupTaskInfo
                {
                    Name = name,
                    State = StartupTaskState.Pending
                };
            }
        }

        public IReadOnlyList<StartupTaskInfo> GetAllTasks()
        {
            lock (_lock)
            {
                return new List<StartupTaskInfo>(_tasks.Values);
            }
        }

        public StartupTaskInfo GetTask(string name)
        {
            return _tasks.GetOrAdd(name, n => new StartupTaskInfo { Name = n });
        }

        public async Task<bool> ExecuteTaskAsync(
            string taskName,
            Func<CancellationToken, Task> action,
            TimeSpan timeout,
            bool isCritical = false,
            CancellationToken parentCt = default)
        {
            var taskInfo = GetTask(taskName);
            taskInfo.IsCritical = isCritical;
            taskInfo.State = StartupTaskState.Running;
            TaskStateChanged?.Invoke(taskInfo);

            ApplicationCrashLogger.Instance.RecordStartupTask(taskName);

            var sw = Stopwatch.StartNew();
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentCt, timeoutCts.Token);

            try
            {
                await action(linkedCts.Token).ConfigureAwait(false);
                sw.Stop();

                taskInfo.Duration = sw.Elapsed;
                taskInfo.State = StartupTaskState.Completed;
                TaskStateChanged?.Invoke(taskInfo);
                return true;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                sw.Stop();
                taskInfo.Duration = sw.Elapsed;
                taskInfo.State = StartupTaskState.Failed;
                taskInfo.ErrorMessage = $"Timeout exceeded ({timeout.TotalSeconds:F0}s)";
                IsDegradedStartup = true;

                ApplicationCrashLogger.Instance.LogCrash(
                    new TimeoutException($"Startup task '{taskName}' timed out after {timeout.TotalSeconds}s"),
                    $"Startup.{taskName}",
                    isFatal: false);

                TaskStateChanged?.Invoke(taskInfo);
                return false;
            }
            catch (Exception ex)
            {
                sw.Stop();
                taskInfo.Duration = sw.Elapsed;
                taskInfo.State = StartupTaskState.Failed;
                taskInfo.ErrorMessage = ex.Message;
                IsDegradedStartup = true;

                ApplicationCrashLogger.Instance.LogCrash(ex, $"Startup.{taskName}", isFatal: isCritical);

                TaskStateChanged?.Invoke(taskInfo);

                if (isCritical)
                {
                    throw; // Propagate critical exceptions (e.g. fatal host failure)
                }

                return false; // Non-critical failures return false and allow startup to continue
            }
        }

        public void MarkCompleted()
        {
            IsStartupCompleted = true;
        }
    }
}
