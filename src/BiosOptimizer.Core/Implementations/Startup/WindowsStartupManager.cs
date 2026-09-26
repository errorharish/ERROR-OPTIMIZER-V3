using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.Startup;

[SupportedOSPlatform("windows")]
public class WindowsStartupManager : IStartupManager
{
    private class StartupLocation
    {
        public string Source { get; set; } = "";
        public string FriendlySource { get; set; } = "";
        public RegistryKey? Root { get; set; }
        public string Path { get; set; } = "";
        public string ApprovedPath { get; set; } = "";
        public bool IsFolder { get; set; }
        public bool RequiresAdmin { get; set; }
    }

    private readonly List<StartupLocation> _locations = new()
    {
        new StartupLocation
        {
            Source = "HKCU_RUN",
            FriendlySource = @"HKCU\Run",
            Root = Registry.CurrentUser,
            Path = @"Software\Microsoft\Windows\CurrentVersion\Run",
            ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            IsFolder = false,
            RequiresAdmin = false
        },
        new StartupLocation
        {
            Source = "HKLM_RUN",
            FriendlySource = @"HKLM\Run",
            Root = Registry.LocalMachine,
            Path = @"Software\Microsoft\Windows\CurrentVersion\Run",
            ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            IsFolder = false,
            RequiresAdmin = true
        },
        new StartupLocation
        {
            Source = "HKCU_RUNONCE",
            FriendlySource = @"HKCU\RunOnce",
            Root = Registry.CurrentUser,
            Path = @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            IsFolder = false,
            RequiresAdmin = false
        },
        new StartupLocation
        {
            Source = "HKLM_RUNONCE",
            FriendlySource = @"HKLM\RunOnce",
            Root = Registry.LocalMachine,
            Path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            IsFolder = false,
            RequiresAdmin = true
        },
        new StartupLocation
        {
            Source = "HKLM_WOW6432_RUN",
            FriendlySource = @"HKLM\WOW6432Node\Run",
            Root = Registry.LocalMachine,
            Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
            IsFolder = false,
            RequiresAdmin = true
        },
        new StartupLocation
        {
            Source = "HKLM_WOW6432_RUNONCE",
            FriendlySource = @"HKLM\WOW6432Node\RunOnce",
            Root = Registry.LocalMachine,
            Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
            ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
            IsFolder = false,
            RequiresAdmin = true
        },
        new StartupLocation
        {
            Source = "STARTUP_FOLDER_USER",
            FriendlySource = "Startup Folder (User)",
            Root = Registry.CurrentUser,
            Path = Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            IsFolder = true,
            RequiresAdmin = false
        },
        new StartupLocation
        {
            Source = "STARTUP_FOLDER_COMMON",
            FriendlySource = "Startup Folder (All Users)",
            Root = Registry.LocalMachine,
            Path = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            IsFolder = true,
            RequiresAdmin = true
        }
    };

    public List<StartupEntryState> GetStartupEntries()
    {
        var entries = new List<StartupEntryState>();

        foreach (var loc in _locations)
        {
            try
            {
                if (loc.IsFolder)
                {
                    ScanFolderLocation(loc, entries);
                }
                else if (loc.Root != null)
                {
                    ScanRegistryLocation(loc, entries);
                }
            }
            catch { }
        }

        return entries;
    }

    private void ScanRegistryLocation(StartupLocation loc, List<StartupEntryState> entries)
    {
        if (loc.Root == null) return;
        using var key = loc.Root.OpenSubKey(loc.Path, false);
        using var approvedKey = loc.Root.OpenSubKey(loc.ApprovedPath, false);

        if (key != null)
        {
            foreach (var valName in key.GetValueNames())
            {
                var val = key.GetValue(valName);
                string commandLine = val?.ToString() ?? "";
                var state = StartupState.Enabled;

                if (approvedKey != null)
                {
                    var appVal = approvedKey.GetValue(valName) as byte[];
                    if (appVal != null && appVal.Length > 0 && (appVal[0] == 0x03 || appVal[0] == 0x01 || (appVal[0] & 1) != 0))
                    {
                        state = StartupState.Disabled;
                    }
                }

                var entry = BuildStartupEntry(loc, valName, commandLine, val, state);
                entries.Add(entry);
            }
        }
    }

    private void ScanFolderLocation(StartupLocation loc, List<StartupEntryState> entries)
    {
        if (string.IsNullOrWhiteSpace(loc.Path) || !Directory.Exists(loc.Path)) return;
        using var approvedKey = loc.Root?.OpenSubKey(loc.ApprovedPath, false);

        foreach (var filePath in Directory.GetFiles(loc.Path))
        {
            string fileName = System.IO.Path.GetFileName(filePath);
            string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            if (ext != ".lnk" && ext != ".exe" && ext != ".bat" && ext != ".cmd") continue;

            string commandLine = filePath;
            var state = StartupState.Enabled;

            if (approvedKey != null)
            {
                var appVal = approvedKey.GetValue(fileName) as byte[];
                if (appVal != null && appVal.Length > 0 && (appVal[0] == 0x03 || appVal[0] == 0x01 || (appVal[0] & 1) != 0))
                {
                    state = StartupState.Disabled;
                }
            }

            var entry = BuildStartupEntry(loc, fileName, commandLine, filePath, state);
            entries.Add(entry);
        }
    }

    private StartupEntryState BuildStartupEntry(StartupLocation loc, string valueName, string commandLine, object? originalValue, StartupState state)
    {
        string exePath = ExtractExePath(commandLine);
        bool fileExists = !string.IsNullOrEmpty(exePath) && File.Exists(exePath);

        string displayName = valueName;
        string publisher = "Unknown";
        string version = "N/A";

        if (fileExists)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                    displayName = vi.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(vi.CompanyName))
                    publisher = vi.CompanyName.Trim();
                if (!string.IsNullOrWhiteSpace(vi.FileVersion))
                    version = vi.FileVersion.Trim();
                else if (!string.IsNullOrWhiteSpace(vi.ProductVersion))
                    version = vi.ProductVersion.Trim();
            }
            catch { }
        }

        // Clean up common publisher names if missing
        if (publisher == "Unknown")
        {
            publisher = InferPublisher(valueName, exePath);
        }

        // Determine Classification
        var (category, isSystem) = ClassifyStartupItem(valueName, displayName, publisher, exePath);

        // Determine Impact (without fake milliseconds)
        var (impact, impactMethod) = EvaluateImpact(valueName, displayName, publisher, exePath);

        return new StartupEntryState
        {
            Source = loc.Source,
            FriendlySource = loc.FriendlySource,
            Path = loc.Path,
            ValueName = valueName,
            DisplayName = displayName,
            Publisher = publisher,
            ExePath = exePath,
            CommandLine = commandLine,
            Version = version,
            OriginalValue = originalValue,
            State = state,
            Category = category,
            Impact = impact,
            ImpactMethod = impactMethod,
            IsSystemItem = isSystem,
            RequiresAdmin = loc.RequiresAdmin,
            FileExists = fileExists
        };
    }

    private string ExtractExePath(string commandLine)
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

    private string InferPublisher(string valueName, string exePath)
    {
        string text = (valueName + " " + exePath).ToLowerInvariant();
        if (text.Contains("discord")) return "Discord Inc.";
        if (text.Contains("steam") || text.Contains("valvesoftware")) return "Valve Corporation";
        if (text.Contains("spotify")) return "Spotify AB";
        if (text.Contains("logitech") || text.Contains("lghub")) return "Logitech";
        if (text.Contains("epicgames") || text.Contains("epic games")) return "Epic Games, Inc.";
        if (text.Contains("riotgames") || text.Contains("riot client")) return "Riot Games, Inc.";
        if (text.Contains("razer")) return "Razer Inc.";
        if (text.Contains("voicemod")) return "Voicemod S.L.";
        if (text.Contains("bluestacks")) return "BlueStack Systems, Inc.";
        if (text.Contains("nvidia")) return "NVIDIA Corporation";
        if (text.Contains("realtek") || text.Contains("rtkaud")) return "Realtek Semiconductor Corp.";
        if (text.Contains("intel")) return "Intel Corporation";
        if (text.Contains("microsoft") || text.Contains("securityhealth") || text.Contains("windowsdefender")) return "Microsoft Corporation";
        if (text.Contains("google") || text.Contains("chrome")) return "Google LLC";
        return "Unknown";
    }

    private (string Category, bool IsSystem) ClassifyStartupItem(string valueName, string displayName, string publisher, string exePath)
    {
        string text = (valueName + " " + displayName + " " + publisher + " " + exePath).ToLowerInvariant();

        if (text.Contains("securityhealth") || text.Contains("defender") || text.Contains("antivirus") || text.Contains("security"))
        {
            return ("SECURITY", true);
        }

        if (text.Contains("nvidia") || text.Contains("realtek") || text.Contains("rtkaud") || text.Contains("intel graphics") || text.Contains("amd radeon") || text.Contains("driver"))
        {
            return ("DRIVER", true);
        }

        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
            exePath.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) ||
            exePath.Contains(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase))
        {
            return ("SYSTEM", true);
        }

        return ("USER APPLICATION", false);
    }

    private (string Impact, string ImpactMethod) EvaluateImpact(string valueName, string displayName, string publisher, string exePath)
    {
        string text = (valueName + " " + displayName + " " + publisher + " " + exePath).ToLowerInvariant();

        // Heavy suites with multi-process renderers and background daemon networks
        if (text.Contains("discord") || text.Contains("steam") || text.Contains("epicgames") || 
            text.Contains("bluestacks") || text.Contains("voicemod") || text.Contains("creative cloud") ||
            text.Contains("razer synapse") || text.Contains("lghub") || text.Contains("overwolf"))
        {
            return ("HIGH", "ESTIMATED");
        }

        if (text.Contains("spotify") || text.Contains("viber") || text.Contains("telegram") || 
            text.Contains("skype") || text.Contains("slack") || text.Contains("urbanvpn") ||
            text.Contains("torrent") || text.Contains("onedrive") || text.Contains("dropbox"))
        {
            return ("MEDIUM", "ESTIMATED");
        }

        if (text.Contains("securityhealth") || text.Contains("rtkaud") || text.Contains("audio") || 
            text.Contains("tray") || text.Contains("ctfmon") || text.Contains("helper"))
        {
            return ("LOW", "ESTIMATED");
        }

        return ("UNKNOWN", "NOT MEASURED");
    }

    public bool DisableEntry(string source, string path, string valueName)
    {
        var loc = _locations.FirstOrDefault(l => l.Source == source && l.Path == path);
        if (loc?.Root == null) return false;

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
        if (loc?.Root == null) return false;

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