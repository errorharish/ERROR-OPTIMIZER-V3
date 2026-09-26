using System.Runtime.Versioning;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.CLI.Adapters;

[SupportedOSPlatform("windows")]
public class WindowsRegistryManager : IRegistryManager
{
    public RegistryValueData? ReadValue(string hive, string path, string valueName)
    {
        var root = GetHive(hive);
        if (root == null) return null;

        using var key = root.OpenSubKey(path, false);
        if (key == null) return null;

        var val = key.GetValue(valueName);
        if (val == null) return null;

        var kind = key.GetValueKind(valueName);
        return new RegistryValueData
        {
            Hive = hive,
            Path = path,
            ValueName = valueName,
            Value = val,
            ValueType = MapToType(kind)
        };
    }

    public bool WriteValue(string hive, string path, string valueName, object value, RegistryValueType type)
    {
        var root = GetHive(hive);
        if (root == null) throw new ArgumentException("Invalid registry hive.");

        using var key = root.CreateSubKey(path, true);
        if (key == null) throw new InvalidOperationException("Failed to create or open registry key path.");

        key.SetValue(valueName, value, MapToKind(type));
        return true;
    }

    public bool DeleteValue(string hive, string path, string valueName)
    {
        var root = GetHive(hive);
        if (root == null) throw new ArgumentException("Invalid registry hive.");

        using var key = root.OpenSubKey(path, true);
        if (key == null) return true; // already gone

        key.DeleteValue(valueName, false);
        return true;
    }

    public bool BackupValue(string hive, string path, string valueName, string backupFilePath)
    {
        // Snapshot is handled at the ActionHandler level now.
        // We return true because the snapshot is built in the engine.
        return true;
    }

    public bool RestoreValue(string backupFilePath)
    {
        return true;
    }

    private RegistryKey? GetHive(string hiveString)
    {
        return hiveString.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => null
        };
    }

    private RegistryValueType MapToType(RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.String => RegistryValueType.String,
            RegistryValueKind.ExpandString => RegistryValueType.String,
            RegistryValueKind.DWord => RegistryValueType.DWord,
            RegistryValueKind.QWord => RegistryValueType.QWord,
            RegistryValueKind.MultiString => RegistryValueType.MultiString,
            RegistryValueKind.Binary => RegistryValueType.Binary,
            _ => RegistryValueType.Unknown
        };
    }

    private RegistryValueKind MapToKind(RegistryValueType type)
    {
        return type switch
        {
            RegistryValueType.String => RegistryValueKind.String,
            RegistryValueType.DWord => RegistryValueKind.DWord,
            RegistryValueType.QWord => RegistryValueKind.QWord,
            RegistryValueType.MultiString => RegistryValueKind.MultiString,
            RegistryValueType.Binary => RegistryValueKind.Binary,
            _ => RegistryValueKind.Unknown
        };
    }
}
