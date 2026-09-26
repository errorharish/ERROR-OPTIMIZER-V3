using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.Service.Engine
{
    public abstract class RegistryOptimizationAction : IOptimizationAction
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Category { get; }
        public abstract string Description { get; }
        public abstract string RiskLevel { get; }
        public virtual bool RequiresRestart => false;

        protected abstract string RootKey { get; } // "HKLM" or "HKCU"
        protected abstract string SubKey { get; }
        protected abstract string ValueName { get; }
        protected abstract object TargetValue { get; }
        protected abstract RegistryValueKind ValueKind { get; }

        private object? _backupValue = null;
        private bool _keyExisted = false;
        private bool _valueExisted = false;

        public virtual Task<(bool IsApplicable, string Reason)> CheckApplicabilityAsync(MachineProfileDto machineProfile, WorkloadProfileDto workloadProfile, CancellationToken ct = default)
        {
            return Task.FromResult((true, string.Empty));
        }

        public Task<(bool AlreadyOptimized, string CurrentValue)> DetectAsync(CancellationToken ct = default)
        {
            string currStr = "Not Set";
            bool alreadyOptimized = false;
            
            try
            {
                using var root = RootKey == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = root.OpenSubKey(SubKey, false);
                
                if (key != null)
                {
                    var val = key.GetValue(ValueName);
                    if (val != null)
                    {
                        currStr = val.ToString() ?? "Not Set";
                        alreadyOptimized = currStr.Equals(TargetValue?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                currStr = $"Error: {ex.Message}";
            }

            return Task.FromResult((alreadyOptimized, currStr));
        }

        public Task<bool> BackupAsync(CancellationToken ct = default)
        {
            try
            {
                using var root = RootKey == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = root.OpenSubKey(SubKey, false);
                if (key != null)
                {
                    _keyExisted = true;
                    _backupValue = key.GetValue(ValueName);
                    _valueExisted = _backupValue != null;
                }
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public Task<bool> ApplyAsync(CancellationToken ct = default)
        {
            try
            {
                using var root = RootKey == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = root.CreateSubKey(SubKey, true);
                if (key != null)
                {
                    key.SetValue(ValueName, TargetValue, ValueKind);
                    return Task.FromResult(true);
                }
            }
            catch
            {
            }
            return Task.FromResult(false);
        }

        public async Task<bool> VerifyAsync(CancellationToken ct = default)
        {
            var (isOpt, _) = await DetectAsync(ct);
            return isOpt;
        }

        public Task<bool> RollbackAsync(CancellationToken ct = default)
        {
            try
            {
                using var root = RootKey == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                if (!_keyExisted)
                {
                    root.DeleteSubKeyTree(SubKey, false);
                    return Task.FromResult(true);
                }

                using var key = root.OpenSubKey(SubKey, true);
                if (key != null)
                {
                    if (_valueExisted && _backupValue != null)
                    {
                        key.SetValue(ValueName, _backupValue, ValueKind);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                    return Task.FromResult(true);
                }
            }
            catch
            {
            }
            return Task.FromResult(false);
        }
    }
}
