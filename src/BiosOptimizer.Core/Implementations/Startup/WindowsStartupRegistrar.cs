#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using BiosOptimizer.Core.Implementations.Diagnostics;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.Startup
{
    [SupportedOSPlatform("windows")]
    public class WindowsStartupRegistrar
    {
        private static readonly Lazy<WindowsStartupRegistrar> _instance = new(() => new WindowsStartupRegistrar());
        public static WindowsStartupRegistrar Instance => _instance.Value;

        public const string AuthoritativeEntryName = "Error Optimizer";
        private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunOnceRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
        private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string StartupApprovedRun32KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";

        private static readonly string[] LegacyDuplicateNames = new[]
        {
            "Error Optimizer Startup",
            "Error Optimizer Startup 2",
            "Error Optimizer (1)",
            "ErrorOptimizer",
            "ErrorOptimizer.exe",
            "Error Optimizer V3",
            "ErrorOptimizerV3",
            "BiosOptimizer",
            "Bios Optimizer",
            "BiosOptimizer.exe",
            "BiosOptimizer.GUI",
            "BiosOptimizer.GUI.exe"
        };

        public static bool IsRunningAsSystem()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return identity.IsSystem;
            }
            catch
            {
                return false;
            }
        }

        public static List<string> GetInteractiveUserSids()
        {
            var sids = new List<string>();
            try
            {
                using var usersKey = Registry.Users;
                foreach (var subKeyName in usersKey.GetSubKeyNames())
                {
                    if (subKeyName.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !subKeyName.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        sids.Add(subKeyName);
                    }
                }
            }
            catch { }
            return sids;
        }

        public static bool CheckPolicyBlocking(out string reason)
        {
            reason = "";
            try
            {
                // Check HKCU Policy
                using (var polKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", false))
                {
                    if (polKey != null)
                    {
                        var disUser = polKey.GetValue("DisableCurrentUserRun");
                        if (disUser is int i && i == 1)
                        {
                            reason = "Group Policy 'DisableCurrentUserRun' is actively enforced under HKCU.";
                            return true;
                        }
                    }
                }

                // Check HKLM Policy
                using (var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var polKey = hklm64.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", false))
                {
                    if (polKey != null)
                    {
                        var disUser = polKey.GetValue("DisableCurrentUserRun");
                        if (disUser is int i && i == 1)
                        {
                            reason = "Group Policy 'DisableCurrentUserRun' is actively enforced under HKLM.";
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public WindowsStartupInfo GetStartupInfo()
        {
            // Reconcile and clean any stale/duplicate registrations first
            ReconcileAndCleanAllDuplicates();

            var info = new WindowsStartupInfo
            {
                EntryName = AuthoritativeEntryName,
                SourceKey = @"HKCU\" + RunRegistryKeyPath
            };

            // Check Policy Block first
            if (CheckPolicyBlocking(out string policyReason))
            {
                info.Status = WindowsStartupRegistrationStatus.BlockedByPolicy;
                info.IsEnabled = false;
                info.DetailMessage = policyReason;
                StartupSelfDiagnosticTracker.Instance.RecordRegistrationState(false, info.DisplayStatus, isBlocked: true, blockedReason: policyReason);
                return info;
            }

            try
            {
                // Attempt read from CurrentUser first
                bool found = TryReadStartupFromRoot(Registry.CurrentUser, info);

                // If not found in CurrentUser and running as SYSTEM or elevated, attempt read from interactive user SID
                if (!found && IsRunningAsSystem())
                {
                    foreach (var sid in GetInteractiveUserSids())
                    {
                        using var userRoot = Registry.Users.OpenSubKey(sid, false);
                        if (userRoot != null && TryReadStartupFromRoot(userRoot, info))
                        {
                            info.SourceKey = $@"HKEY_USERS\{sid}\" + RunRegistryKeyPath;
                            break;
                        }
                    }
                }

                StartupSelfDiagnosticTracker.Instance.RecordRegistrationState(
                    info.IsEnabled,
                    info.DisplayStatus,
                    isBlocked: info.Status == WindowsStartupRegistrationStatus.BlockedByPolicy,
                    blockedReason: info.Status == WindowsStartupRegistrationStatus.BlockedByPolicy ? info.DetailMessage : "");

                return info;
            }
            catch (UnauthorizedAccessException ex)
            {
                info.Status = WindowsStartupRegistrationStatus.AccessDenied;
                info.DetailMessage = $"Access denied to registry: {ex.Message}";
                return info;
            }
            catch (Exception ex)
            {
                info.Status = WindowsStartupRegistrationStatus.NotConfigured;
                info.DetailMessage = $"Registry read error: {ex.Message}";
                return info;
            }
        }

        private static bool TryReadStartupFromRoot(RegistryKey rootKey, WindowsStartupInfo info)
        {
            using var runKey = rootKey.OpenSubKey(RunRegistryKeyPath, false);
            if (runKey == null) return false;

            var val = runKey.GetValue(AuthoritativeEntryName);
            if (val == null)
            {
                info.Status = WindowsStartupRegistrationStatus.DisabledByUser;
                info.IsEnabled = false;
                info.DetailMessage = "Startup entry is not registered.";
                return false;
            }

            string commandLine = val.ToString() ?? "";
            string exePath = ExtractExePath(commandLine);
            string args = ExtractArgs(commandLine);

            info.ExePath = exePath;
            info.Arguments = args;

            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                info.Status = WindowsStartupRegistrationStatus.CorruptedOrMissingExe;
                info.IsEnabled = false;
                info.DetailMessage = $"Registered path '{exePath}' does not exist on disk.";
                return true;
            }

            // Check Task Manager StartupApproved state
            using var approvedKey = rootKey.OpenSubKey(StartupApprovedRunKeyPath, false);
            if (approvedKey != null)
            {
                var approvedVal = approvedKey.GetValue(AuthoritativeEntryName) as byte[];
                if (approvedVal != null && approvedVal.Length > 0)
                {
                    if (approvedVal[0] == 0x03 || approvedVal[0] == 0x01 || (approvedVal[0] & 1) != 0)
                    {
                        info.Status = WindowsStartupRegistrationStatus.ExternalChangeDetectedDisabled;
                        info.IsEnabled = false;
                        info.DetailMessage = "Application startup was disabled externally by user via Windows Task Manager.";
                        return true;
                    }
                }
            }

            info.Status = WindowsStartupRegistrationStatus.VerifiedEnabled;
            info.IsEnabled = true;
            info.DetailMessage = "Real Windows startup entry is registered, verified, and enabled.";
            return true;
        }

        public bool ConfigureStartup(bool enable, bool startMinimized = false)
        {
            try
            {
                ReconcileAndCleanAllDuplicates();

                bool cuSuccess = false;
                bool anySidSuccess = false;

                if (IsRunningAsSystem())
                {
                    // Running under SYSTEM (e.g. background service or elevated installer): register into interactive user hive(s)
                    var sids = GetInteractiveUserSids();
                    foreach (var sid in sids)
                    {
                        try
                        {
                            using var userRoot = Registry.Users.OpenSubKey(sid, true);
                            if (userRoot != null)
                            {
                                if (ConfigureUserHive(userRoot, enable, startMinimized))
                                {
                                    anySidSuccess = true;
                                }
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    // Running under interactive user session: configure CurrentUser directly
                    cuSuccess = ConfigureUserHive(Registry.CurrentUser, enable, startMinimized);
                }

                bool finalSuccess = cuSuccess || anySidSuccess;

                var info = GetStartupInfo();
                StartupSelfDiagnosticTracker.Instance.RecordRegistrationState(
                    info.IsEnabled,
                    info.DisplayStatus,
                    isBlocked: info.Status == WindowsStartupRegistrationStatus.BlockedByPolicy,
                    blockedReason: info.Status == WindowsStartupRegistrationStatus.BlockedByPolicy ? info.DetailMessage : "");

                return finalSuccess;
            }
            catch
            {
                return false;
            }
        }

        private static bool ConfigureUserHive(RegistryKey rootKey, bool enable, bool startMinimized)
        {
            try
            {
                using var runKey = rootKey.CreateSubKey(RunRegistryKeyPath, true);
                if (runKey == null) return false;

                if (!enable)
                {
                    // Remove from Run key
                    if (runKey.GetValue(AuthoritativeEntryName) != null)
                    {
                        runKey.DeleteValue(AuthoritativeEntryName, false);
                    }

                    // Mark disabled in StartupApproved
                    using var approvedKey = rootKey.CreateSubKey(StartupApprovedRunKeyPath, true);
                    if (approvedKey != null)
                    {
                        byte[] disabledVal = { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(AuthoritativeEntryName, disabledVal, RegistryValueKind.Binary);
                    }

                    return true;
                }

                // Determine current installed/running executable path
                string exePath = ResolveInstalledOrLocalExePath();
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    return false;
                }

                // Format command line with quotes and arguments
                string commandLine = $"\"{exePath}\"";
                if (startMinimized)
                {
                    commandLine += " --startup-background";
                }
                else
                {
                    commandLine += " --startup";
                }

                runKey.SetValue(AuthoritativeEntryName, commandLine, RegistryValueKind.String);

                // Update StartupApproved to Enabled (0x02)
                using var appKey = rootKey.CreateSubKey(StartupApprovedRunKeyPath, true);
                if (appKey != null)
                {
                    byte[] enabledVal = { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    appKey.SetValue(AuthoritativeEntryName, enabledVal, RegistryValueKind.Binary);
                }

                // Read-back verification
                var readVal = runKey.GetValue(AuthoritativeEntryName)?.ToString();
                return string.Equals(readVal, commandLine, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string ResolveInstalledOrLocalExePath()
        {
            try
            {
                // 1. Check HKLM 64-bit Registry Install Location from Inno Setup
                using (var hklmBase64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var hklmKey = hklmBase64.OpenSubKey(@"Software\Error Optimizer V3", false) ??
                                         hklmBase64.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Error Optimizer V3_is1", false))
                    {
                        if (hklmKey != null)
                        {
                            string? installLoc = (hklmKey.GetValue("InstallLocation") ?? hklmKey.GetValue("Inno Setup: App Path")) as string;
                            if (!string.IsNullOrWhiteSpace(installLoc))
                            {
                                string targetExe = Path.Combine(installLoc, "ErrorOptimizer.exe");
                                if (File.Exists(targetExe))
                                {
                                    return targetExe;
                                }
                            }
                        }
                    }
                }

                // 2. Check standard 64-bit Program Files installation path
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pfExe = Path.Combine(progFiles, "Error Optimizer V3", "ErrorOptimizer.exe");
                if (File.Exists(pfExe))
                {
                    return pfExe;
                }

                string pfAltExe = Path.Combine(progFiles, "Error Optimizer", "ErrorOptimizer.exe");
                if (File.Exists(pfAltExe))
                {
                    return pfAltExe;
                }

                // 3. Check 32-bit Program Files fallback
                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrWhiteSpace(progFilesX86))
                {
                    string pf86Exe = Path.Combine(progFilesX86, "Error Optimizer V3", "ErrorOptimizer.exe");
                    if (File.Exists(pf86Exe))
                    {
                        return pf86Exe;
                    }
                }

                // 4. Check Per-User Local AppData Programs fallback
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    string localUserExe = Path.Combine(localAppData, "Programs", "Error Optimizer V3", "ErrorOptimizer.exe");
                    if (File.Exists(localUserExe))
                    {
                        return localUserExe;
                    }
                }

                // 5. Current executing process path
                string currentPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrWhiteSpace(currentPath) && 
                    Path.GetFileName(currentPath).Equals("ErrorOptimizer.exe", StringComparison.OrdinalIgnoreCase) && 
                    File.Exists(currentPath))
                {
                    return currentPath;
                }

                // 6. Base directory of current AppDomain
                string baseDirExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ErrorOptimizer.exe");
                if (File.Exists(baseDirExe))
                {
                    return baseDirExe;
                }

                if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                {
                    return currentPath;
                }
            }
            catch { }

            return "";
        }

        public void ReconcileAndCleanAllDuplicates()
        {
            CleanupDuplicateEntries();
            CleanupHklmRunEntries();
            CleanupStartupFolders();
            CleanupLegacyScheduledTasks();
        }

        public void CleanupDuplicateEntries()
        {
            try
            {
                // 1. Clean HKCU Run
                CleanUserHiveDuplicates(Registry.CurrentUser);

                // 2. If running as SYSTEM or elevated, clean across all interactive users
                if (IsRunningAsSystem())
                {
                    foreach (var sid in GetInteractiveUserSids())
                    {
                        try
                        {
                            using var userRoot = Registry.Users.OpenSubKey(sid, true);
                            if (userRoot != null)
                            {
                                CleanUserHiveDuplicates(userRoot);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void CleanUserHiveDuplicates(RegistryKey rootKey)
        {
            try
            {
                // 1. Clean Run Key
                using var runKey = rootKey.CreateSubKey(RunRegistryKeyPath, true);
                if (runKey != null)
                {
                    var valueNames = runKey.GetValueNames();
                    foreach (var name in valueNames)
                    {
                        if (string.Equals(name, AuthoritativeEntryName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Keep authoritative entry
                        }

                        bool isLegacyName = LegacyDuplicateNames.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
                        string? valStr = runKey.GetValue(name)?.ToString() ?? "";
                        bool pointsToUs = valStr.Contains("ErrorOptimizer", StringComparison.OrdinalIgnoreCase) ||
                                          valStr.Contains("BiosOptimizer", StringComparison.OrdinalIgnoreCase);

                        if (isLegacyName || pointsToUs)
                        {
                            try { runKey.DeleteValue(name, false); } catch { }
                        }
                    }
                }

                // 2. Clean StartupApproved\Run
                using var approvedKey = rootKey.CreateSubKey(StartupApprovedRunKeyPath, true);
                if (approvedKey != null)
                {
                    var approvedNames = approvedKey.GetValueNames();
                    foreach (var name in approvedNames)
                    {
                        if (string.Equals(name, AuthoritativeEntryName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (LegacyDuplicateNames.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)))
                        {
                            try { approvedKey.DeleteValue(name, false); } catch { }
                        }
                    }
                }

                // 3. Clean RunOnce
                using var runOnceKey = rootKey.CreateSubKey(RunOnceRegistryKeyPath, true);
                if (runOnceKey != null)
                {
                    foreach (var name in runOnceKey.GetValueNames())
                    {
                        string? valStr = runOnceKey.GetValue(name)?.ToString() ?? "";
                        if (LegacyDuplicateNames.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)) ||
                            valStr.Contains("ErrorOptimizer", StringComparison.OrdinalIgnoreCase) ||
                            valStr.Contains("BiosOptimizer", StringComparison.OrdinalIgnoreCase))
                        {
                            try { runOnceKey.DeleteValue(name, false); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void CleanupHklmRunEntries()
        {
            try
            {
                // Clean HKLM Run (64-bit)
                using (var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var hklmRun = hklm64.OpenSubKey(RunRegistryKeyPath, true))
                    {
                        if (hklmRun != null)
                        {
                            foreach (var name in hklmRun.GetValueNames())
                            {
                                string? valStr = hklmRun.GetValue(name)?.ToString() ?? "";
                                if (string.Equals(name, AuthoritativeEntryName, StringComparison.OrdinalIgnoreCase) ||
                                    LegacyDuplicateNames.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)) ||
                                    valStr.Contains("ErrorOptimizer", StringComparison.OrdinalIgnoreCase) ||
                                    valStr.Contains("BiosOptimizer", StringComparison.OrdinalIgnoreCase))
                                {
                                    try { hklmRun.DeleteValue(name, false); } catch { }
                                }
                            }
                        }
                    }

                    // Clean HKLM StartupApproved\Run
                    using (var hklmApproved = hklm64.OpenSubKey(StartupApprovedRunKeyPath, true))
                    {
                        if (hklmApproved != null)
                        {
                            foreach (var name in hklmApproved.GetValueNames())
                            {
                                if (string.Equals(name, AuthoritativeEntryName, StringComparison.OrdinalIgnoreCase) ||
                                    LegacyDuplicateNames.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)))
                                {
                                    try { hklmApproved.DeleteValue(name, false); } catch { }
                                }
                            }
                        }
                    }
                }

                // Clean HKLM Run (32-bit WOW6432Node)
                using (var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                {
                    using (var hklmRun32 = hklm32.OpenSubKey(RunRegistryKeyPath, true))
                    {
                        if (hklmRun32 != null)
                        {
                            foreach (var name in hklmRun32.GetValueNames())
                            {
                                string? valStr = hklmRun32.GetValue(name)?.ToString() ?? "";
                                if (string.Equals(name, AuthoritativeEntryName, StringComparison.OrdinalIgnoreCase) ||
                                    LegacyDuplicateNames.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)) ||
                                    valStr.Contains("ErrorOptimizer", StringComparison.OrdinalIgnoreCase) ||
                                    valStr.Contains("BiosOptimizer", StringComparison.OrdinalIgnoreCase))
                                {
                                    try { hklmRun32.DeleteValue(name, false); } catch { }
                                }
                            }
                        }
                    }

                    using (var hklmApp32 = hklm32.OpenSubKey(StartupApprovedRun32KeyPath, true))
                    {
                        if (hklmApp32 != null)
                        {
                            foreach (var name in hklmApp32.GetValueNames())
                            {
                                if (string.Equals(name, AuthoritativeEntryName, StringComparison.OrdinalIgnoreCase) ||
                                    LegacyDuplicateNames.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)))
                                {
                                    try { hklmApp32.DeleteValue(name, false); } catch { }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void CleanupStartupFolders()
        {
            try
            {
                // 1. User Startup folder
                string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(userStartup))
                {
                    var lnkFiles = Directory.GetFiles(userStartup, "*.lnk", SearchOption.TopDirectoryOnly);
                    foreach (var file in lnkFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.Contains("Error Optimizer", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains("ErrorOptimizer", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains("BiosOptimizer", StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }

                // 2. Common Startup folder (All Users)
                string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                if (Directory.Exists(commonStartup))
                {
                    var commonLnkFiles = Directory.GetFiles(commonStartup, "*.lnk", SearchOption.TopDirectoryOnly);
                    foreach (var file in commonLnkFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.Contains("Error Optimizer", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains("ErrorOptimizer", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains("BiosOptimizer", StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static void CleanupLegacyScheduledTasks()
        {
            try
            {
                var legacyTasks = new[]
                {
                    @"\ErrorOptimizer\StartupTask",
                    "ErrorOptimizerStartup",
                    "BiosOptimizerStartup"
                };

                foreach (var task in legacyTasks)
                {
                    try
                    {
                        using var p = new Process();
                        p.StartInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/delete /tn \"{task}\" /f",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        p.Start();
                        p.WaitForExit(1000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string ExtractExePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return "";
            string clean = commandLine.Trim();

            if (clean.StartsWith("\""))
            {
                int nextQuote = clean.IndexOf('"', 1);
                if (nextQuote > 1)
                {
                    clean = clean.Substring(1, nextQuote - 1);
                }
            }
            else
            {
                int exeIdx = clean.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx > 0)
                {
                    clean = clean.Substring(0, exeIdx + 4);
                }
                else
                {
                    int spaceIdx = clean.IndexOf(' ');
                    if (spaceIdx > 0)
                    {
                        clean = clean.Substring(0, spaceIdx);
                    }
                }
            }

            try
            {
                clean = Environment.ExpandEnvironmentVariables(clean).Trim();
            }
            catch { }

            return clean;
        }

        private static string ExtractArgs(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return "";
            string clean = commandLine.Trim();

            if (clean.StartsWith("\""))
            {
                int nextQuote = clean.IndexOf('"', 1);
                if (nextQuote > 1 && nextQuote < clean.Length - 1)
                {
                    return clean.Substring(nextQuote + 1).Trim();
                }
            }
            else
            {
                int spaceIdx = clean.IndexOf(' ');
                if (spaceIdx > 0 && spaceIdx < clean.Length - 1)
                {
                    return clean.Substring(spaceIdx + 1).Trim();
                }
            }

            return "";
        }
    }
}
