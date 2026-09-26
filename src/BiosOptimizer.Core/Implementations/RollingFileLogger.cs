using System;
using System.IO;
using System.Text;

namespace BiosOptimizer.Core.Implementations
{
    public class RollingFileLogger
    {
        private readonly string _logDirectory;
        private readonly string _baseFileName;
        private readonly long _maxFileSizeBytes;
        private readonly int _maxRollingFiles;
        private readonly object _lock = new();

        public RollingFileLogger(string logDirectory, string baseFileName = "error_optimizer.log", long maxFileSizeBytes = 10 * 1024 * 1024, int maxRollingFiles = 3)
        {
            _logDirectory = logDirectory;
            _baseFileName = baseFileName;
            _maxFileSizeBytes = maxFileSizeBytes;
            _maxRollingFiles = maxRollingFiles;

            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
                CleanStaleLogs(daysToRetain: 7);
            }
            catch { }
        }

        public void Log(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    string filePath = Path.Combine(_logDirectory, _baseFileName);
                    RotateIfNeeded(filePath);

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToUpperInvariant()}] {message}{Environment.NewLine}";
                    File.AppendAllText(filePath, line, Encoding.UTF8);
                }
                catch { }
            }
        }

        public void Info(string message) => Log("INFO", message);
        public void Warn(string message) => Log("WARN", message);
        public void Error(string message, Exception? ex = null) => Log("ERROR", ex != null ? $"{message} | {ex.Message}\n{ex.StackTrace}" : message);

        private void RotateIfNeeded(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                var fi = new FileInfo(filePath);
                if (fi.Length >= _maxFileSizeBytes)
                {
                    // Roll files: .3 is deleted, .2 -> .3, .1 -> .2, current -> .1
                    string lastFile = Path.Combine(_logDirectory, $"{_baseFileName}.{_maxRollingFiles}");
                    if (File.Exists(lastFile))
                    {
                        File.Delete(lastFile);
                    }

                    for (int i = _maxRollingFiles - 1; i >= 1; i--)
                    {
                        string src = Path.Combine(_logDirectory, $"{_baseFileName}.{i}");
                        string dst = Path.Combine(_logDirectory, $"{_baseFileName}.{i + 1}");
                        if (File.Exists(src))
                        {
                            File.Move(src, dst, true);
                        }
                    }

                    string firstBackup = Path.Combine(_logDirectory, $"{_baseFileName}.1");
                    File.Move(filePath, firstBackup, true);
                }
            }
            catch { }
        }

        public void CleanStaleLogs(int daysToRetain = 7)
        {
            try
            {
                var dir = new DirectoryInfo(_logDirectory);
                if (!dir.Exists) return;

                var cutoff = DateTime.UtcNow.AddDays(-daysToRetain);
                foreach (var file in dir.GetFiles("*.log*"))
                {
                    if (file.LastWriteTimeUtc < cutoff)
                    {
                        file.Delete();
                    }
                }
            }
            catch { }
        }
    }
}
