using System;
using System.Collections.Generic;

namespace BiosOptimizer.Core.Models
{
    public enum SystemHealthCategoryType
    {
        Memory,
        Storage,
        WindowsIntegrity,
        WindowsMaintenance,
        Hardware,
        Power,
        Network,
        Gpu,
        Advanced
    }

    public enum HealthState
    {
        Excellent,
        Good,
        Warning,
        Critical,
        Unknown
    }

    public enum RecommendationSeverity
    {
        Info,
        Low,
        Medium,
        High,
        Critical
    }

    public class HealthRecommendation
    {
        public string RecommendationId { get; set; } = string.Empty;
        public SystemHealthCategoryType Category { get; set; }
        public RecommendationSeverity Severity { get; set; } = RecommendationSeverity.Low;
        public string Title { get; set; } = string.Empty;
        public string Why { get; set; } = string.Empty;
        public string Evidence { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Risk { get; set; } = "SAFE";
        public double Confidence { get; set; } = 1.0;
        public string ActionId { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = "OPTIMIZE";
        public string TargetEngine { get; set; } = string.Empty;
        public bool RequiresAdmin { get; set; }
        public bool RequiresReboot { get; set; }
    }

    public class HealthCategoryScore
    {
        public SystemHealthCategoryType Category { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "🛠";
        public int Score { get; set; } = 100;
        public double Weight { get; set; } = 10.0;
        public HealthState State { get; set; } = HealthState.Excellent;
        public string HealthLabel { get; set; } = "OPTIMAL";
        public string Evidence { get; set; } = string.Empty;
        public double Confidence { get; set; } = 1.0;
        public List<HealthRecommendation> Recommendations { get; set; } = new();
        public DateTime LastChecked { get; set; } = DateTime.Now;
        public bool RequiresAdmin { get; set; }
        public bool RequiresReboot { get; set; }
    }

    public class SystemHealthSnapshot
    {
        public int OverallScore { get; set; } = 100;
        public HealthState OverallHealthState { get; set; } = HealthState.Excellent;
        public string OverallHealthText { get; set; } = "SYSTEM HEALTHY";
        public string? CriticalCapApplied { get; set; }
        public List<HealthCategoryScore> Categories { get; set; } = new();
        public List<HealthRecommendation> ActiveRecommendations { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public TimeSpan ScanDuration { get; set; } = TimeSpan.Zero;
    }
}
