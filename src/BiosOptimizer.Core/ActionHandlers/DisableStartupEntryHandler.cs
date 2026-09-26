using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using Microsoft.Win32;

namespace BiosOptimizer.Core.ActionHandlers;

public class DisableStartupEntryHandler : IActionHandler
{
    private readonly IStartupManager _startupManager;
    public DisableStartupEntryHandler(IStartupManager startupManager)
    {
        _startupManager = startupManager;
    }

    public string ActionName => "DisableStartupEntry";

    // Registry startup locations to search
    private static readonly (RegistryHive Hive, string Path)[] StartupPaths =
    [
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
    ];

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready; 
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult { ItemId = entry.Id, DisplayName = entry.DisplayName };
        
        var currentValue = GetCurrentValueDisplay(entry);

        if (string.Equals(currentValue, "Not Found", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentValue, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = "Startup entry is not active.";
            return result;
        }

        // Capture before state into Universal Backup Manager
        try
        {
            BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureGenericTweak(
                entry.SourceProfile is { Length: > 0 } sp ? sp : "Manual",
                $"startup.{entry.Target}".ToLowerInvariant(),
                $"Startup Entry: {entry.Target}",
                "Services",
                "StartupEntry",
                "Enabled",
                "Disabled (Removed from Run key)",
                "RegistryValueRestore",
                false, // Removal from Run key is not directly re-addable via BackupManager
                "SAFE"
            );
        }
        catch { }

        // Try to remove from registry directly (most reliable approach)
        bool removed = TryRemoveFromRegistry(entry.Target);
        if (removed)
        {
            result.Status = ResultStatus.Success;
            result.Message = "Startup entry removed from registry.";
            return result;
        }

        result.Status = ResultStatus.NotApplicable;
        result.Message = "Startup entry not found or could not be disabled.";
        return result;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        try
        {
            // Check registry run keys
            foreach (var (hive, path) in StartupPaths)
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(path, false);
                if (key == null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    if (valueName.Contains(entry.Target, StringComparison.OrdinalIgnoreCase))
                    {
                        return "Enabled";
                    }
                }
            }
        }
        catch { }

        return "Not Found";
    }

    private static bool TryRemoveFromRegistry(string entryName)
    {
        bool removed = false;
        foreach (var (hive, path) in StartupPaths)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(path, true);
                if (key == null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    if (valueName.Contains(entryName, StringComparison.OrdinalIgnoreCase))
                    {
                        key.DeleteValue(valueName, false);
                        removed = true;
                    }
                }
            }
            catch { }
        }
        return removed;
    }
}

