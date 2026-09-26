using System.Runtime.Versioning;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.CLI.Adapters;

[SupportedOSPlatform("windows")]
public class WindowsStartupManager : IStartupManager
{
    private readonly List<(string Source, RegistryKey Root, string Path, string ApprovedPath)> _locations = new()
    {
        ("HKCU_RUN", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        ("HKLM_RUN", Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")
    };

    public List<StartupEntryState> GetStartupEntries()
    {
        var entries = new List<StartupEntryState>();

        foreach (var loc in _locations)
        {
            using var key = loc.Root.OpenSubKey(loc.Path, false);
            using var approvedKey = loc.Root.OpenSubKey(loc.ApprovedPath, false);
            
            if (key != null)
            {
                foreach (var valName in key.GetValueNames())
                {
                    var val = key.GetValue(valName);
                    var state = StartupState.Enabled;
                    
                    if (approvedKey != null)
                    {
                        var appVal = approvedKey.GetValue(valName) as byte[];
                        if (appVal != null && appVal.Length > 0 && appVal[0] != 0x02)
                        {
                            state = StartupState.Disabled;
                        }
                    }
                    
                    entries.Add(new StartupEntryState
                    {
                        Source = loc.Source,
                        Path = loc.Path,
                        ValueName = valName,
                        OriginalValue = val,
                        State = state
                    });
                }
            }
        }

        return entries;
    }

    public bool DisableEntry(string source, string path, string valueName)
    {
        var loc = _locations.FirstOrDefault(l => l.Source == source && l.Path == path);
        if (loc.Root == null) return false;

        try
        {
            using var approvedKey = loc.Root.CreateSubKey(loc.ApprovedPath, true);
            if (approvedKey == null) return false;
            
            // 0x03 means disabled in Task Manager
            byte[] disabledVal = { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            approvedKey.SetValue(valueName, disabledVal, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool EnableEntry(string source, string path, string valueName)
    {
        var loc = _locations.FirstOrDefault(l => l.Source == source && l.Path == path);
        if (loc.Root == null) return false;

        try
        {
            using var approvedKey = loc.Root.CreateSubKey(loc.ApprovedPath, true);
            if (approvedKey == null) return false;
            
            // 0x02 means enabled in Task Manager
            byte[] enabledVal = { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            approvedKey.SetValue(valueName, enabledVal, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
