using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
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
        return TargetState.Ready; // We can always attempt to write to registry, verification will catch failure. 
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
        // Split Path and ValueName from the remainder. The convention here: the last segment is the ValueName.
        var subParts = parts[1].Split('\\');
        if (subParts.Length < 2)
        {
             result.Status = ResultStatus.Failed;
             result.Message = "Invalid registry target path.";
             return result;
        }
        
        var valueName = subParts.Last();
        var path = string.Join("\\", subParts.Take(subParts.Length - 1));

        var existing = _registryManager.ReadValue(hive, path, valueName);

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
        else if (entry.Value != null)
        {
            targetValue = entry.Value;
            if (entry.Value is int || entry.Value is uint) targetValueType = RegistryValueType.DWord;
            else if (entry.Value is long || entry.Value is ulong) targetValueType = RegistryValueType.QWord;
            else if (entry.Value is string) targetValueType = RegistryValueType.String;
        }

        // Check if change is actually needed
        if (existing != null && existing.Value != null && existing.Value.Equals(targetValue))
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = "Registry value is already optimized.";
            return result;
        }


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
            result.Message = $"Registry write failed: Invalid value type or conversion failed ({ex.Message})";
            return result;
        }
        catch (Exception ex)
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Registry write failed: {ex.Message}";
            return result;
        }

        var verification = _registryManager.ReadValue(hive, path, valueName);
        if (verification == null || !verification.Value!.Equals(targetValue))
        {
            result.Status = ResultStatus.Failed;
            result.Message = "Verification failed.";
            return result;
        }

        result.Status = ResultStatus.Success;
        result.Message = "Registry updated successfully.";
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
