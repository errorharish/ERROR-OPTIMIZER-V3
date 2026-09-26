using BiosOptimizer.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace BiosOptimizer.Core.Implementations;

public class BackupManager : IBackupManager
{
    private const string BackupRoot = @"C:\OptimizerBackup";
    private const string RegistryBackupPath = BackupRoot + @"\Registry";
    private const string ServicesBackupPath = BackupRoot + @"\Services";
    private const string ProcessesBackupPath = BackupRoot + @"\Processes";

    private readonly IRegistryManager _registryManager;
    private readonly IServiceManager _serviceManager;

    public BackupManager(IRegistryManager registryManager, IServiceManager serviceManager)
    {
        _registryManager = registryManager;
        _serviceManager = serviceManager;
        
        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(BackupRoot);
        Directory.CreateDirectory(RegistryBackupPath);
        Directory.CreateDirectory(ServicesBackupPath);
        Directory.CreateDirectory(ProcessesBackupPath);
    }

    public bool CreateSystemRestorePoint(string description)
    {
        try
        {
            var info = new RESTOREPOINTINFO
            {
                dwEventType = BEGIN_SYSTEM_CHANGE,
                dwRestorePtType = MODIFY_SETTINGS,
                llSequenceNumber = 0,
                szDescription = description
            };

            var status = new STATEMGRSTATUS();
            bool result = SRSetRestorePointW(ref info, out status);
            return result;
        }
        catch
        {
            return false; // Sometimes blocked by GPO or disabled by user
        }
    }

    public bool BackupRegistryState(IEnumerable<string> registryPaths)
    {
        try
        {
            // We'll use the existing IRegistryManager to back up each path.
            // But since the requirement specifically asks to "Export every registry key to C:\OptimizerBackup\Registry\*.reg",
            // we will invoke reg.exe export for each full path.
            foreach (var fullPath in registryPaths)
            {
                var safeName = fullPath.Replace("\\", "_").Replace(":", "") + ".reg";
                var exportPath = Path.Combine(RegistryBackupPath, safeName);
                
                var p = new System.Diagnostics.Process();
                p.StartInfo.FileName = "reg.exe";
                p.StartInfo.Arguments = $"export \"{fullPath}\" \"{exportPath}\" /y";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit();
            }
            return true;
        }
        catch { return false; }
    }

    public bool BackupServiceState(IEnumerable<string> serviceNames)
    {
        try
        {
            var doc = new XDocument(new XElement("Services"));
            foreach (var srvName in serviceNames)
            {
                var startup = _serviceManager.GetStartupType(srvName);
                var state = _serviceManager.GetRunningState(srvName);
                var el = new XElement("Service",
                    new XAttribute("Name", srvName),
                    new XAttribute("StartupType", startup.ToString()),
                    new XAttribute("RunningState", state.ToString())
                );
                doc.Root?.Add(el);
            }
            doc.Save(Path.Combine(ServicesBackupPath, "backup.xml"));
            return true;
        }
        catch { return false; }
    }

    public bool LogKilledProcesses(IEnumerable<string> processPaths)
    {
        try
        {
            File.AppendAllLines(Path.Combine(ProcessesBackupPath, "killed.txt"), processPaths);
            return true;
        }
        catch { return false; }
    }

    public bool RestoreAll()
    {
        try
        {
            // 1. Restore registry
            if (Directory.Exists(RegistryBackupPath))
            {
                foreach (var file in Directory.GetFiles(RegistryBackupPath, "*.reg"))
                {
                    var p = new System.Diagnostics.Process();
                    p.StartInfo.FileName = "reg.exe";
                    p.StartInfo.Arguments = $"import \"{file}\"";
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.CreateNoWindow = true;
                    p.Start();
                    p.WaitForExit();
                }
            }

            // 2. Restore services
            var svcFile = Path.Combine(ServicesBackupPath, "backup.xml");
            if (File.Exists(svcFile))
            {
                var doc = XDocument.Load(svcFile);
                if (doc.Root != null)
                {
                    foreach (var el in doc.Root.Elements("Service"))
                    {
                        var name = el.Attribute("Name")?.Value;
                        var startup = el.Attribute("StartupType")?.Value;
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(startup))
                        {
                            if (Enum.TryParse<ServiceStartupType>(startup, out var startupType))
                            {
                                _serviceManager.SetStartupType(name, startupType);
                            }
                        }
                    }
                }
            }
            
            return true;
        }
        catch { return false; }
    }

    // --- P/Invoke for System Restore ---
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RESTOREPOINTINFO
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [DllImport("Srclient.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SRSetRestorePointW(ref RESTOREPOINTINFO pRestorePtSpec, out STATEMGRSTATUS pSMgrStatus);

    private const int BEGIN_SYSTEM_CHANGE = 100;
    private const int MODIFY_SETTINGS = 12;

    public bool BackupRegistryValue(string owner, string registryKey, string valueName, object oldValue, string oldType, object targetValue, string result)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string backupDir = Path.Combine(appData, "AntiGravity", "backups");
            Directory.CreateDirectory(backupDir);

            string manifestPath = Path.Combine(backupDir, "manifest.json");
            var list = new List<object>();

            if (File.Exists(manifestPath))
            {
                string json = File.ReadAllText(manifestPath);
                try { list = System.Text.Json.JsonSerializer.Deserialize<List<object>>(json) ?? new List<object>(); } catch {}
            }

            var entry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                machine = Environment.MachineName,
                OS = Environment.OSVersion.VersionString,
                profile = owner,
                setting = registryKey + "\\" + valueName,
                oldValue = oldValue?.ToString(),
                oldType = oldType,
                targetValue = targetValue?.ToString(),
                result = result
            };

            list.Add(entry);
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RestoreByOwner(string owner)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string backupDir = Path.Combine(appData, "AntiGravity", "backups");
            string manifestPath = Path.Combine(backupDir, "manifest.json");

            if (!File.Exists(manifestPath)) return false;

            string json = File.ReadAllText(manifestPath);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(json);
            if (list == null) return false;

            bool restoredAny = false;
            var remaining = new List<System.Text.Json.JsonElement>();

            // Reverse to restore last applied first
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var el = list[i];
                if (el.TryGetProperty("profile", out var prof) && prof.GetString() == owner)
                {
                    // Restore logic
                    if (el.TryGetProperty("setting", out var setting) && el.TryGetProperty("oldValue", out var oldVal) && el.TryGetProperty("oldType", out var oldType))
                    {
                        string? path = setting.GetString();
                        string? valStr = oldVal.GetString();
                        string? t = oldType.GetString();

                        if (path != null)
                        {
                            int lastSlash = path.LastIndexOf('\\');
                            if (lastSlash > 0)
                            {
                                string keyPath = path.Substring(0, lastSlash);
                                string valName = path.Substring(lastSlash + 1);

                                Microsoft.Win32.RegistryKey root = keyPath.StartsWith("HKEY_LOCAL_MACHINE") || keyPath.StartsWith("HKLM") ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
                                string subPath = keyPath.Replace("HKEY_LOCAL_MACHINE\\", "").Replace("HKLM\\", "").Replace("HKEY_CURRENT_USER\\", "").Replace("HKCU\\", "");

                                using var key = root.OpenSubKey(subPath, true);
                                if (key != null)
                                {
                                    if (valStr == null || valStr == "Not Set")
                                    {
                                        key.DeleteValue(valName, false);
                                    }
                                    else
                                    {
                                        Microsoft.Win32.RegistryValueKind kind = Microsoft.Win32.RegistryValueKind.String;
                                        if (t == "DWord") kind = Microsoft.Win32.RegistryValueKind.DWord;
                                        
                                        object finalVal = valStr;
                                        if (kind == Microsoft.Win32.RegistryValueKind.DWord && int.TryParse(valStr, out int iVal)) finalVal = iVal;

                                        key.SetValue(valName, finalVal, kind);
                                    }
                                    restoredAny = true;
                                }
                            }
                        }
                    }
                }
                else
                {
                    remaining.Insert(0, el);
                }
            }

            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(remaining, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return restoredAny;
        }
        catch
        {
            return false;
        }
    }
}
