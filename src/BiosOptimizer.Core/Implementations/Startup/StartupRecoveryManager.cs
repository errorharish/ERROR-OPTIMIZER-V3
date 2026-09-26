using System;
using System.IO;
using System.Text.Json;

namespace BiosOptimizer.Core.Implementations.Startup
{
    public class StartupStateData
    {
        public bool LastStartupSuccess { get; set; } = true;
        public string LastFailingSubsystem { get; set; } = "None";
        public string LastExceptionType { get; set; } = "None";
        public string LastExceptionMessage { get; set; } = "None";
        public bool SafeMode { get; set; } = false;
        public int StartupAttemptCount { get; set; } = 0;
        public DateTime LastAttemptUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class StartupRecoveryManager
    {
        private static readonly Lazy<StartupRecoveryManager> _instance = new(() => new StartupRecoveryManager());
        public static StartupRecoveryManager Instance => _instance.Value;

        private readonly string _stateFilePath;
        private readonly object _lock = new();
        private StartupStateData _currentState;

        public StartupStateData CurrentState => _currentState;

        private StartupRecoveryManager()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer");
            try { Directory.CreateDirectory(appData); } catch { }
            _stateFilePath = Path.Combine(appData, "startup-state.json");
            _currentState = LoadState();
        }

        private StartupStateData LoadState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    string json = File.ReadAllText(_stateFilePath);
                    var data = JsonSerializer.Deserialize<StartupStateData>(json);
                    if (data != null) return data;
                }
            }
            catch { }
            return new StartupStateData();
        }

        public void RecordStartupAttempt()
        {
            lock (_lock)
            {
                _currentState.StartupAttemptCount++;
                _currentState.LastAttemptUtc = DateTime.UtcNow;
                _currentState.LastStartupSuccess = false; // Will be set to true upon complete window presentation
                SaveState();
            }
        }

        public void RecordStartupSuccess()
        {
            lock (_lock)
            {
                _currentState.LastStartupSuccess = true;
                _currentState.StartupAttemptCount = 0;
                _currentState.LastFailingSubsystem = "None";
                _currentState.LastExceptionType = "None";
                _currentState.LastExceptionMessage = "None";
                SaveState();
            }
        }

        public void RecordStartupFailure(string subsystem, Exception ex)
        {
            lock (_lock)
            {
                _currentState.LastStartupSuccess = false;
                _currentState.LastFailingSubsystem = subsystem;
                _currentState.LastExceptionType = ex?.GetType().FullName ?? "UnknownException";
                _currentState.LastExceptionMessage = ex?.Message ?? "Unknown Error";
                if (_currentState.StartupAttemptCount >= 2)
                {
                    _currentState.SafeMode = true;
                }
                SaveState();
            }
        }

        public bool ShouldEnableSafeMode()
        {
            lock (_lock)
            {
                return _currentState.SafeMode || _currentState.StartupAttemptCount >= 2;
            }
        }

        public void ResetSafeMode()
        {
            lock (_lock)
            {
                _currentState.SafeMode = false;
                _currentState.StartupAttemptCount = 0;
                SaveState();
            }
        }

        private void SaveState()
        {
            try
            {
                string json = JsonSerializer.Serialize(_currentState, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_stateFilePath, json);
            }
            catch { }
        }
    }
}
