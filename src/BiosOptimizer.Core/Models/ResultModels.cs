namespace BiosOptimizer.Core.Models;

public enum ResultStatus
{
    Success,
    Verified,
    AlreadyOptimized,
    NotApplicable,
    NotAvailable,
    Blocked,
    Reverted,
    Skipped,
    RequiresReboot,
    Failed,
    Running,
    Verifying,
    Timeout,
    Cancelled
}

public class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public SynchronousProgress(Action<T> handler) => _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    public void Report(T value) => _handler(value);
}

public class OptimizationResult
{
    public ResultStatus Status { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TechnicalDetails { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public bool RequiresReboot { get; set; }
}

public class ConfirmationContext
{
    public bool IsConfirmed { get; set; }
}

public class PerformanceBaseline
{
    public DateTime Timestamp { get; set; }
    public int ProcessCount { get; set; }
    public long AvailableRamBytes { get; set; }
    public double CpuUsage { get; set; }
    public double GpuUsage { get; set; } // If available
    public string PowerPlan { get; set; } = string.Empty;
    public string ActiveWorkload { get; set; } = string.Empty;
}

public class PerformanceResult
{
    public PerformanceBaseline Before { get; set; } = new();
    public PerformanceBaseline After { get; set; } = new();
    public int DeltaProcessCount { get; set; }
    public long DeltaAvailableRamBytes { get; set; }
    public bool MetricAvailability { get; set; }
}

