using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Core.Services;
using System;
using System.Linq;

namespace BiosOptimizer.Core.ActionHandlers;

public class SetRegistryValueHandler : IActionHandler
{
    private readonly IRegistryManager _registryManager;

    public SetRegistryValueHandler(IRegistryManager registryManager)
    {
        _registryManager = registryManager;
    }

    public string ActionName => "SetRegistryValue";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Target))
            return TargetState.NotApplicable;

        var parts = entry.Target.Split('\\', 2);
        if (parts.Length < 2) return TargetState.NotApplicable;

        var hive = parts[0];
        var subParts = parts[1].Split('\\');
        if (subParts.Length < 2) return TargetState.NotApplicable;

        if (!hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) && 
            !hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) &&
            !hive.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) &&
            !hive.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
        {
            return TargetState.NotApplicable;
        }

        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult { ItemId = entry.Id, DisplayName = entry.DisplayName };

        var parts = entry.Target.Split('\\', 2);
        if (parts.Length < 2)
        {
            result.Status = ResultStatus.Failed;
            result.Message = "Invalid registry target format. Use HIVE\\Path\\ValueName format (e.g. HKCU\\Software\\Key\\Value).";
            return result;
        }

        var hive = parts[0];
        var subParts = parts[1].Split('\\');
        if (subParts.Length < 2)
        {
            result.Status = ResultStatus.Failed;
            result.Message = "Invalid registry target path.";
            return result;
        }
        
        var valueName = subParts.Last();
        var path = string.Join("\\", subParts.Take(subParts.Length - 1));

        // Value parsing
        object targetValue = 0;
        var targetValueType = RegistryValueType.DWord;
        
        if (entry.Value is System.Text.Json.JsonElement jsonElement)
        {
            switch (jsonElement.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    targetValue = jsonElement.GetString() ?? "";
                    targetValueType = RegistryValueType.String;
                    if (int.TryParse(targetValue.ToString(), out int parsedInt))
                    {
                        targetValue = parsedInt;
                        targetValueType = RegistryValueType.DWord;
                    }
                    break;
                case System.Text.Json.JsonValueKind.Number:
                    if (jsonElement.TryGetInt32(out int iVal))
                    {
                        targetValue = iVal;
                        targetValueType = RegistryValueType.DWord;
                    }
                    else if (jsonElement.TryGetInt64(out long lVal))
                    {
                        targetValue = lVal;
                        targetValueType = RegistryValueType.QWord;
                    }
                    break;
                case System.Text.Json.JsonValueKind.True:
                    targetValue = 1;
                    targetValueType = RegistryValueType.DWord;
                    break;
                case System.Text.Json.JsonValueKind.False:
                    targetValue = 0;
                    targetValueType = RegistryValueType.DWord;
                    break;
                default:
                    targetValue = jsonElement.ToString();
                    targetValueType = RegistryValueType.String;
                    break;
            }
        }
        else if (entry.Value is string s)
        {
            targetValue = s;
            targetValueType = RegistryValueType.String;
            if (int.TryParse(s, out var i))
            {
                targetValue = i;
                targetValueType = RegistryValueType.DWord;
            }
        }
        else if (entry.Value is int intVal)
        {
            targetValue = intVal;
            targetValueType = RegistryValueType.DWord;
        }
        else if (entry.Value is bool b)
        {
            targetValue = b ? 1 : 0;
            targetValueType = RegistryValueType.DWord;
        }
        else if (entry.Value != null)
        {
            targetValue = entry.Value;
        }

        // Capture before state into Universal Backup Manager
        try
        {
            var before = _registryManager.ReadValue(hive, path, valueName);
            if (before == null || !StateNormalizer.IsSatisfied(before.Value?.ToString(), targetValue.ToString()))
            {
                BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureRegistryTweak(
                    "Manual",
                    entry.DisplayName ?? valueName,
                    hive,
                    path,
                    valueName,
                    before?.Value ?? "VALUE DID NOT EXIST",
                    targetValueType.ToString(),
                    targetValue,
                    "SAFE"
                );
            }
        }
        catch { }

        // Apply via RegistryManager
        try
        {
            _registryManager.WriteValue(hive, path, valueName, targetValue, targetValueType);
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Status = ResultStatus.Blocked;
            result.Message = $"Registry write blocked: Access denied ({ex.Message})";
            return result;
        }
        catch (System.Security.SecurityException ex)
        {
            result.Status = ResultStatus.Blocked;
            result.Message = $"Registry write blocked: Security policy ({ex.Message})";
            return result;
        }
        catch (ArgumentException ex)
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Registry write failed: Invalid value type or conversion ({ex.Message})";
            return result;
        }
        catch (Exception ex)
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Registry write failed: {ex.Message}";
            return result;
        }

        // Live Readback Verification
        var verification = _registryManager.ReadValue(hive, path, valueName);
        if (verification == null)
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Verification failed: Registry value '{hive}\\{path}\\{valueName}' was not found after write.";
            return result;
        }

        bool isMatch = StateNormalizer.IsSatisfied(verification.Value?.ToString(), targetValue.ToString());

        if (!isMatch)
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Verification mismatch: Target was '{targetValue}', but readback was '{verification.Value}'.";
            return result;
        }

        result.Status = ResultStatus.Success;
        result.Message = $"Registry verified successfully ({valueName} = {targetValue}).";
        return result;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var parts = entry.Target.Split('\\', 2);
        if (parts.Length < 2) return "Unknown";
        var hive = parts[0];
        var subParts = parts[1].Split('\\');
        if (subParts.Length < 2) return "Unknown";
        var valueName = subParts.Last();
        var path = string.Join("\\", subParts.Take(subParts.Length - 1));

        var existing = _registryManager.ReadValue(hive, path, valueName);
        return existing?.Value?.ToString() ?? "Not Set";
    }
}
