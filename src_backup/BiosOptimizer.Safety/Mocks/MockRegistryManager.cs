using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Safety.Mocks;

public class MockRegistryManager : IRegistryManager
{
    private readonly Dictionary<string, RegistryValueData> _store = new(StringComparer.OrdinalIgnoreCase);

    private string GetKey(string hive, string path, string name) => $"{hive}\\{path}\\{name}";

    public void AddMockValue(string hive, string path, string valueName, object value, RegistryValueType type)
    {
        var key = GetKey(hive, path, valueName);
        _store[key] = new RegistryValueData { Hive = hive, Path = path, ValueName = valueName, Value = value, ValueType = type };
    }

    public RegistryValueData? ReadValue(string hive, string path, string valueName)
    {
        var key = GetKey(hive, path, valueName);
        _store.TryGetValue(key, out var data);
        return data;
    }

    public bool WriteValue(string hive, string path, string valueName, object value, RegistryValueType type)
    {
        var key = GetKey(hive, path, valueName);
        _store[key] = new RegistryValueData { Hive = hive, Path = path, ValueName = valueName, Value = value, ValueType = type };
        return true;
    }

    public bool DeleteValue(string hive, string path, string valueName)
    {
        return _store.Remove(GetKey(hive, path, valueName));
    }

    public bool BackupValue(string hive, string path, string valueName, string backupFilePath) => true;
    public bool RestoreValue(string backupFilePath) => true;
}
