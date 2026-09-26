#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BiosOptimizer.Core.Implementations.Profiles
{
    public class OptimizationHistoryRecord
    {
        public string OptimizationId { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public string LastBeforeState { get; set; } = string.Empty;
        public string LastAfterState { get; set; } = string.Empty;
        public DateTime LastAppliedTime { get; set; } = DateTime.UtcNow;

        public double EffectivenessScore => Attempts > 0 ? (Successes / (double)Attempts) * 100.0 : 0.0;

        public LearningStatus Status
        {
            get
            {
                if (Attempts == 0) return LearningStatus.Supported;
                if (Successes == 0 && Failures > 0) return LearningStatus.Ineffective;
                if (Successes >= 5 && EffectivenessScore >= 85.0) return LearningStatus.ProvenEffective;
                if (Successes > 0) return LearningStatus.Verified;
                return LearningStatus.Tried;
            }
        }

        public string SummaryText => Attempts > 0 
            ? $"{Successes}/{Attempts} verified successful ({EffectivenessScore:F0}% effectiveness)"
            : "No previous executions recorded on this PC";
    }

    public class OptimizationLearningEngine
    {
        private static readonly Lazy<OptimizationLearningEngine> _instance = new(() => new OptimizationLearningEngine());
        public static OptimizationLearningEngine Instance => _instance.Value;

        private readonly string _filePath;
        private readonly object _lock = new();
        private Dictionary<string, OptimizationHistoryRecord> _records = new(StringComparer.OrdinalIgnoreCase);

        public OptimizationLearningEngine()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "machine_profile_learning.json");
            Load();
        }

        public OptimizationHistoryRecord GetRecord(string optimizationId)
        {
            lock (_lock)
            {
                if (_records.TryGetValue(optimizationId, out var record))
                {
                    return record;
                }
                return new OptimizationHistoryRecord { OptimizationId = optimizationId };
            }
        }

        public void RecordOutcome(string optimizationId, string profileId, bool success, string beforeState, string afterState)
        {
            lock (_lock)
            {
                if (!_records.TryGetValue(optimizationId, out var record))
                {
                    record = new OptimizationHistoryRecord
                    {
                        OptimizationId = optimizationId,
                        ProfileId = profileId
                    };
                    _records[optimizationId] = record;
                }

                record.Attempts++;
                if (success) record.Successes++;
                else record.Failures++;

                record.ProfileId = profileId;
                record.LastBeforeState = beforeState;
                record.LastAfterState = afterState;
                record.LastAppliedTime = DateTime.UtcNow;

                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<OptimizationHistoryRecord>>(json);
                    if (list != null)
                    {
                        _records = list.ToDictionary(r => r.OptimizationId, r => r, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                var list = _records.Values.ToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }
    }
}
