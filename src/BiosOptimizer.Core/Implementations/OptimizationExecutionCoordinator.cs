#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations
{
    public class OptimizationExecutionCoordinator : IOptimizationExecutionCoordinator
    {
        private static readonly Lazy<OptimizationExecutionCoordinator> _instance = new(() => new OptimizationExecutionCoordinator());
        public static OptimizationExecutionCoordinator Instance => _instance.Value;

        private readonly ConcurrentDictionary<OptimizationCategory, SemaphoreSlim> _categoryLocks = new();
        private readonly ConcurrentDictionary<OptimizationCategory, OptimizationOperationState> _categoryStates = new();
        private readonly SemaphoreSlim _globalLock = new(1, 1);
        private readonly string _logFilePath;

        public event Action<OptimizationExecutionContext>? OperationStateChanged;

        public OptimizationExecutionCoordinator()
        {
            foreach (OptimizationCategory cat in Enum.GetValues(typeof(OptimizationCategory)))
            {
                _categoryLocks[cat] = new SemaphoreSlim(1, 1);
                _categoryStates[cat] = OptimizationOperationState.Idle;
            }

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer");
            string logDir = Path.Combine(appData, "logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "optimization_execution.log");
        }

        public OptimizationOperationState GetCategoryState(OptimizationCategory category)
        {
            return _categoryStates.TryGetValue(category, out var st) ? st : OptimizationOperationState.Idle;
        }

        public bool IsCategoryBusy(OptimizationCategory category)
        {
            var st = GetCategoryState(category);
            return st == OptimizationOperationState.Queued ||
                   st == OptimizationOperationState.Running ||
                   st == OptimizationOperationState.Verifying;
        }

        public async Task<OptimizationExecutionContext> ExecuteAsync(
            string operationName,
            OptimizationCategory category,
            Func<CancellationToken, Task<string>> executeAction,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            var effectiveTimeout = timeout ?? GetDefaultTimeout(category);
            var context = new OptimizationExecutionContext
            {
                OperationName = operationName,
                Category = category,
                Timeout = effectiveTimeout,
                StartTimeUtc = DateTime.UtcNow,
                CallerThreadId = Environment.CurrentManagedThreadId,
                IsStaThread = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
            };

            var catLock = _categoryLocks.GetOrAdd(category, _ => new SemaphoreSlim(1, 1));
            bool isGlobal = category == OptimizationCategory.Global;

            LogDiagnostic(context, "ENQUEUE", $"Operation queued. Timeout: {effectiveTimeout.TotalSeconds:F0}s, CallerThread: {context.CallerThreadId}, STA: {context.IsStaThread}");

            SetCategoryState(context, OptimizationOperationState.Queued);

            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var linkedToken = linkedCts.Token;

            bool lockAcquired = false;
            bool globalLockAcquired = false;

            try
            {
                // Acquire locks
                if (isGlobal)
                {
                    globalLockAcquired = await _globalLock.WaitAsync((int)effectiveTimeout.TotalMilliseconds, linkedToken);
                    if (!globalLockAcquired)
                    {
                        context.State = OptimizationOperationState.TimedOut;
                        context.ErrorDetails = "Failed to acquire global optimization lock within timeout.";
                        LogDiagnostic(context, "TIMEOUT_LOCK", context.ErrorDetails);
                        return context;
                    }
                }

                lockAcquired = await catLock.WaitAsync((int)effectiveTimeout.TotalMilliseconds, linkedToken);
                if (!lockAcquired)
                {
                    context.State = OptimizationOperationState.TimedOut;
                    context.ErrorDetails = $"Category lock for '{category}' busy or timed out.";
                    LogDiagnostic(context, "TIMEOUT_CAT_LOCK", context.ErrorDetails);
                    return context;
                }

                SetCategoryState(context, OptimizationOperationState.Running);
                LogDiagnostic(context, "EXECUTE_START", "Background execution started.");

                // Execute purely on background thread pool
                var executeTask = Task.Run(async () =>
                {
                    return await executeAction(linkedToken);
                }, linkedToken);

                var completedTask = await Task.WhenAny(executeTask, Task.Delay(effectiveTimeout, linkedToken));

                if (completedTask == executeTask)
                {
                    string summary = await executeTask;
                    context.StatusMessage = summary;
                    context.Succeeded = true;
                    context.State = OptimizationOperationState.Success;
                    LogDiagnostic(context, "EXECUTE_SUCCESS", $"Result: {summary}");
                }
                else
                {
                    context.Succeeded = false;
                    if (timeoutCts.IsCancellationRequested)
                    {
                        context.State = OptimizationOperationState.TimedOut;
                        context.ErrorDetails = $"Operation exceeded timeout of {effectiveTimeout.TotalSeconds:F0}s.";
                        LogDiagnostic(context, "TIMEOUT", context.ErrorDetails);
                    }
                    else
                    {
                        context.State = OptimizationOperationState.Cancelled;
                        context.ErrorDetails = "Operation cancelled by user.";
                        LogDiagnostic(context, "CANCELLED", context.ErrorDetails);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                context.Succeeded = false;
                if (timeoutCts.IsCancellationRequested)
                {
                    context.State = OptimizationOperationState.TimedOut;
                    context.ErrorDetails = $"Operation timed out ({effectiveTimeout.TotalSeconds:F0}s limit reached).";
                }
                else
                {
                    context.State = OptimizationOperationState.Cancelled;
                    context.ErrorDetails = "Operation was cancelled.";
                }
                LogDiagnostic(context, "CANCELLED_EX", context.ErrorDetails);
            }
            catch (Exception ex)
            {
                context.Succeeded = false;
                context.State = OptimizationOperationState.Failed;
                context.ErrorDetails = ex.Message;
                LogDiagnostic(context, "EXCEPTION", ex.ToString());
            }
            finally
            {
                context.EndTimeUtc = DateTime.UtcNow;

                // Guaranteed category release
                if (lockAcquired)
                {
                    try { catLock.Release(); } catch { }
                }

                if (globalLockAcquired)
                {
                    try { _globalLock.Release(); } catch { }
                }

                SetCategoryState(context, OptimizationOperationState.Idle);
                LogDiagnostic(context, "FINAL_IDLE", $"Released lock. Duration: {context.Duration.TotalMilliseconds:F0}ms. Succeeded: {context.Succeeded}");
            }

            return context;
        }

        private void SetCategoryState(OptimizationExecutionContext ctx, OptimizationOperationState st)
        {
            _categoryStates[ctx.Category] = st;
            OperationStateChanged?.Invoke(ctx);
        }

        private static TimeSpan GetDefaultTimeout(OptimizationCategory category) => category switch
        {
            OptimizationCategory.Memory => TimeSpan.FromSeconds(8),
            OptimizationCategory.Storage => TimeSpan.FromSeconds(30),
            OptimizationCategory.Gpu => TimeSpan.FromSeconds(10),
            OptimizationCategory.Power => TimeSpan.FromSeconds(10),
            OptimizationCategory.Network => TimeSpan.FromSeconds(10),
            OptimizationCategory.Registry => TimeSpan.FromSeconds(10),
            OptimizationCategory.Input => TimeSpan.FromSeconds(10),
            OptimizationCategory.Global => TimeSpan.FromSeconds(60),
            _ => TimeSpan.FromSeconds(10)
        };

        private void LogDiagnostic(OptimizationExecutionContext ctx, string stage, string detail)
        {
            try
            {
                string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{ctx.OperationId}] [{ctx.Category}] [{stage}] {ctx.OperationName}: {detail}";
                File.AppendAllLines(_logFilePath, new[] { line });
                Debug.WriteLine(line);
            }
            catch { }
        }
    }
}