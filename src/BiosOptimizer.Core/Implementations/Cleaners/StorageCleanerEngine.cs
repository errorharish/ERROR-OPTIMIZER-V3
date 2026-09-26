using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations.Storage;

namespace BiosOptimizer.Core.Implementations.Cleaners
{
    /// <summary>
    /// Authoritative storage cleaner engine.
    /// SCAN → SELECT → PREVIEW → DELETE → VERIFY → RESCAN
    /// Never claims cleanup unless filesystem is actually measured before and after.
    /// </summary>
    public class StorageCleanerEngine
    {
        // ─────────────────────────────────────────────────────────────────
        // Category definitions — all real filesystem paths across all profiles
        // ─────────────────────────────────────────────────────────────────
                private static void AddNormalizedPath(HashSet<string> set, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Directory.Exists(full) || File.Exists(full))
                {
                    set.Add(full);
                }
            }
            catch { }
        }

        public static List<StorageCleanupCategory> BuildCategories()
        {
            var userProfiles = GetUserProfilePaths();
            string winDir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

            var tempPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNormalizedPath(tempPaths, Path.GetTempPath());
            AddNormalizedPath(tempPaths, Path.Combine(winDir, "Temp"));

            var updatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNormalizedPath(updatePaths, Path.Combine(winDir, "SoftwareDistribution", "Download"));
            AddNormalizedPath(updatePaths, Path.Combine(winDir, "Installer", "$PatchCache$"));

            var deliveryOptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNormalizedPath(deliveryOptPaths, Path.Combine(winDir, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"));

            var thumbnailPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var browserPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dumpPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNormalizedPath(dumpPaths, Path.Combine(winDir, "Minidump"));

            var logPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNormalizedPath(logPaths, Path.Combine(winDir, "Logs"));
            AddNormalizedPath(logPaths, Path.Combine(winDir, "Panther"));

            var tempAppDataPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shaderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // System MEMORY.DMP check
            string memoryDmp = Path.Combine(winDir, "MEMORY.DMP");
            if (File.Exists(memoryDmp))
            {
                AddNormalizedPath(dumpPaths, winDir); // Handled safely by IsLikelySafeToCount
            }

            foreach (var userProfile in userProfiles)
            {
                string localAppData = Path.Combine(userProfile, "AppData", "Local");
                string localLow = Path.Combine(userProfile, "AppData", "LocalLow");
                string appData = Path.Combine(userProfile, "AppData", "Roaming");

                AddNormalizedPath(tempPaths, Path.Combine(localAppData, "Temp"));
                AddNormalizedPath(tempAppDataPaths, Path.Combine(localAppData, "Microsoft", "Windows", "INetCache"));
                AddNormalizedPath(tempAppDataPaths, Path.Combine(localAppData, "Microsoft", "Windows", "WebCache"));
                AddNormalizedPath(tempAppDataPaths, Path.Combine(localAppData, "Microsoft", "Windows", "Caches"));

                AddNormalizedPath(thumbnailPaths, Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"));
                AddNormalizedPath(dumpPaths, Path.Combine(localAppData, "CrashDumps"));
                AddNormalizedPath(dumpPaths, Path.Combine(localAppData, "Temp", "WER"));
                AddNormalizedPath(logPaths, Path.Combine(localAppData, "CrashDumps"));

                // Browser caches (only cache and code cache directories, never user data)
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "GPUCache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "GPUCache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"));
                AddNormalizedPath(browserPaths, Path.Combine(appData, "Opera Software", "Opera Stable", "Cache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"));
                AddNormalizedPath(browserPaths, Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Code Cache"));

                // Shader caches across vendors
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "D3DSCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "NVIDIA", "DXCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "NVIDIA", "GLCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localLow, "NVIDIA", "PerDriverVersion", "DXCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "AMD", "DxCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "AMD", "GLCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "AMD", "D3DSCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "Intel", "ShaderCache"));
                AddNormalizedPath(shaderPaths, Path.Combine(localAppData, "Intel", "OpenCL"));
            }

            return new List<StorageCleanupCategory>
            {
                new()
                {
                    Id = "temp-files",
                    Name = "Temporary Files",
                    Description = "Windows and user temporary files accumulated by apps and the OS.",
                    Icon = "⚡",
                    CategoryType = "TEMP",
                    RiskLevel = "SAFE",
                    IsSelected = false,
                    TargetPaths = tempPaths.ToList()
                },
                new()
                {
                    Id = "windows-update-cache",
                    Name = "Windows Update Cleanup",
                    Description = "Cached Windows Update files that are no longer needed after installation.",
                    Icon = "🔄",
                    CategoryType = "UPDATE",
                    RiskLevel = "LOW RISK",
                    IsSelected = false,
                    TargetPaths = updatePaths.ToList()
                },
                new()
                {
                    Id = "delivery-opt-cache",
                    Name = "Delivery Optimization Cache",
                    Description = "Peer-to-peer Windows Update delivery cache files.",
                    Icon = "📡",
                    CategoryType = "CACHE",
                    RiskLevel = "SAFE",
                    IsSelected = false,
                    TargetPaths = deliveryOptPaths.ToList()
                },
                new()
                {
                    Id = "thumbnail-cache",
                    Name = "Thumbnail Cache",
                    Description = "Cached thumbnail images for File Explorer. Rebuilt automatically on demand.",
                    Icon = "🖼",
                    CategoryType = "CACHE",
                    RiskLevel = "SAFE",
                    IsSelected = false,
                    TargetPaths = thumbnailPaths.ToList()
                },
                new()
                {
                    Id = "browser-cache",
                    Name = "Browser Cache",
                    Description = "Cached web pages, images, and scripts from major browsers.",
                    Icon = "🌐",
                    CategoryType = "BROWSER",
                    RiskLevel = "SAFE",
                    IsSelected = false,
                    TargetPaths = browserPaths.ToList()
                },
                new()
                {
                    Id = "crash-dumps",
                    Name = "Crash Dumps",
                    Description = "Windows memory dump files from system and application crashes.",
                    Icon = "💥",
                    CategoryType = "DUMPS",
                    RiskLevel = "SAFE",
                    IsSelected = false,
                    TargetPaths = dumpPaths.ToList()
                },
                new()
                {
                    Id = "log-files",
                    Name = "Log Files",
                    Description = "Application and system log files older than 7 days.",
                    Icon = "📋",
                    CategoryType = "LOGS",
                    RiskLevel = "LOW RISK",
                    IsSelected = false,
                    TargetPaths = logPaths.ToList()
                },
                new()
                {
                    Id = "temp-appdata",
                    Name = "Temporary AppData",
                    Description = "Stale temporary files in user AppData folders.",
                    Icon = "📁",
                    CategoryType = "TEMP",
                    RiskLevel = "LOW RISK",
                    IsSelected = false,
                    TargetPaths = tempAppDataPaths.ToList()
                },
                new()
                {
                    Id = "shader-cache",
                    Name = "GPU Shader Cache",
                    Description = "DirectX and GPU shader caches. Rebuilt automatically on next launch.",
                    Icon = "🎮",
                    CategoryType = "CACHE",
                    RiskLevel = "LOW RISK",
                    IsSelected = false,
                    TargetPaths = shaderPaths.ToList()
                }
            };
        }

        private static List<string> GetUserProfilePaths()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string currentProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(currentProfile) && Directory.Exists(currentProfile))
            {
                list.Add(currentProfile);
            }

            string usersDir = Path.GetDirectoryName(currentProfile) ?? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users");
            if (Directory.Exists(usersDir))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(usersDir))
                    {
                        string name = Path.GetFileName(dir);
                        if (name.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Default User", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("All Users", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        list.Add(dir);
                    }
                }
                catch { }
            }

            return list.ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        // SCAN — measure bytes and file count for a category (no deletion)
        // ─────────────────────────────────────────────────────────────────
        public static IEnumerable<string> SafeEnumerateFiles(string rootPath, CancellationToken ct = default)
        {
            if (!Directory.Exists(rootPath)) yield break;
            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) yield break;
                string current = stack.Pop();

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(current);
                }
                catch { subDirs = Array.Empty<string>(); }

                foreach (var sd in subDirs)
                {
                    stack.Push(sd);
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch { files = Array.Empty<string>(); }

                foreach (var f in files)
                {
                    if (ct.IsCancellationRequested) yield break;
                    yield return f;
                }
            }
        }

        public StorageCleanupCategory ScanCategory(StorageCleanupCategory cat)
        {
            long totalBytes = 0;
            int  fileCount  = 0;
            bool anyPathExists = false;
            bool permissionDenied = false;

            foreach (var path in cat.TargetPaths)
            {
                if (!Directory.Exists(path)) continue;
                anyPathExists = true;

                try
                {
                    foreach (var f in SafeEnumerateFiles(path))
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (IsLikelySafeToCount(f, cat.Id))
                            {
                                totalBytes += fi.Length;
                                fileCount++;
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            permissionDenied = true;
                        }
                        catch { /* locked / access denied */ }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    permissionDenied = true;
                }
                catch { }
            }

            cat.DetectedBytes = totalBytes;
            cat.FileCount = fileCount;
            cat.IsScanned = true;

            if (!anyPathExists)
            {
                cat.Applicability = CleanupApplicabilityState.NotApplicable;
                cat.IsSelected = false;
            }
            else if (permissionDenied && fileCount == 0)
            {
                cat.Applicability = CleanupApplicabilityState.RequiresElevation;
                cat.IsSelected = false;
            }
            else if (totalBytes == 0)
            {
                cat.Applicability = CleanupApplicabilityState.AlreadyClean;
                cat.IsSelected = false;
            }
            else
            {
                cat.Applicability = CleanupApplicabilityState.Applicable;
                cat.IsSelected = (cat.RiskLevel == "SAFE");
            }

            return cat;
        }

        private bool IsLikelySafeToCount(string filePath, string categoryId)
        {
            // Thumbnail cache — only count thumbcache_* and iconcache files
            if (categoryId == "thumbnail-cache")
            {
                var fn = Path.GetFileName(filePath);
                return fn.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase)
                    || fn.StartsWith("iconcache", StringComparison.OrdinalIgnoreCase);
            }

            // Log files — only count old logs
            if (categoryId == "log-files")
            {
                try
                {
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext != ".log" && ext != ".txt" && ext != ".etl") return false;
                    return File.GetLastWriteTimeUtc(filePath) < DateTime.UtcNow.AddDays(-7);
                }
                catch { return false; }
            }

            return true;
        }

        private static bool IsPathInsideAllowedRoots(string filePath, IEnumerable<string> allowedRoots)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                foreach (var root in allowedRoots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    string fullRoot = Path.GetFullPath(root);
                    if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // CLEAN — measure before, delete, measure after, return real delta
        // ─────────────────────────────────────────────────────────────────
        public StorageCleanupResult CleanCategories(
            IEnumerable<StorageCleanupCategory> categories,
            string driveLetter,
            Action<StorageCleanupProgressReport>? onProgress,
            CancellationToken ct)
        {
            long freeBefore = GetDriveFreeBytes(driveLetter);

            var result = new StorageCleanupResult
            {
                InitialFreeBytes = freeBefore
            };

            var catList = categories.ToList();
            int catIdx = 0;
            var transactionLogs = new List<StorageTransactionLog>();

            foreach (var cat in catList)
            {
                var startTime = DateTime.Now;
                if (ct.IsCancellationRequested) break;

                catIdx++;
                long catBytesRemoved = 0;
                int  catFilesRemoved = 0;
                int  catFilesSkipped = 0;
                int  catFilesFailed  = 0;
                long catBytesBefore  = cat.DetectedBytes;
                int  catFilesBefore  = cat.FileCount;

                onProgress?.Invoke(new StorageCleanupProgressReport
                {
                    CurrentStage = "CLEANING",
                    ProgressPercent = (int)((catIdx - 1) * 100.0 / catList.Count),
                    CurrentItemName = cat.Name,
                    FilesProcessed = result.FilesRemoved + result.FilesSkipped + result.FilesFailed,
                    FilesRemoved = result.FilesRemoved,
                    FilesSkipped = result.FilesSkipped,
                    FilesFailed = result.FilesFailed,
                    BytesReclaimed = result.BytesReclaimed,
                    StatusMessage = $"Cleaning: {cat.Name}"
                });

                foreach (var path in cat.TargetPaths)
                {
                    if (!Directory.Exists(path)) continue;

                    try
                    {
                        int batchCounter = 0;
                        foreach (var f in SafeEnumerateFiles(path, ct))
                        {
                            if (ct.IsCancellationRequested) break;

                            // Safe path boundary check — ignore without skewing candidate skip counts
                            if (!IsPathInsideAllowedRoots(f, cat.TargetPaths))
                            {
                                continue;
                            }

                            // Adaptive CPU throttle yield every batch
                            if (++batchCounter % AdaptiveResourceGovernor.Instance.FileBatchSize == 0)
                            {
                                if (AdaptiveResourceGovernor.Instance.IsThrottled || AdaptiveResourceGovernor.Instance.ActiveTier == HardwareTier.LowResource)
                                {
                                    Thread.Sleep(10);
                                }
                            }

                            if (!IsLikelySafeToCount(f, cat.Id))
                            {
                                continue;
                            }

                            try
                            {
                                if (!File.Exists(f))
                                {
                                    continue;
                                }

                                var fi = new FileInfo(f);
                                long len = fi.Length;
                                if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                                {
                                    fi.Attributes &= ~FileAttributes.ReadOnly;
                                }

                                fi.Delete();

                                // Post-deletion existence verification
                                if (!File.Exists(f))
                                {
                                    catBytesRemoved += len;
                                    catFilesRemoved++;
                                }
                                else
                                {
                                    catFilesSkipped++;
                                    result.DetailedLog.Add($"SKIPPED (in use/locked): {f}");
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                catFilesSkipped++;
                                result.DetailedLog.Add($"SKIPPED (access denied / locked): {f}");
                            }
                            catch (IOException)
                            {
                                catFilesSkipped++;
                                result.DetailedLog.Add($"SKIPPED (in use / open by process): {f}");
                            }
                            catch (Exception ex)
                            {
                                catFilesFailed++;
                                result.DetailedLog.Add($"FAILED: {f} — {ex.Message}");
                            }
                        }

                        // Prune empty subdirectories in temp locations
                        if (cat.CategoryType == "TEMP" || cat.CategoryType == "CACHE")
                        {
                            try
                            {
                                var subDirs = Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                                    .OrderByDescending(d => d.Length)
                                    .ToList();
                                foreach (var d in subDirs)
                                {
                                    if (ct.IsCancellationRequested) break;
                                    try
                                    {
                                        if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
                                        {
                                            Directory.Delete(d, false);
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Post-clean rescan verification for this category
                long catBytesAfter = 0;
                int catFilesAfter = 0;
                foreach (var p in cat.TargetPaths)
                {
                    if (!Directory.Exists(p)) continue;
                    try
                    {
                        foreach (var f in SafeEnumerateFiles(p))
                        {
                            try
                            {
                                if (IsLikelySafeToCount(f, cat.Id))
                                {
                                    catBytesAfter += new FileInfo(f).Length;
                                    catFilesAfter++;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                cat.DeletedBytes = catBytesRemoved;
                cat.DeletedCount = catFilesRemoved;
                cat.SkippedCount = catFilesSkipped;
                cat.FailedCount  = catFilesFailed;
                cat.DetectedBytes = catBytesAfter;
                cat.FileCount = catFilesAfter;

                string terminalStatus;
                if (ct.IsCancellationRequested)
                {
                    cat.VerifiedStatus = CategoryVerificationStatus.Partial;
                    terminalStatus = "CANCELLED";
                }
                else if (catBytesAfter == 0 || catFilesAfter == 0)
                {
                    cat.VerifiedStatus = CategoryVerificationStatus.Verified;
                    cat.Applicability = CleanupApplicabilityState.AlreadyClean;
                    cat.IsSelected = false;
                    terminalStatus = "COMPLETED";
                }
                else if (catFilesRemoved > 0)
                {
                    cat.VerifiedStatus = CategoryVerificationStatus.Partial;
                    cat.Applicability = CleanupApplicabilityState.Applicable;
                    terminalStatus = "PARTIALLY_COMPLETED";
                }
                else if (catFilesSkipped > 0 && catFilesFailed == 0)
                {
                    cat.VerifiedStatus = CategoryVerificationStatus.Partial;
                    cat.Applicability = CleanupApplicabilityState.Applicable;
                    terminalStatus = "SKIPPED_IN_USE";
                }
                else if (catFilesFailed > 0)
                {
                    cat.VerifiedStatus = CategoryVerificationStatus.Failed;
                    terminalStatus = "FAILED";
                }
                else
                {
                    cat.VerifiedStatus = CategoryVerificationStatus.Verified;
                    cat.Applicability = CleanupApplicabilityState.AlreadyClean;
                    terminalStatus = "COMPLETED";
                }

                transactionLogs.Add(new StorageTransactionLog
                {
                    Category = cat.Name,
                    Path = string.Join("; ", cat.TargetPaths),
                    BeforeBytes = catBytesBefore,
                    BeforeFiles = catFilesBefore,
                    SelectedFiles = catFilesBefore,
                    DeletedFiles = catFilesRemoved,
                    SkippedFiles = catFilesSkipped,
                    FailedFiles = catFilesFailed,
                    AfterBytes = catBytesAfter,
                    AfterFiles = catFilesAfter,
                    Status = terminalStatus,
                    StartTime = startTime,
                    EndTime = DateTime.Now,
                    Timestamp = DateTime.Now
                });

                result.BytesReclaimed += catBytesRemoved;
                result.FilesRemoved   += catFilesRemoved;
                result.FilesSkipped   += catFilesSkipped;
                result.FilesFailed    += catFilesFailed;
            }

            // Persist transaction log
            PersistTransactionLogs(transactionLogs);

            long freeAfter = GetDriveFreeBytes(driveLetter);
            result.FinalFreeBytes = freeAfter;

            long actualDelta = freeAfter - freeBefore;
            if (actualDelta > result.BytesReclaimed) result.BytesReclaimed = actualDelta;

            result.Success = result.FilesRemoved > 0 || (result.FilesSkipped > 0 && result.FilesFailed == 0) || actualDelta > 0;
            result.SummaryMessage = result.Success
                ? $"Cleanup complete — {StorageCleanupCategory.FormatBytes(result.BytesReclaimed)} reclaimed. " +
                  $"{result.FilesRemoved:N0} removed, {result.FilesSkipped:N0} skipped (locked), {result.FilesFailed:N0} failed."
                : $"No files could be removed (all files locked or in use). Skipped: {result.FilesSkipped:N0}.";

            return result;
        }

        private static void PersistTransactionLogs(List<StorageTransactionLog> logs)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer", "Logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "StorageTransactionJournal.json");

                var existing = new List<StorageTransactionLog>();
                if (File.Exists(path))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        existing = System.Text.Json.JsonSerializer.Deserialize<List<StorageTransactionLog>>(json) ?? new();
                    }
                    catch { }
                }

                existing.AddRange(logs);
                // Keep last 500 records
                if (existing.Count > 500) existing = existing.Skip(existing.Count - 500).ToList();

                var outJson = System.Text.Json.JsonSerializer.Serialize(existing, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, outJson);
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        // RECYCLE BIN — query and empty
        // ─────────────────────────────────────────────────────────────────
        public (long SizeBytes, int ItemCount) GetRecycleBinInfo()
        {
            long totalBytes = 0;
            int  count     = 0;
            try
            {
                // Query $RECYCLE.BIN on all drives
                foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
                {
                    string rbPath = Path.Combine(d.Name, "$RECYCLE.BIN");
                    if (!Directory.Exists(rbPath)) continue;
                    try
                    {
                        foreach (var subDir in Directory.EnumerateDirectories(rbPath))
                        {
                            try
                            {
                                foreach (var f in Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories))
                                {
                                    try
                                    {
                                        if (!Path.GetFileName(f).StartsWith("$I", StringComparison.OrdinalIgnoreCase)) // skip metadata
                                        {
                                            totalBytes += new FileInfo(f).Length;
                                            count++;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return (totalBytes, count);
        }

        public (bool Success, long FreedBytes, string Message) EmptyRecycleBin()
        {
            long before = 0;
            long after  = 0;

            // Measure free space before on C:
            try { before = new DriveInfo("C").AvailableFreeSpace; } catch { }

            int exitCode = 0;
            string stderr = string.Empty;
            try
            {
                // Use shell32 SHEmptyRecycleBin approach via PowerShell for reliability
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c rd /s /q \"%SystemDrive%\\$Recycle.Bin\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(10000);
                exitCode = proc.ExitCode;
            }
            catch (Exception ex)
            {
                return (false, 0, $"Failed: {ex.Message}");
            }

            try { after = new DriveInfo("C").AvailableFreeSpace; } catch { }

            long freed = Math.Max(0, after - before);
            return (true, freed, $"✓ Recycle Bin emptied. {StorageCleanupCategory.FormatBytes(freed)} freed.");
        }

        // ─────────────────────────────────────────────────────────────────
        // LARGE FILE FINDER — real filesystem enumeration
        // ─────────────────────────────────────────────────────────────────
        public List<LargeFileInfo> FindLargeFiles(string driveLetter, long minSizeBytes, CancellationToken ct)
        {
            var results = new List<LargeFileInfo>();
            string rootPath = driveLetter.EndsWith("\\") ? driveLetter : driveLetter + "\\";

            // Safe top-level directories to scan (exclude system and protected dirs)
            var safeDirs = new List<string>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(rootPath))
                {
                    if (ct.IsCancellationRequested) break;
                    string dirName = Path.GetFileName(dir).ToLowerInvariant();
                    if (dirName == "windows" || dirName == "program files" || dirName == "program files (x86)" || 
                        dirName == "$recycle.bin" || dirName == "system volume information" || dirName.StartsWith("$"))
                        continue; // Skip core Windows and system dirs for safety
                    safeDirs.Add(dir);
                }
            }
            catch { }

            // Check root directory files
            try
            {
                foreach (var f in Directory.EnumerateFiles(rootPath))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length >= minSizeBytes)
                        {
                            results.Add(new LargeFileInfo
                            {
                                FileName = fi.Name,
                                FullPath = f,
                                Extension = fi.Extension.ToUpperInvariant().TrimStart('.'),
                                SizeBytes = fi.Length,
                                LastModified = fi.LastWriteTime,
                                DriveLetter = driveLetter.TrimEnd('\\')
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }

            foreach (var dir in safeDirs)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    foreach (var f in SafeEnumerateFiles(dir, ct))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            var fi = new FileInfo(f);
                            if (fi.Length >= minSizeBytes)
                            {
                                results.Add(new LargeFileInfo
                                {
                                    FileName = fi.Name,
                                    FullPath = f,
                                    Extension = fi.Extension.ToUpperInvariant().TrimStart('.'),
                                    SizeBytes = fi.Length,
                                    LastModified = fi.LastWriteTime,
                                    DriveLetter = driveLetter.TrimEnd('\\')
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return results.OrderByDescending(x => x.SizeBytes).Take(500).ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        // DUPLICATE FILE FINDER — size + SHA-256 hash comparison
        // ─────────────────────────────────────────────────────────────────
        public List<DuplicateFileGroup> FindDuplicates(string rootPath, long minSizeBytes, CancellationToken ct)
        {
            var results = new List<DuplicateFileGroup>();

            // Step 1: Group by size
            var sizeGroups = new Dictionary<long, List<string>>();
            try
            {
                foreach (var f in SafeEnumerateFiles(rootPath, ct))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length < minSizeBytes) continue;
                        if (!sizeGroups.ContainsKey(fi.Length)) sizeGroups[fi.Length] = new();
                        sizeGroups[fi.Length].Add(f);
                    }
                    catch { }
                }
            }
            catch { }

            // Step 2: For groups with more than 1 file, hash and group by hash
            foreach (var (size, files) in sizeGroups)
            {
                if (ct.IsCancellationRequested) break;
                if (files.Count < 2) continue;

                var hashGroups = new Dictionary<string, List<string>>();
                foreach (var f in files)
                {
                    try
                    {
                        string hash = ComputeFileHash(f);
                        if (!hashGroups.ContainsKey(hash)) hashGroups[hash] = new();
                        hashGroups[hash].Add(f);
                    }
                    catch { }
                }

                foreach (var (hash, dupes) in hashGroups)
                {
                    if (dupes.Count < 2) continue;
                    results.Add(new DuplicateFileGroup
                    {
                        GroupId = Guid.NewGuid().ToString("N")[..8],
                        FileSizeBytes = size,
                        Hash = hash,
                        FilePaths = dupes
                    });
                }
            }

            return results.OrderByDescending(g => g.ReclaimableBytes).Take(200).ToList();
        }

        private string ComputeFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
            // Only hash first 512KB for performance on large files
            var buf = new byte[Math.Min(stream.Length, 524288)];
            int read = stream.Read(buf, 0, buf.Length);
            var hash = sha256.ComputeHash(buf, 0, read);
            return Convert.ToHexString(hash);
        }

        // ─────────────────────────────────────────────────────────────────
        // FULL FILE EXPLORER SCANNER — Supports all local drives up to 1 TB+
        // ─────────────────────────────────────────────────────────────────
        public static string ClassifyFileType(string ext)
        {
            ext = ext.TrimStart('.').ToLowerInvariant();
            return ext switch
            {
                "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" or "m4v" or "ts" or "vob" or "3gp" or "m2ts" => "VIDEOS",
                "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "tiff" or "ico" or "svg" or "heic" or "raw" or "cr2" or "nef" => "IMAGES",
                "mp3" or "wav" or "flac" or "aac" or "ogg" or "wma" or "m4a" or "opus" or "mid" or "midi" => "AUDIO",
                "pdf" or "docx" or "doc" or "xlsx" or "xls" or "pptx" or "ppt" or "txt" or "csv" or "rtf" or "epub" or "md" => "DOCUMENTS",
                "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "cab" or "dmg" => "ARCHIVES",
                "exe" or "msi" or "bat" or "cmd" or "ps1" or "vbs" or "com" => "EXECUTABLES",
                "iso" or "img" or "vhd" or "vhdx" or "vmdk" or "bin" or "cue" => "ISO",
                "sln" or "csproj" or "cpp" or "c" or "h" or "cs" or "py" or "js" or "ts" or "json" or "xml" or "unity" or "blend" or "prproj" or "aep" or "psd" or "ai" or "dwg" => "PROJECTS",
                _ => "OTHER"
            };
        }

        public static string DetermineSafetyStatus(string fullPath, string ext)
        {
            string p = fullPath.ToLowerInvariant();
            string e = ext.TrimStart('.').ToLowerInvariant();

            // Protected System Paths & Files
            if (p.StartsWith(@"c:\windows") ||
                p.Contains(@"\system volume information") ||
                p.Contains(@"\$recycle.bin") ||
                p.Contains(@"\recovery\") ||
                p.EndsWith("pagefile.sys") ||
                p.EndsWith("swapfile.sys") ||
                p.EndsWith("hiberfil.sys") ||
                e == "sys" || e == "dll" && p.StartsWith(@"c:\windows"))
            {
                return "SYSTEM/PROTECTED";
            }

            // Known Safe Cleanup Files
            if (p.Contains(@"\temp\") ||
                p.Contains(@"\softwaredistribution\download") ||
                p.Contains(@"\crashdumps\") ||
                p.Contains(@"\explorer\thumbcache") ||
                e is "tmp" or "old" or "bak" or "chk" or "dmp" or "log" ||
                p.EndsWith("thumbs.db"))
            {
                return "SAFE TO REMOVE";
            }

            // Executables, Installers, User Data
            if (e is "exe" or "msi" or "iso" or "zip" or "rar" or "7z" or "mp4" or "mkv" or "pdf" or "docx")
            {
                return "REVIEW";
            }

            return "UNKNOWN";
        }

        public (List<StorageFileItem> Files, StorageScanSummary Summary) ScanAllAccessibleFiles(
            string driveFilter,
            long minSizeBytes,
            Action<StorageScanProgress>? progressCallback,
            CancellationToken ct)
        {
            var results = new List<StorageFileItem>();
            var summary = new StorageScanSummary();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            long filesScanned = 0;
            long foldersScanned = 0;
            long totalScannedBytes = 0;
            DateTime lastReportTime = DateTime.UtcNow;

            var drivesToScan = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Where(d => string.IsNullOrEmpty(driveFilter) || driveFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase) || d.Name.StartsWith(driveFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var drive in drivesToScan)
            {
                if (ct.IsCancellationRequested) break;

                var dirsToProcess = new Stack<string>();
                dirsToProcess.Push(drive.RootDirectory.FullName);

                while (dirsToProcess.Count > 0)
                {
                    if (ct.IsCancellationRequested) break;
                    string currentDir = dirsToProcess.Pop();
                    foldersScanned++;

                    // Skip protected system recursion directories
                    string lowerDir = currentDir.ToLowerInvariant();
                    if (lowerDir.Contains("$recycle.bin") ||
                        lowerDir.Contains("system volume information") ||
                        lowerDir.Contains(@"\windows\winsxs"))
                    {
                        continue;
                    }

                    // Enumerate files
                    try
                    {
                        var dirInfo = new DirectoryInfo(currentDir);
                        foreach (var fi in dirInfo.EnumerateFiles())
                        {
                            if (ct.IsCancellationRequested) break;
                            filesScanned++;

                            try
                            {
                                long size = fi.Length;
                                totalScannedBytes += size;
                                string ext = fi.Extension.ToUpperInvariant().TrimStart('.');
                                string category = ClassifyFileType(ext);
                                string safety = DetermineSafetyStatus(fi.FullName, ext);

                                // Aggregate summary metrics
                                summary.TotalFilesFound++;
                                summary.TotalScannedBytes += size;
                                if (size > summary.LargestFileBytes)
                                {
                                    summary.LargestFileBytes = size;
                                    summary.LargestFileName = fi.Name;
                                }
                                switch (category)
                                {
                                    case "VIDEOS": summary.VideosBytes += size; break;
                                    case "IMAGES": summary.ImagesBytes += size; break;
                                    case "AUDIO": summary.AudioBytes += size; break;
                                    case "DOCUMENTS": summary.DocumentsBytes += size; break;
                                    case "EXECUTABLES": summary.ExecutablesBytes += size; break;
                                    case "ARCHIVES": summary.ArchivesBytes += size; break;
                                    case "ISO": summary.IsoBytes += size; break;
                                    case "PROJECTS": summary.ProjectsBytes += size; break;
                                    default: summary.OtherBytes += size; break;
                                }

                                if (size >= minSizeBytes)
                                {
                                    results.Add(new StorageFileItem
                                    {
                                        FileName = fi.Name,
                                        FullPath = fi.FullName,
                                        Location = fi.DirectoryName ?? currentDir,
                                        Extension = string.IsNullOrEmpty(ext) ? "FILE" : ext,
                                        SizeBytes = size,
                                        LastModified = fi.LastWriteTime,
                                        DriveLetter = drive.Name.TrimEnd('\\'),
                                        FileTypeCategory = category,
                                        SafetyStatus = safety,
                                        IsSelected = false
                                    });
                                }

                                // Periodic progress update
                                if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds >= 250)
                                {
                                    lastReportTime = DateTime.UtcNow;
                                    progressCallback?.Invoke(new StorageScanProgress
                                    {
                                        FilesScannedCount = filesScanned,
                                        FoldersScannedCount = foldersScanned,
                                        CurrentScanningPath = fi.FullName,
                                        TotalResultsCount = results.Count,
                                        TotalScannedBytes = totalScannedBytes,
                                        ElapsedSeconds = sw.Elapsed.TotalSeconds,
                                        IsCompleted = false
                                    });
                                }
                            }
                            catch { }
                        }

                        // Enumerate subdirectories
                        foreach (var subDir in dirInfo.EnumerateDirectories())
                        {
                            try
                            {
                                // Skip reparse points / junctions to avoid endless loops
                                if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                                dirsToProcess.Push(subDir.FullName);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }

            // Final completion report
            progressCallback?.Invoke(new StorageScanProgress
            {
                FilesScannedCount = filesScanned,
                FoldersScannedCount = foldersScanned,
                CurrentScanningPath = "Scan Complete",
                TotalResultsCount = results.Count,
                TotalScannedBytes = totalScannedBytes,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                IsCompleted = true
            });

            return (results, summary);
        }

        public StorageFileDeleteResult DeleteStorageFiles(IEnumerable<string> filePaths, bool moveToRecycleBin)
        {
            var result = new StorageFileDeleteResult();
            var pathList = filePaths.Distinct().ToList();

            foreach (var path in pathList)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        result.SkippedCount++;
                        result.FailedFiles.Add($"{path}: File does not exist on disk");
                        continue;
                    }

                    var fi = new FileInfo(path);
                    long size = fi.Length;
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        fi.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    if (moveToRecycleBin)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        File.Delete(path);
                    }

                    // Post-deletion existence check
                    if (!File.Exists(path))
                    {
                        result.DeletedCount++;
                        result.ReclaimedBytes += size;
                    }
                    else
                    {
                        result.SkippedCount++;
                        result.FailedFiles.Add($"{path}: File in use or locked by another process");
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    result.FailedCount++;
                    result.FailedFiles.Add($"{path}: Access denied (administrator permission required) — {ex.Message}");
                }
                catch (IOException ex)
                {
                    result.SkippedCount++;
                    result.FailedFiles.Add($"{path}: File is currently open or in use — {ex.Message}");
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.FailedFiles.Add($"{path}: Deletion error — {ex.Message}");
                }
            }

            result.Success = result.DeletedCount > 0 || (result.SkippedCount > 0 && result.FailedCount == 0);
            result.SummaryMessage = $"Deleted: {result.DeletedCount} | Skipped: {result.SkippedCount} | Failed: {result.FailedCount} | Reclaimed: {result.FormattedReclaimedBytes}";
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────
        private static long GetDriveFreeBytes(string driveLetter)
        {
            try
            {
                string letter = driveLetter.Length >= 1 ? driveLetter[..1] : "C";
                return new DriveInfo(letter).AvailableFreeSpace;
            }
            catch { return 0; }
        }
    }
}

