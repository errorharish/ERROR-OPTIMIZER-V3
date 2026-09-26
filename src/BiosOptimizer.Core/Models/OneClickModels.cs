#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BiosOptimizer.Core.Models
{
    public enum OneClickActionCategory
    {
        Cache,
        Memory,
        Cleanup,
        Visual,
        HardwareTimers
    }

    public enum OneClickActionStatus
    {
        Available,
        Recommended,
        AlreadyCompleted,
        RequiresAdmin,
        RequiresRestart,
        NotApplicable,
        Scanning,
        Applying,
        Verifying,
        Verified,
        Failed,
        Skipped
    }

    public class OneClickActionItem : INotifyPropertyChanged
    {
        private OneClickActionStatus _status = OneClickActionStatus.Available;
        private string _currentStateText = "Unknown";
        private string _targetStateText = "Optimal";
        private string _metricText = "";
        private bool _isSelected = true;

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public OneClickActionCategory Category { get; set; }
        public string CategoryName => Category switch
        {
            OneClickActionCategory.Cache => "CACHE CLEANUP",
            OneClickActionCategory.Memory => "RAM MANAGEMENT",
            OneClickActionCategory.Cleanup => "SYSTEM CLEANUP",
            OneClickActionCategory.Visual => "VISUAL PERFORMANCE",
            OneClickActionCategory.HardwareTimers => "SYSTEM TIMERS",
            _ => "MAINTENANCE"
        };

        public OneClickActionStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(CanOptimizeDirectly));
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(StatusBadgeText));
                    OnPropertyChanged(nameof(StatusBrushKey));
                }
            }
        }

        public string CurrentStateText
        {
            get => _currentStateText;
            set { if (_currentStateText != value) { _currentStateText = value; OnPropertyChanged(); } }
        }

        public string TargetStateText
        {
            get => _targetStateText;
            set { if (_targetStateText != value) { _targetStateText = value; OnPropertyChanged(); } }
        }

        public string MetricText
        {
            get => _metricText;
            set { if (_metricText != value) { _metricText = value; OnPropertyChanged(); } }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public bool RequiresAdmin { get; set; } = false;
        public bool RequiresRestart { get; set; } = false;
        public string RiskLevel { get; set; } = "LOW";
        public string UnderlyingEngine { get; set; } = "Dedicated One-Click Engine";
        public string ExecutionDetail { get; set; } = "";

        public bool IsCompleted => Status == OneClickActionStatus.AlreadyCompleted || Status == OneClickActionStatus.Verified;
        public bool CanOptimizeDirectly => !IsCompleted && Status != OneClickActionStatus.NotApplicable && Status != OneClickActionStatus.Applying;

        public string ActionButtonText => Status switch
        {
            OneClickActionStatus.Applying => "APPLYING...",
            OneClickActionStatus.Verifying => "VERIFYING...",
            OneClickActionStatus.AlreadyCompleted or OneClickActionStatus.Verified => "✓ VERIFIED",
            OneClickActionStatus.Failed => "↻ RETRY",
            OneClickActionStatus.NotApplicable => "N/A",
            _ => "⚡ OPTIMIZE"
        };

        public string StatusBadgeText => Status switch
        {
            OneClickActionStatus.AlreadyCompleted or OneClickActionStatus.Verified => "✓ OPTIMIZED",
            OneClickActionStatus.Recommended => "⚡ RECOMMENDED",
            OneClickActionStatus.Available => "READY",
            OneClickActionStatus.Applying => "APPLYING",
            OneClickActionStatus.Verifying => "VERIFYING",
            OneClickActionStatus.Failed => "FAILED",
            OneClickActionStatus.NotApplicable => "NOT APPLICABLE",
            _ => "AVAILABLE"
        };

        public string StatusBrushKey => Status switch
        {
            OneClickActionStatus.AlreadyCompleted or OneClickActionStatus.Verified => "SuccessBrush",
            OneClickActionStatus.Recommended or OneClickActionStatus.Applying => "AccentBrush",
            OneClickActionStatus.Failed => "DangerBrush",
            _ => "TextMutedBrush"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AuditedExistingFeature
    {
        public string FeatureName { get; set; } = "";
        public string HandledByModule { get; set; } = "";
        public string AuditStatus { get; set; } = "ALREADY EXISTS (NOT DUPLICATED)";
        public string Description { get; set; } = "";
    }

    public class OneClickPlan
    {
        public List<OneClickActionItem> Actions { get; set; } = new();
        public List<AuditedExistingFeature> ExistingAuditedFeatures { get; set; } = new();
        public List<string> ExcludedRepairFeatures { get; set; } = new();

        public int TotalFound => Actions.Count;
        public int ApplicableCount { get; set; }
        public int RecommendedCount { get; set; }
        public int AlreadyDoneCount { get; set; }
        public int NotAvailableCount { get; set; }
        public int ManualCount { get; set; }
        public long EstimatedBytesRecoverable { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class OneClickExecutionResult
    {
        public bool OverallSuccess { get; set; }
        public int TotalProcessed { get; set; }
        public int AppliedCount { get; set; }
        public int VerifiedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public int ManualCount { get; set; }
        public long BytesRecovered { get; set; }
        public string SummaryMessage { get; set; } = "";
        public List<string> ExecutionLogs { get; set; } = new();
        public List<OneClickActionItem> CompletedActions { get; set; } = new();
    }
}
