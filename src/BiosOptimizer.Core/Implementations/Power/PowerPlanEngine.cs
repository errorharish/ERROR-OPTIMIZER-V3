using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.Power
{
    /// <summary>
    /// Canonical, authoritative Power Plan Engine built around Windows native Power Management APIs (powrprof.dll).
    /// Windows is the single source of truth for installed schemes, active scheme, and custom plan creation.
    /// </summary>
    public class PowerPlanEngine
    {
        private static readonly Lazy<PowerPlanEngine> _instance = new(() => new PowerPlanEngine());
        public static PowerPlanEngine Instance => _instance.Value;

        // Well-known Standard Windows Power Scheme GUIDs
        public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string HighPerformanceAltGuid = "87a68632-24a8-4ba8-ba06-0fc8d0e5cdf9";
        public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61"; // Template GUID
        public const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

        // Well-known Power Setting Subgroups & Setting GUIDs
        public static readonly Guid GUID_PROCESSOR_SETTINGS_SUBGROUP = new("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid GUID_PROCTHROTTLEMIN = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        public static readonly Guid GUID_PROCTHROTTLEMAX = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
        public static readonly Guid GUID_SYSCOOLPOL = new("94d3a615-a899-4ac5-8282-e32b15a44ac1");
        public static readonly Guid GUID_PERFBOOSTMODE = new("be337238-0d82-4146-a960-4f3749d470c7");

        public const uint ACCESS_SCHEME = 16;
        private const string REG_AI_PLANS_KEY = @"Software\ErrorOptimizer\PowerPlans";
        private const string REG_POWER_CONFIG_KEY = @"Software\ErrorOptimizer\PowerPlans\Config";

        private string? _previousActiveSchemeGuid;
        private string? _previousActiveSchemeName;
        private string? _lastKnownActiveGuid;
        private string? _lastKnownActiveName;
        private bool _isApplyingScheme;
        private readonly List<PowerPlanChangeLogEntry> _changeLog = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _enforcementCts;
        private Task? _enforcementTask;

        public UserPowerPlanLock UserLock { get; } = new();

        public string? LastTargetGuid { get; private set; }
        public PowerPlanChangeSource LastChangeSource { get; private set; } = PowerPlanChangeSource.UNKNOWN;
        public DateTime? LastChangeTimestamp { get; private set; }
        public PowerPlanOperationResult? LastActivationResult { get; private set; }

        public IReadOnlyList<PowerPlanChangeLogEntry> GetChangeLog()
        {
            lock (_lock)
            {
                return _changeLog.ToList();
            }
        }

        private void AppendChangeLog(PowerPlanChangeLogEntry entry)
        {
            lock (_lock)
            {
                _changeLog.Insert(0, entry);
                if (_changeLog.Count > 100)
                {
                    _changeLog.RemoveAt(_changeLog.Count - 1);
                }
            }
        }

        #region Native Win32 PowrProf APIs

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerEnumerate(
            IntPtr RootPowerKey,
            IntPtr SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            uint AccessFlags,
            uint Index,
            IntPtr Buffer,
            ref uint BufferSize);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr pActivePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerDuplicateScheme(IntPtr RootPowerKey, ref Guid SourceSchemeGuid, out IntPtr DestinationSchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerDeleteScheme(IntPtr RootPowerKey, ref Guid SchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PowerReadFriendlyName(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid,
            IntPtr Buffer,
            ref uint BufferSize);

        [DllImport("powrprof.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PowerReadDescription(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid,
            IntPtr Buffer,
            ref uint BufferSize);

        [DllImport("powrprof.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PowerWriteFriendlyName(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid,
            string Buffer,
            uint BufferSize);

        [DllImport("powrprof.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PowerWriteDescription(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid,
            string Buffer,
            uint BufferSize);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerReadACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerReadDCValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint DcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerWriteACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerWriteDCValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint DcValueIndex);

        [DllImport("Kernel32.dll", EntryPoint = "LocalFree")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        #endregion

        public string? PreviousActiveSchemeName
        {
            get { lock (_lock) return _previousActiveSchemeName; }
        }

        public string? PreviousActiveSchemeGuid
        {
            get { lock (_lock) return _previousActiveSchemeGuid; }
        }

        /// <summary>
        /// Queries Windows directly and authoritatively via powercfg /getactivescheme and Win32 PowerGetActiveScheme.
        /// Zero caching. Returns the exact active GUID and friendly name.
        /// </summary>
        public (Guid guid, string name) GetActiveSchemeNative()
        {
            Guid activeGuid = Guid.Empty;
            string friendlyName = string.Empty;

            // 1. Primary Authoritative Query via powercfg /getactivescheme (Control Panel & System-wide true state)
            try
            {
                var res = TaskExecutionSupervisor.ExecuteProcessAsync(GetPowercfgPath(), "/getactivescheme", TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                if (res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.StandardOutput))
                {
                    var (guidStr, name) = ParseSchemeLine(res.StandardOutput);
                    if (Guid.TryParse(guidStr, out Guid parsedGuid))
                    {
                        activeGuid = parsedGuid;
                        friendlyName = !string.IsNullOrWhiteSpace(name) ? name : ReadSchemeFriendlyName(parsedGuid);
                    }
                }
            }
            catch { }

            // 2. Secondary Native Query via Win32 PowerGetActiveScheme
            if (activeGuid == Guid.Empty)
            {
                try
                {
                    IntPtr pGuid;
                    uint ret = PowerGetActiveScheme(IntPtr.Zero, out pGuid);
                    if (ret == 0 && pGuid != IntPtr.Zero)
                    {
                        try
                        {
                            object? structObj = Marshal.PtrToStructure(pGuid, typeof(Guid));
                            if (structObj is Guid g)
                            {
                                activeGuid = g;
                                friendlyName = ReadSchemeFriendlyName(g);
                            }
                        }
                        finally
                        {
                            LocalFree(pGuid);
                        }
                    }
                }
                catch { }
            }

            if (activeGuid == Guid.Empty)
            {
                activeGuid = new Guid(BalancedGuid);
                friendlyName = "Balanced";
            }

            // Track external transitions passively
            TrackExternalChange(activeGuid, friendlyName);

            return (activeGuid, friendlyName);
        }

        private void TrackExternalChange(Guid activeGuid, string friendlyName)
        {
            string currentGuidStr = NormalizeGuid(activeGuid.ToString());
            lock (_lock)
            {
                if (_lastKnownActiveGuid != null && !_isApplyingScheme && !string.Equals(_lastKnownActiveGuid, currentGuidStr, StringComparison.OrdinalIgnoreCase))
                {
                    AppendChangeLog(new PowerPlanChangeLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        PreviousGuid = _lastKnownActiveGuid,
                        PreviousName = _lastKnownActiveName ?? "Unknown",
                        NewGuid = currentGuidStr,
                        NewName = friendlyName,
                        Source = PowerPlanChangeSource.EXTERNAL_OR_WINDOWS,
                        UserInitiated = false,
                        ThreadOrTask = Thread.CurrentThread.ManagedThreadId.ToString(),
                        Result = "EXTERNAL_CHANGE",
                        Message = $"Power scheme changed externally by Windows/Control Panel to '{friendlyName}'."
                    });
                }
                _lastKnownActiveGuid = currentGuidStr;
                _lastKnownActiveName = friendlyName;
            }
        }

        /// <summary>
        /// Async wrapper returning authoritative string GUID and friendly name.
        /// </summary>
        public async Task<(string guid, string name)> GetActiveSchemeAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var (guid, name) = GetActiveSchemeNative();
                return (NormalizeGuid(guid.ToString()), name);
            }, ct);
        }

        /// <summary>
        /// Discovers all real power schemes installed in Windows via native PowerEnumerate API.
        /// Compares against PowerGetActiveScheme once to mark the single true active plan.
        /// </summary>
        public async Task<List<PowerPlanItem>> DiscoverPowerPlansAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var plans = new List<PowerPlanItem>();
                var (activeGuid, _) = GetActiveSchemeNative();
                string activeGuidStr = NormalizeGuid(activeGuid.ToString());
                var ownedRecords = GetOwnershipRecords();
                var startupConfig = GetStartupConfig();

                // 1. Primary Native Enumeration via PowerEnumerate
                uint index = 0;
                uint bufferSize = (uint)Marshal.SizeOf(typeof(Guid));
                IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);

                try
                {
                    while (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, index, buffer, ref bufferSize) == 0)
                    {
                        object? structObj = Marshal.PtrToStructure(buffer, typeof(Guid));
                        if (structObj is Guid schemeGuid)
                        {
                            string guidStr = NormalizeGuid(schemeGuid.ToString());
                            string name = ReadSchemeFriendlyName(schemeGuid);
                            string desc = ReadSchemeDescription(schemeGuid);

                            bool isOwned = ownedRecords.ContainsKey(guidStr) ||
                                           name.Contains("Error Optimizer AI", StringComparison.OrdinalIgnoreCase) ||
                                           name.Contains("AI Performance", StringComparison.OrdinalIgnoreCase);

                            bool isBuiltin = IsBuiltinScheme(guidStr);
                            bool isActive = schemeGuid.Equals(activeGuid);
                            bool isStartup = startupConfig.AutoEnableOnStartup &&
                                             !string.IsNullOrEmpty(startupConfig.StartupPowerPlanGuid) &&
                                             guidStr.Equals(NormalizeGuid(startupConfig.StartupPowerPlanGuid), StringComparison.OrdinalIgnoreCase);

                            string planType = "WINDOWS_BUILTIN";
                            if (isOwned)
                            {
                                if (ownedRecords.TryGetValue(guidStr, out var rec) && !string.IsNullOrEmpty(rec.PlanType))
                                {
                                    planType = rec.PlanType;
                                }
                                else if (name.Contains("Battery Efficiency", StringComparison.OrdinalIgnoreCase))
                                {
                                    planType = "AI_BATTERY_EFFICIENCY";
                                }
                                else if (name.Contains("Max Performance", StringComparison.OrdinalIgnoreCase) || name.Contains("AI Performance", StringComparison.OrdinalIgnoreCase))
                                {
                                    planType = "AI_MAX_PERFORMANCE";
                                }
                                else
                                {
                                    planType = "CUSTOM";
                                }
                            }
                            else if (!isBuiltin)
                            {
                                planType = "OEM_VENDOR";
                            }

                            var plan = new PowerPlanItem
                            {
                                Guid = guidStr,
                                SchemeGuid = schemeGuid,
                                Name = name,
                                Description = desc,
                                PlanType = planType,
                                IsInstalled = true,
                                IsSupported = true,
                                IsCustomAiPlan = isOwned && (planType == "AI_BATTERY_EFFICIENCY" || planType == "AI_MAX_PERFORMANCE"),
                                IsErrorOptimizerOwned = isOwned,
                                IsBuiltin = isBuiltin,
                                IsActive = isActive,
                                IsStartupPlan = isStartup,
                                Owner = isOwned ? "ErrorOptimizer" : (isBuiltin ? "Windows" : "OEM"),
                                BasePlanName = isOwned && ownedRecords.TryGetValue(guidStr, out var oRec) && !string.IsNullOrEmpty(oRec.BasePlanGuid) ? oRec.BasePlanGuid : (isBuiltin ? "Standard Windows" : "Vendor Preset"),
                                Status = isActive ? PowerPlanStatus.Active : PowerPlanStatus.Available
                            };

                            // Read AC/DC processor settings for this scheme
                            PopulateSchemeSettings(plan);
                            AssignPlanMetadata(plan);
                            plans.Add(plan);
                        }

                        index++;
                        bufferSize = (uint)Marshal.SizeOf(typeof(Guid));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                // 2. Secondary fallback via powercfg /list if native enumeration returned 0 items
                if (plans.Count == 0)
                {
                    try
                    {
                        var res = TaskExecutionSupervisor.ExecuteProcessAsync(GetPowercfgPath(), "/list", TimeSpan.FromSeconds(4), externalCt: ct).GetAwaiter().GetResult();
                        if (res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.StandardOutput))
                        {
                            using var sr = new StringReader(res.StandardOutput);
                            string? line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (!line.Contains("GUID:", StringComparison.OrdinalIgnoreCase)) continue;

                                var (guid, name) = ParseSchemeLine(line);
                                if (string.IsNullOrEmpty(guid) || !Guid.TryParse(guid, out Guid sGuid)) continue;

                                string normGuid = NormalizeGuid(guid);
                                bool isActive = sGuid.Equals(activeGuid) || line.Contains('*');
                                bool isOwned = ownedRecords.ContainsKey(normGuid) ||
                                               name.Contains("Error Optimizer AI", StringComparison.OrdinalIgnoreCase);
                                bool isBuiltin = IsBuiltinScheme(normGuid);
                                bool isStartup = startupConfig.AutoEnableOnStartup &&
                                                 !string.IsNullOrEmpty(startupConfig.StartupPowerPlanGuid) &&
                                                 normGuid.Equals(NormalizeGuid(startupConfig.StartupPowerPlanGuid), StringComparison.OrdinalIgnoreCase);

                                var plan = new PowerPlanItem
                                {
                                    Guid = normGuid,
                                    SchemeGuid = sGuid,
                                    Name = name,
                                    Description = GetSchemeDescription(sGuid),
                                    IsInstalled = true,
                                    IsSupported = true,
                                    IsCustomAiPlan = isOwned,
                                    IsErrorOptimizerOwned = isOwned,
                                    IsBuiltin = isBuiltin,
                                    IsActive = isActive,
                                    IsStartupPlan = isStartup,
                                    Owner = isOwned ? "ErrorOptimizer" : (isBuiltin ? "Windows" : "OEM"),
                                    BasePlanName = isOwned && ownedRecords.TryGetValue(normGuid, out var rec) && !string.IsNullOrEmpty(rec.BasePlanGuid) ? rec.BasePlanGuid : (isBuiltin ? "Standard Windows" : "Vendor Preset"),
                                    Status = isActive ? PowerPlanStatus.Active : PowerPlanStatus.Available
                                };

                                PopulateSchemeSettings(plan);
                                AssignPlanMetadata(plan);
                                plans.Add(plan);
                            }
                        }
                    }
                    catch { }
                }

                // 3. Ultimate Performance template check
                bool hasUlt = plans.Any(p => p.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase) ||
                                             p.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase));
                if (!hasUlt)
                {
                    bool isWindows10Or11 = Environment.OSVersion.Version.Major >= 10;
                    var ultPlan = new PowerPlanItem
                    {
                        Guid = UltimatePerformanceGuid,
                        SchemeGuid = new Guid(UltimatePerformanceGuid),
                        Name = "Ultimate Performance",
                        Description = "Provides ultimate performance on higher end PCs. Unlocks peak CPU frequency and eliminates micro-stutter.",
                        IsInstalled = false,
                        IsSupported = isWindows10Or11,
                        IsBuiltin = true,
                        Status = isWindows10Or11 ? PowerPlanStatus.AvailableToEnable : PowerPlanStatus.Unsupported,
                        BatteryImpact = "CRITICAL",
                        RiskLevel = "CAUTION"
                    };
                    plans.Add(ultPlan);
                }

                return plans;
            }, ct);
        }

        /// <summary>
        /// Activates the exact target power scheme GUID, reading back and comparing against Windows PowerGetActiveScheme.
        /// If verification fails, rolls back to previous scheme.
        /// </summary>
        public async Task<PowerPlanOperationResult> ApplyPowerPlanAsync(string targetGuidStr, PowerPlanChangeSource source = PowerPlanChangeSource.USER_REQUEST, CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = new PowerPlanOperationResult { Source = source };

                if (string.IsNullOrWhiteSpace(targetGuidStr) || !Guid.TryParse(targetGuidStr, out Guid targetGuid))
                {
                    result.Success = false;
                    result.Message = $"Invalid power scheme GUID: '{targetGuidStr}'.";
                    return result;
                }

                string normalizedTargetGuid = NormalizeGuid(targetGuid.ToString());
                result.TargetSchemeGuid = normalizedTargetGuid;

                lock (_lock)
                {
                    _isApplyingScheme = true;
                    LastTargetGuid = normalizedTargetGuid;
                    LastChangeSource = source;
                    LastChangeTimestamp = DateTime.UtcNow;
                }

                try
                {
                    // 1. Read Authoritative Current Scheme from Windows
                    var (currentGuid, currentName) = GetActiveSchemeNative();
                    string currentGuidStr = NormalizeGuid(currentGuid.ToString());

                    lock (_lock)
                    {
                        _previousActiveSchemeGuid = currentGuidStr;
                        _previousActiveSchemeName = currentName;
                    }

                    // 2. Check if already active
                    if (currentGuid.Equals(targetGuid))
                    {
                        result.Success = true;
                        result.AlreadyActive = true;
                        result.Verified = true;
                        result.ActiveSchemeGuid = currentGuidStr;
                        result.ActiveSchemeName = currentName;
                        result.Message = $"Power scheme '{currentName}' is already active.";

                        if (source == PowerPlanChangeSource.USER_REQUEST)
                        {
                            lock (_lock)
                            {
                                UserLock.UserSelectedPlanGuid = currentGuidStr;
                                UserLock.UserSelectedPlanName = currentName;
                                UserLock.UserSelectedTimestamp = DateTime.UtcNow;
                                UserLock.UserSelectionSource = source;
                            }
                        }

                        AppendChangeLog(new PowerPlanChangeLogEntry
                        {
                            Timestamp = DateTime.UtcNow,
                            PreviousGuid = currentGuidStr,
                            PreviousName = currentName,
                            NewGuid = currentGuidStr,
                            NewName = currentName,
                            Source = source,
                            UserInitiated = (source == PowerPlanChangeSource.USER_REQUEST),
                            ThreadOrTask = Thread.CurrentThread.ManagedThreadId.ToString(),
                            Result = "ALREADY_ACTIVE",
                            Message = result.Message
                        });

                        lock (_lock) { LastActivationResult = result; }
                        return result;
                    }

                    // 3. Native Win32 Activation Call
                    PowerSetActiveScheme(IntPtr.Zero, ref targetGuid);

                    // 4. System-wide commit and Control Panel notification via powercfg /setactive
                    await TaskExecutionSupervisor.ExecuteProcessAsync(
                        GetPowercfgPath(),
                        $"/setactive {normalizedTargetGuid}",
                        TimeSpan.FromSeconds(5),
                        externalCt: ct
                    );

                    // 5. Authoritative Readback Verification directly from Windows
                    var (verifiedGuid, verifiedName) = GetActiveSchemeNative();
                    string verifiedGuidStr = NormalizeGuid(verifiedGuid.ToString());

                    if (verifiedGuid.Equals(targetGuid))
                    {
                        result.Success = true;
                        result.Verified = true;
                        result.ActiveSchemeGuid = verifiedGuidStr;
                        result.ActiveSchemeName = verifiedName;
                        result.Message = $"Successfully activated and verified power scheme: {verifiedName} ({verifiedGuidStr})";

                        if (source == PowerPlanChangeSource.USER_REQUEST)
                        {
                            lock (_lock)
                            {
                                UserLock.UserSelectedPlanGuid = verifiedGuidStr;
                                UserLock.UserSelectedPlanName = verifiedName;
                                UserLock.UserSelectedTimestamp = DateTime.UtcNow;
                                UserLock.UserSelectionSource = source;
                            }
                        }

                        AppendChangeLog(new PowerPlanChangeLogEntry
                        {
                            Timestamp = DateTime.UtcNow,
                            PreviousGuid = currentGuidStr,
                            PreviousName = currentName,
                            NewGuid = verifiedGuidStr,
                            NewName = verifiedName,
                            Source = source,
                            UserInitiated = (source == PowerPlanChangeSource.USER_REQUEST),
                            ThreadOrTask = Thread.CurrentThread.ManagedThreadId.ToString(),
                            Result = "SUCCESS",
                            Message = result.Message
                        });
                    }
                    else
                    {
                        // Rollback to previous scheme
                        if (currentGuid != Guid.Empty && !currentGuid.Equals(targetGuid))
                        {
                            var rollbackGuid = currentGuid;
                            PowerSetActiveScheme(IntPtr.Zero, ref rollbackGuid);
                            await TaskExecutionSupervisor.ExecuteProcessAsync(
                                GetPowercfgPath(),
                                $"/setactive {NormalizeGuid(rollbackGuid.ToString())}",
                                TimeSpan.FromSeconds(5),
                                externalCt: ct
                            );
                        }

                        var (rollbackActualGuid, rollbackActualName) = GetActiveSchemeNative();
                        result.Success = false;
                        result.Verified = false;
                        result.ActiveSchemeGuid = NormalizeGuid(rollbackActualGuid.ToString());
                        result.ActiveSchemeName = rollbackActualName;
                        result.Message = $"ACTIVATION FAILED: Windows reports active scheme is '{rollbackActualName}' ({NormalizeGuid(rollbackActualGuid.ToString())}), expected '{normalizedTargetGuid}'.";

                        AppendChangeLog(new PowerPlanChangeLogEntry
                        {
                            Timestamp = DateTime.UtcNow,
                            PreviousGuid = currentGuidStr,
                            PreviousName = currentName,
                            NewGuid = result.ActiveSchemeGuid,
                            NewName = result.ActiveSchemeName,
                            Source = source,
                            UserInitiated = (source == PowerPlanChangeSource.USER_REQUEST),
                            ThreadOrTask = Thread.CurrentThread.ManagedThreadId.ToString(),
                            Result = "FAILED",
                            Message = result.Message
                        });
                    }

                    lock (_lock) { LastActivationResult = result; }
                    return result;
                }
                finally
                {
                    lock (_lock)
                    {
                        _isApplyingScheme = false;
                    }
                }
            }, ct);
        }

        public const string AI_BATTERY_PLAN_NAME = "Error Optimizer AI — Battery Efficiency";
        public const string AI_MAX_PERF_PLAN_NAME = "Error Optimizer AI — Max Performance";

        /// <summary>
        /// Creates or retrieves the flagship Error Optimizer AI — Max Performance Windows power scheme,
        /// applies machine-specific tuned performance settings, and activates it.
        /// </summary>
        public async Task<PowerPlanOperationResult> CreateOrGetAiMaxPerformancePlanAsync(CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var hwReport = HardwareAnalyzer.Instance.Collect(forceRefresh: true);
                var existingPlans = await DiscoverPowerPlansAsync(ct);

                // Check if an AI Max Performance plan already exists
                var existingMaxPerf = existingPlans.FirstOrDefault(p => (p.IsAiMaxPerformance || (p.IsErrorOptimizerOwned && (p.Name.Contains("Max Performance", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("AI Performance", StringComparison.OrdinalIgnoreCase)))) && p.IsInstalled);
                if (existingMaxPerf != null)
                {
                    // Ensure settings are tuned to maximum practical performance
                    if (Guid.TryParse(existingMaxPerf.Guid, out Guid maxGuid))
                    {
                        PowerWriteFriendlyName(IntPtr.Zero, ref maxGuid, IntPtr.Zero, IntPtr.Zero, AI_MAX_PERF_PLAN_NAME, (uint)((AI_MAX_PERF_PLAN_NAME.Length + 1) * 2));
                        await TaskExecutionSupervisor.ExecuteProcessAsync(
                            GetPowercfgPath(),
                            $"-changename {existingMaxPerf.Guid} \"{AI_MAX_PERF_PLAN_NAME}\"",
                            TimeSpan.FromSeconds(3),
                            externalCt: ct
                        );

                        RecordOwnership(new PowerPlanOwnershipRecord
                        {
                            Guid = existingMaxPerf.Guid,
                            Name = AI_MAX_PERF_PLAN_NAME,
                            PlanType = "AI_MAX_PERFORMANCE",
                            Owner = "ErrorOptimizer"
                        });

                        Guid subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                        var settings = new List<PowerPlanSettingItem>();
                        ApplyAndVerifySetting(maxGuid, subGroup, GUID_PROCTHROTTLEMIN, "Minimum Processor State", 100, 5, settings);
                        ApplyAndVerifySetting(maxGuid, subGroup, GUID_PROCTHROTTLEMAX, "Maximum Processor State", 100, 100, settings);
                        ApplyAndVerifySetting(maxGuid, subGroup, GUID_SYSCOOLPOL, "System Cooling Policy", 0, 0, settings);
                        ApplyAndVerifySetting(maxGuid, subGroup, GUID_PERFBOOSTMODE, "Processor Performance Boost Mode", 2, 1, settings);
                    }

                    return await ApplyPowerPlanAsync(existingMaxPerf.Guid, PowerPlanChangeSource.AI_POLICY, ct);
                }

                // Choose base scheme: Ultimate Performance -> High Performance -> Balanced -> any installed
                var basePlan = existingPlans.FirstOrDefault(p => (p.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase)) && p.IsInstalled)
                            ?? existingPlans.FirstOrDefault(p => (p.Guid.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) || p.Guid.Equals(HighPerformanceAltGuid, StringComparison.OrdinalIgnoreCase)) && p.IsInstalled)
                            ?? existingPlans.FirstOrDefault(p => p.Guid.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase) && p.IsInstalled)
                            ?? existingPlans.FirstOrDefault(p => p.IsInstalled);

                string baseGuidStr = basePlan?.Guid ?? BalancedGuid;
                string planName = AI_MAX_PERF_PLAN_NAME;
                string planDesc = $"Machine-specific maximum performance policy for {hwReport.CpuName} ({hwReport.PhysicalCores} Cores, {hwReport.RamTotalGb:F0}GB RAM).";

                var createRes = await CreateCustomPowerPlanAsync(baseGuidStr, planName, planDesc, "AI_MAX_PERFORMANCE", ct);
                if (!createRes.Success) return createRes;

                // Configure performance settings: AC 100% min/max, DC 5% min / 100% max, Boost AC 2 (Aggressive) / DC 1 (Enabled), Cooling Active
                if (Guid.TryParse(createRes.TargetSchemeGuid, out Guid newGuid))
                {
                    Guid subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                    var settings = new List<PowerPlanSettingItem>();
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_PROCTHROTTLEMIN, "Minimum Processor State", 100, 5, settings);
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_PROCTHROTTLEMAX, "Maximum Processor State", 100, 100, settings);
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_SYSCOOLPOL, "System Cooling Policy", 0, 0, settings);
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_PERFBOOSTMODE, "Processor Performance Boost Mode", 2, 1, settings);
                    createRes.VerifiedSettings = settings;
                }

                // Activate the newly created plan
                var activationResult = await ApplyPowerPlanAsync(createRes.TargetSchemeGuid, PowerPlanChangeSource.AI_POLICY, ct);
                activationResult.VerifiedSettings = createRes.VerifiedSettings;
                return activationResult;
            }, ct);
        }

        /// <summary>
        /// Creates or retrieves the Error Optimizer AI — Battery Efficiency Windows power scheme,
        /// applies machine-specific tuned battery-saving settings, and activates it.
        /// </summary>
        public async Task<PowerPlanOperationResult> CreateOrGetAiBatteryEfficiencyPlanAsync(CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var hwReport = HardwareAnalyzer.Instance.Collect(forceRefresh: true);
                var existingPlans = await DiscoverPowerPlansAsync(ct);

                // Check if an AI Battery Efficiency plan already exists
                var existingBattery = existingPlans.FirstOrDefault(p => (p.IsAiBatteryEfficiency || (p.IsErrorOptimizerOwned && p.Name.Contains("Battery Efficiency", StringComparison.OrdinalIgnoreCase))) && p.IsInstalled);
                if (existingBattery != null)
                {
                    // Ensure settings are tuned to maximum practical battery savings
                    if (Guid.TryParse(existingBattery.Guid, out Guid batGuid))
                    {
                        Guid subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                        var settings = new List<PowerPlanSettingItem>();
                        ApplyAndVerifySetting(batGuid, subGroup, GUID_PROCTHROTTLEMIN, "Minimum Processor State", 5, 5, settings);
                        ApplyAndVerifySetting(batGuid, subGroup, GUID_PROCTHROTTLEMAX, "Maximum Processor State", 100, 75, settings);
                        ApplyAndVerifySetting(batGuid, subGroup, GUID_SYSCOOLPOL, "System Cooling Policy", 0, 1, settings);
                        ApplyAndVerifySetting(batGuid, subGroup, GUID_PERFBOOSTMODE, "Processor Performance Boost Mode", 1, 0, settings);
                    }

                    return await ApplyPowerPlanAsync(existingBattery.Guid, PowerPlanChangeSource.AI_POLICY, ct);
                }

                // Choose base scheme: Power Saver -> Balanced -> any installed
                var basePlan = existingPlans.FirstOrDefault(p => (p.Guid.Equals(PowerSaverGuid, StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Power Saver", StringComparison.OrdinalIgnoreCase)) && p.IsInstalled)
                            ?? existingPlans.FirstOrDefault(p => p.Guid.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase) && p.IsInstalled)
                            ?? existingPlans.FirstOrDefault(p => p.IsInstalled);

                string baseGuidStr = basePlan?.Guid ?? PowerSaverGuid;
                string planName = AI_BATTERY_PLAN_NAME;
                string planDesc = $"Machine-specific battery efficiency profile for {hwReport.CpuName} ({hwReport.PhysicalCores} Cores, {hwReport.RamTotalGb:F0}GB RAM).";

                var createRes = await CreateCustomPowerPlanAsync(baseGuidStr, planName, planDesc, "AI_BATTERY_EFFICIENCY", ct);
                if (!createRes.Success) return createRes;

                // Configure battery settings: AC 5% min / 100% max, DC 5% min / 75% max, Boost AC 1 / DC 0 (Disabled), Cooling AC Active / DC Passive
                if (Guid.TryParse(createRes.TargetSchemeGuid, out Guid newGuid))
                {
                    Guid subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                    var settings = new List<PowerPlanSettingItem>();
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_PROCTHROTTLEMIN, "Minimum Processor State", 5, 5, settings);
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_PROCTHROTTLEMAX, "Maximum Processor State", 100, 75, settings);
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_SYSCOOLPOL, "System Cooling Policy", 0, 1, settings);
                    ApplyAndVerifySetting(newGuid, subGroup, GUID_PERFBOOSTMODE, "Processor Performance Boost Mode", 1, 0, settings);
                    createRes.VerifiedSettings = settings;
                }

                // Activate the newly created plan
                var activationResult = await ApplyPowerPlanAsync(createRes.TargetSchemeGuid, PowerPlanChangeSource.AI_POLICY, ct);
                activationResult.VerifiedSettings = createRes.VerifiedSettings;
                return activationResult;
            }, ct);
        }

        /// <summary>
        /// Legacy alias routing to CreateOrGetAiMaxPerformancePlanAsync.
        /// </summary>
        public async Task<PowerPlanOperationResult> CreateCustomAiPlanAsync(CancellationToken ct = default)
        {
            return await CreateOrGetAiMaxPerformancePlanAsync(ct);
        }

        /// <summary>
        /// Creates a new named custom power plan duplicated from a real base scheme, configured, and ownership tagged.
        /// </summary>
        public async Task<PowerPlanOperationResult> CreateCustomPowerPlanAsync(
            string baseSchemeGuidStr,
            string planName,
            string planDesc,
            string planType = "CUSTOM",
            CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = new PowerPlanOperationResult();
                string normBase = NormalizeGuid(baseSchemeGuidStr);
                if (string.IsNullOrWhiteSpace(normBase) || !Guid.TryParse(normBase, out Guid baseGuid))
                {
                    baseGuid = new Guid(BalancedGuid);
                    normBase = BalancedGuid;
                }

                if (string.IsNullOrWhiteSpace(planName)) planName = "Error Optimizer Custom Performance";
                if (string.IsNullOrWhiteSpace(planDesc)) planDesc = "Custom calibrated Windows power plan.";

                Guid newSchemeGuid = Guid.Empty;

                // 1. Native Duplicate Scheme
                IntPtr pDest;
                uint nativeDup = PowerDuplicateScheme(IntPtr.Zero, ref baseGuid, out pDest);
                if (nativeDup == 0 && pDest != IntPtr.Zero)
                {
                    try
                    {
                        object? structObj = Marshal.PtrToStructure(pDest, typeof(Guid));
                        if (structObj is Guid g) newSchemeGuid = g;
                    }
                    finally
                    {
                        LocalFree(pDest);
                    }
                }

                // Fallback CLI duplicate
                if (newSchemeGuid == Guid.Empty)
                {
                    var dupRes = await TaskExecutionSupervisor.ExecuteProcessAsync(
                        GetPowercfgPath(),
                        $"-duplicatescheme {normBase}",
                        TimeSpan.FromSeconds(8),
                        externalCt: ct
                    );

                    if (dupRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(dupRes.StandardOutput))
                    {
                        var (parsedGuidStr, _) = ParseSchemeLine(dupRes.StandardOutput);
                        if (Guid.TryParse(parsedGuidStr, out Guid parsedGuid))
                        {
                            newSchemeGuid = parsedGuid;
                        }
                    }
                }

                if (newSchemeGuid == Guid.Empty)
                {
                    result.Success = false;
                    result.Message = "Windows failed to duplicate base power scheme.";
                    return result;
                }

                string newGuidStr = NormalizeGuid(newSchemeGuid.ToString());

                // 2. Persist Ownership Record immediately
                RecordOwnership(new PowerPlanOwnershipRecord
                {
                    Guid = newGuidStr,
                    BasePlanGuid = normBase,
                    Name = planName,
                    PlanType = planType,
                    Owner = "ErrorOptimizer",
                    CreationTime = DateTime.UtcNow,
                    Version = "3.0"
                });

                // 3. Rename Scheme via Win32 + powercfg
                PowerWriteFriendlyName(IntPtr.Zero, ref newSchemeGuid, IntPtr.Zero, IntPtr.Zero, planName, (uint)((planName.Length + 1) * 2));
                PowerWriteDescription(IntPtr.Zero, ref newSchemeGuid, IntPtr.Zero, IntPtr.Zero, planDesc, (uint)((planDesc.Length + 1) * 2));

                await TaskExecutionSupervisor.ExecuteProcessAsync(
                    GetPowercfgPath(),
                    $"-changename {newGuidStr} \"{planName}\" \"{planDesc}\"",
                    TimeSpan.FromSeconds(4),
                    externalCt: ct
                );

                // 4. Tune default processor settings if custom
                Guid subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                var verifiedSettings = new List<PowerPlanSettingItem>();
                ApplyAndVerifySetting(newSchemeGuid, subGroup, GUID_PROCTHROTTLEMIN, "Minimum Processor State", 100, 5, verifiedSettings);
                ApplyAndVerifySetting(newSchemeGuid, subGroup, GUID_PROCTHROTTLEMAX, "Maximum Processor State", 100, 100, verifiedSettings);
                ApplyAndVerifySetting(newSchemeGuid, subGroup, GUID_SYSCOOLPOL, "System Cooling Policy", 0, 0, verifiedSettings);
                ApplyAndVerifySetting(newSchemeGuid, subGroup, GUID_PERFBOOSTMODE, "Processor Performance Boost Mode", 2, 1, verifiedSettings);

                result.Success = true;
                result.Verified = true;
                result.TargetSchemeGuid = newGuidStr;
                result.ActiveSchemeGuid = newGuidStr;
                result.ActiveSchemeName = planName;
                result.VerifiedSettings = verifiedSettings;
                result.Message = $"Custom power plan '{planName}' created successfully ({newGuidStr}).";

                return result;
            }, ct);
        }

        /// <summary>
        /// Updates a specific AC/DC power setting on a scheme, verifies readback value, and returns verified status.
        /// </summary>
        public async Task<PowerPlanOperationResult> UpdatePlanSettingAsync(
            string schemeGuidStr,
            Guid subGroupGuid,
            Guid settingGuid,
            string settingName,
            uint targetAC,
            uint targetDC,
            CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = new PowerPlanOperationResult();
                string normGuid = NormalizeGuid(schemeGuidStr);

                if (!Guid.TryParse(normGuid, out Guid schemeGuid))
                {
                    result.Success = false;
                    result.Message = $"Invalid scheme GUID: '{schemeGuidStr}'.";
                    return result;
                }

                uint retAc = PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroupGuid, ref settingGuid, targetAC);
                uint retDc = PowerWriteDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroupGuid, ref settingGuid, targetDC);

                // Also call powercfg -setacvalueindex and -setdcvalueindex to sync with shell
                await TaskExecutionSupervisor.ExecuteProcessAsync(
                    GetPowercfgPath(),
                    $"-setacvalueindex {normGuid} {subGroupGuid} {settingGuid} {targetAC}",
                    TimeSpan.FromSeconds(3),
                    externalCt: ct
                );
                await TaskExecutionSupervisor.ExecuteProcessAsync(
                    GetPowercfgPath(),
                    $"-setdcvalueindex {normGuid} {subGroupGuid} {settingGuid} {targetDC}",
                    TimeSpan.FromSeconds(3),
                    externalCt: ct
                );

                // Readback verification
                uint readAc = 0, readDc = 0;
                uint readRetAc = PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroupGuid, ref settingGuid, out readAc);
                uint readRetDc = PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroupGuid, ref settingGuid, out readDc);

                bool verified = (readRetAc == 0 && readAc == targetAC) && (readRetDc == 0 && readDc == targetDC);

                result.Success = verified;
                result.Verified = verified;
                result.TargetSchemeGuid = normGuid;
                result.Message = verified
                    ? $"Setting '{settingName}' applied and verified: AC={readAc}, DC={readDc}."
                    : $"Verification failed for '{settingName}': AC target={targetAC}/read={readAc}, DC target={targetDC}/read={readDc}.";

                result.VerifiedSettings.Add(new PowerPlanSettingItem
                {
                    SettingGuid = settingGuid,
                    SubgroupGuid = subGroupGuid,
                    Name = settingName,
                    CurrentAC = readAc,
                    TargetAC = targetAC,
                    CurrentDC = readDc,
                    TargetDC = targetDC,
                    Applied = (retAc == 0 && retDc == 0),
                    Verified = verified
                });

                return result;
            }, ct);
        }

        /// <summary>
        /// Deletes a custom power scheme created by Error Optimizer.
        /// Windows/OEM/Builtin plans are strictly protected from deletion.
        /// </summary>
        public async Task<PowerPlanOperationResult> DeleteCustomAiPlanAsync(string guidStr, CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = new PowerPlanOperationResult();
                string normGuid = NormalizeGuid(guidStr);

                if (!Guid.TryParse(normGuid, out Guid schemeGuid))
                {
                    result.Success = false;
                    result.Message = "Invalid GUID.";
                    return result;
                }

                // Protected: Never delete Windows built-in or OEM standard plans
                if (IsBuiltinScheme(normGuid))
                {
                    result.Success = false;
                    result.Message = "Cannot delete: Windows built-in power schemes are protected.";
                    return result;
                }

                // Verify Error Optimizer ownership
                var owned = GetOwnershipRecords();
                if (!owned.ContainsKey(normGuid))
                {
                    result.Success = false;
                    result.Message = "Cannot delete: Plan is not owned by Error Optimizer.";
                    return result;
                }

                // If this plan was configured for startup auto-apply, clear it immediately
                var startupConfig = GetStartupConfig();
                if (normGuid.Equals(NormalizeGuid(startupConfig.StartupPowerPlanGuid), StringComparison.OrdinalIgnoreCase))
                {
                    startupConfig.StartupPowerPlanGuid = string.Empty;
                    startupConfig.StartupPowerPlanName = string.Empty;
                    startupConfig.AutoEnableOnStartup = false;
                    startupConfig.LastStartupExecutionResult = "PLAN DELETED";
                    startupConfig.LastStartupExecutionMessage = "Startup power plan was deleted by user.";
                    SaveStartupConfig(startupConfig);
                }

                // If currently active, switch back to Balanced first
                var (currentGuid, _) = GetActiveSchemeNative();
                if (currentGuid.Equals(schemeGuid))
                {
                    await ApplyPowerPlanAsync(BalancedGuid, PowerPlanChangeSource.RESTORE, ct);
                }

                // Delete via Native Win32 API
                uint delRet = PowerDeleteScheme(IntPtr.Zero, ref schemeGuid);

                // Fallback CLI delete
                if (delRet != 0)
                {
                    await TaskExecutionSupervisor.ExecuteProcessAsync(
                        GetPowercfgPath(),
                        $"-deletescheme {normGuid}",
                        TimeSpan.FromSeconds(5),
                        externalCt: ct
                    );
                }

                RemoveOwnership(normGuid);

                // Verify deletion from Windows
                var remainingPlans = await DiscoverPowerPlansAsync(ct);
                bool stillExists = remainingPlans.Any(p => p.Guid.Equals(normGuid, StringComparison.OrdinalIgnoreCase) && p.IsInstalled);

                if (!stillExists)
                {
                    result.Success = true;
                    result.Verified = true;
                    result.Message = "Custom power plan deleted and verified removed from Windows.";
                }
                else
                {
                    result.Success = false;
                    result.Verified = false;
                    result.Message = "Warning: Windows still reports the power scheme after deletion.";
                }

                return result;
            }, ct);
        }

        #region Startup Auto-Apply Configuration & Execution

        public StartupPowerPlanConfig GetStartupConfig()
        {
            var config = new StartupPowerPlanConfig();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REG_POWER_CONFIG_KEY);
                if (key != null)
                {
                    config.StartupPowerPlanGuid = key.GetValue("StartupPowerPlanGuid")?.ToString() ?? string.Empty;
                    config.StartupPowerPlanName = key.GetValue("StartupPowerPlanName")?.ToString() ?? string.Empty;
                    config.AutoEnableOnStartup = (key.GetValue("AutoEnableOnStartup")?.ToString() ?? "0") == "1";
                    config.ContinuousEnforcement = (key.GetValue("ContinuousEnforcement")?.ToString() ?? "0") == "1";
                    config.LastStartupExecutionResult = key.GetValue("LastStartupExecutionResult")?.ToString() ?? "READY";
                    config.LastStartupExecutionMessage = key.GetValue("LastStartupExecutionMessage")?.ToString() ?? string.Empty;
                    if (DateTime.TryParse(key.GetValue("LastStartupExecutionTime")?.ToString(), out var dt))
                    {
                        config.LastStartupExecutionTime = dt;
                    }
                }
            }
            catch { }
            return config;
        }

        public void SaveStartupConfig(StartupPowerPlanConfig config)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(REG_POWER_CONFIG_KEY);
                if (key != null)
                {
                    key.SetValue("StartupPowerPlanGuid", config.StartupPowerPlanGuid ?? string.Empty);
                    key.SetValue("StartupPowerPlanName", config.StartupPowerPlanName ?? string.Empty);
                    key.SetValue("AutoEnableOnStartup", config.AutoEnableOnStartup ? "1" : "0");
                    key.SetValue("ContinuousEnforcement", config.ContinuousEnforcement ? "1" : "0");
                    key.SetValue("LastStartupExecutionResult", config.LastStartupExecutionResult ?? "READY");
                    key.SetValue("LastStartupExecutionMessage", config.LastStartupExecutionMessage ?? string.Empty);
                    if (config.LastStartupExecutionTime.HasValue)
                    {
                        key.SetValue("LastStartupExecutionTime", config.LastStartupExecutionTime.Value.ToString("o"));
                    }
                }
            }
            catch { }
        }

                public void StartBackgroundEnforcement()
        {
            lock (_lock)
            {
                if (_enforcementTask != null && !_enforcementTask.IsCompleted) return;

                _enforcementCts = new CancellationTokenSource();
                _enforcementTask = Task.Run(() => BackgroundEnforcementLoopAsync(_enforcementCts.Token));
            }
        }

        public void StopBackgroundEnforcement()
        {
            lock (_lock)
            {
                _enforcementCts?.Cancel();
                _enforcementCts = null;
                _enforcementTask = null;
            }
        }

        private async Task BackgroundEnforcementLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var config = GetStartupConfig();
                    if (config.AutoEnableOnStartup && !string.IsNullOrWhiteSpace(config.StartupPowerPlanGuid))
                    {
                        string targetGuidStr = NormalizeGuid(config.StartupPowerPlanGuid);
                        var (currentGuid, currentName) = GetActiveSchemeNative();
                        string currentGuidStr = NormalizeGuid(currentGuid.ToString());

                        if (!currentGuidStr.Equals(targetGuidStr, StringComparison.OrdinalIgnoreCase))
                        {
                            // State mismatch detected (e.g. Windows/OEM reverted to Balanced)
                            // Enforce and re-apply target plan
                            var applyResult = await ApplyPowerPlanAsync(targetGuidStr, PowerPlanChangeSource.AI_POLICY, ct);
                            if (applyResult.Success && applyResult.Verified)
                            {
                                config.LastStartupExecutionTime = DateTime.UtcNow;
                                config.LastStartupExecutionResult = "ENFORCED & VERIFIED";
                                config.LastStartupExecutionMessage = $"AI Power Plan automatically re-enforced '{applyResult.ActiveSchemeName}' after detecting external change.";
                                SaveStartupConfig(config);
                            }
                        }
                    }
                }
                catch { }

                try { await Task.Delay(10000, ct); } // Check every 10 seconds (0.00% CPU)
                catch (OperationCanceledException) { break; }
            }
        }

        public async Task<PowerPlanOperationResult> SetStartupAutoEnableAsync(string targetGuidStr, bool enable, CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = new PowerPlanOperationResult();
                string normGuid = NormalizeGuid(targetGuidStr);
                var config = GetStartupConfig();

                if (enable)
                {
                    if (string.IsNullOrWhiteSpace(normGuid) || !Guid.TryParse(normGuid, out Guid parsedGuid))
                    {
                        result.Success = false;
                        result.Message = $"Invalid target GUID: '{targetGuidStr}'.";
                        return result;
                    }

                    var plans = await DiscoverPowerPlansAsync(ct);
                    var match = plans.FirstOrDefault(p => p.Guid.Equals(normGuid, StringComparison.OrdinalIgnoreCase));
                    if (match == null || !match.IsInstalled)
                    {
                        result.Success = false;
                        result.Message = $"Power plan '{normGuid}' is not installed in Windows.";
                        return result;
                    }

                    config.StartupPowerPlanGuid = normGuid;
                    config.StartupPowerPlanName = match.Name;
                    config.AutoEnableOnStartup = true;
                    config.LastStartupExecutionResult = "READY";
                    config.LastStartupExecutionMessage = $"Configured to auto-activate '{match.Name}' on startup.";
                    SaveStartupConfig(config);

                    result.Success = true;
                    result.Verified = true;
                    result.TargetSchemeGuid = normGuid;
                    result.Message = $"Power plan '{match.Name}' is configured to Auto-Enable on Windows Startup.";
                }
                else
                {
                    config.StartupPowerPlanGuid = string.Empty;
                    config.StartupPowerPlanName = string.Empty;
                    config.AutoEnableOnStartup = false;
                    config.LastStartupExecutionResult = "DISABLED";
                    config.LastStartupExecutionMessage = "Startup auto-apply is disabled.";
                    SaveStartupConfig(config);

                    result.Success = true;
                    result.Verified = true;
                    result.Message = "Startup auto-enable disabled.";
                }

                return result;
            }, ct);
        }

        public async Task<PowerPlanOperationResult> InitializeStartupAutoApplyAsync(CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var config = GetStartupConfig();
                if (!config.AutoEnableOnStartup || string.IsNullOrWhiteSpace(config.StartupPowerPlanGuid))
                {
                    return new PowerPlanOperationResult
                    {
                        Success = true,
                        Message = "Startup auto-apply is disabled or not configured."
                    };
                }

                // Bounded startup readiness delay to allow Windows power subsystem to initialize
                await Task.Delay(800, ct);

                string targetGuidStr = NormalizeGuid(config.StartupPowerPlanGuid);
                var plans = await DiscoverPowerPlansAsync(ct);
                var match = plans.FirstOrDefault(p => p.Guid.Equals(targetGuidStr, StringComparison.OrdinalIgnoreCase) && p.IsInstalled);

                if (match == null)
                {
                    config.AutoEnableOnStartup = false;
                    config.LastStartupExecutionTime = DateTime.UtcNow;
                    config.LastStartupExecutionResult = "STARTUP POWER PLAN UNAVAILABLE";
                    config.LastStartupExecutionMessage = $"Saved power plan '{config.StartupPowerPlanName}' ({targetGuidStr}) was deleted or is no longer present in Windows.";
                    SaveStartupConfig(config);

                    return new PowerPlanOperationResult
                    {
                        Success = false,
                        Message = config.LastStartupExecutionMessage,
                        TargetSchemeGuid = targetGuidStr
                    };
                }

                var (currentGuid, currentName) = GetActiveSchemeNative();
                if (currentGuid.ToString().Equals(targetGuidStr, StringComparison.OrdinalIgnoreCase))
                {
                    config.LastStartupExecutionTime = DateTime.UtcNow;
                    config.LastStartupExecutionResult = "ALREADY ACTIVE & VERIFIED";
                    config.LastStartupExecutionMessage = $"Power plan '{match.Name}' was already active upon startup.";
                    SaveStartupConfig(config);

                    return new PowerPlanOperationResult
                    {
                        Success = true,
                        AlreadyActive = true,
                        Verified = true,
                        ActiveSchemeGuid = targetGuidStr,
                        ActiveSchemeName = match.Name,
                        Message = config.LastStartupExecutionMessage
                    };
                }

                // Attempt activation with max 3 bounded retries
                PowerPlanOperationResult? lastResult = null;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    lastResult = await ApplyPowerPlanAsync(targetGuidStr, PowerPlanChangeSource.STARTUP, ct);
                    if (lastResult.Success && lastResult.Verified)
                    {
                        config.LastStartupExecutionTime = DateTime.UtcNow;
                        config.LastStartupExecutionResult = "ACTIVE & VERIFIED";
                        config.LastStartupExecutionMessage = $"Successfully auto-activated '{match.Name}' on startup (Attempt {attempt}).";
                        SaveStartupConfig(config);
                        StartBackgroundEnforcement();
                        return lastResult;
                    }

                    await Task.Delay(500, ct);
                }

                config.LastStartupExecutionTime = DateTime.UtcNow;
                config.LastStartupExecutionResult = "STARTUP POWER PLAN FAILED";
                config.LastStartupExecutionMessage = $"Failed to activate '{match.Name}' on startup after 3 attempts. (Windows reported: {lastResult?.ActiveSchemeName})";
                SaveStartupConfig(config);

                return lastResult ?? new PowerPlanOperationResult
                {
                    Success = false,
                    Message = config.LastStartupExecutionMessage,
                    TargetSchemeGuid = targetGuidStr
                };
            }, ct);
        }

        #endregion

        /// <summary>
        /// Restores the previously active power plan GUID saved before the last change.
        /// </summary>
        public async Task<PowerPlanOperationResult> RestorePreviousPowerPlanAsync(CancellationToken ct = default)
        {
            string prevGuid;
            lock (_lock)
            {
                prevGuid = _previousActiveSchemeGuid ?? BalancedGuid;
            }
            return await ApplyPowerPlanAsync(prevGuid, PowerPlanChangeSource.RESTORE, ct);
        }

        /// <summary>
        /// Enables the Ultimate Performance power scheme if supported on this PC.
        /// </summary>
        public async Task<PowerPlanOperationResult> EnableUltimatePerformanceAsync(CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                // Check if already installed
                var plans = await DiscoverPowerPlansAsync(ct);
                var existingUlt = plans.FirstOrDefault(p => p.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase) && p.IsInstalled);
                if (existingUlt != null)
                {
                    return await ApplyPowerPlanAsync(existingUlt.Guid, PowerPlanChangeSource.USER_REQUEST, ct);
                }

                // Duplicate the well-known Ultimate Performance template
                var dupRes = await TaskExecutionSupervisor.ExecuteProcessAsync(
                    GetPowercfgPath(),
                    $"-duplicatescheme {UltimatePerformanceGuid}",
                    TimeSpan.FromSeconds(8),
                    externalCt: ct
                );

                if (dupRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(dupRes.StandardOutput))
                {
                    var (parsedGuidStr, _) = ParseSchemeLine(dupRes.StandardOutput);
                    if (!string.IsNullOrEmpty(parsedGuidStr))
                    {
                        return await ApplyPowerPlanAsync(parsedGuidStr, PowerPlanChangeSource.USER_REQUEST, ct);
                    }
                }

                return await ApplyPowerPlanAsync(UltimatePerformanceGuid, PowerPlanChangeSource.USER_REQUEST, ct);
            }, ct);
        }

        public PowerPlanRecommendation GenerateRecommendation(List<PowerPlanItem> plans)
        {
            var hw = HardwareAnalyzer.Instance.Collect();
            return HardwareAnalyzer.Instance.Recommend(hw, plans);
        }

        #region Helpers & Native Read/Write

        public static string NormalizeGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return string.Empty;
            return guid.Trim().Trim('{', '}').ToLowerInvariant();
        }

        private static bool IsBuiltinScheme(string guidStr)
        {
            return guidStr.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase) ||
                   guidStr.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ||
                   guidStr.Equals(HighPerformanceAltGuid, StringComparison.OrdinalIgnoreCase) ||
                   guidStr.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase) ||
                   guidStr.Equals(PowerSaverGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadSchemeFriendlyName(Guid schemeGuid)
        {
            uint bufferSize = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref bufferSize);
            if (bufferSize > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
                try
                {
                    if (PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref bufferSize) == 0)
                    {
                        string name = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            string norm = NormalizeGuid(schemeGuid.ToString());
            if (norm.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase)) return "Balanced";
            if (norm.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) || norm.Equals(HighPerformanceAltGuid, StringComparison.OrdinalIgnoreCase)) return "High performance";
            if (norm.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase)) return "Ultimate Performance";
            if (norm.Equals(PowerSaverGuid, StringComparison.OrdinalIgnoreCase)) return "Power saver";

            return "Windows Power Scheme";
        }

        private static string ReadSchemeDescription(Guid schemeGuid)
        {
            uint bufferSize = 0;
            PowerReadDescription(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref bufferSize);
            if (bufferSize > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
                try
                {
                    if (PowerReadDescription(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref bufferSize) == 0)
                    {
                        string desc = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(desc)) return desc;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return "Windows configured power and performance scheme.";
        }

        private static string GetSchemeDescription(Guid schemeGuid)
        {
            return ReadSchemeDescription(schemeGuid);
        }

        private static void PopulateSchemeSettings(PowerPlanItem plan)
        {
            try
            {
                Guid schemeGuid = plan.SchemeGuid;
                Guid subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;

                // Processor Throttle Min
                Guid minGuid = GUID_PROCTHROTTLEMIN;
                if (PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref minGuid, out uint acMin) == 0 &&
                    PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref minGuid, out uint dcMin) == 0)
                {
                    plan.Settings.Add(new PowerPlanSettingItem
                    {
                        SettingGuid = minGuid,
                        SubgroupGuid = subGroup,
                        Name = "Minimum Processor State",
                        Description = "Sets the minimum performance state (frequency) of processor cores.",
                        CurrentAC = acMin,
                        TargetAC = acMin,
                        CurrentDC = dcMin,
                        TargetDC = dcMin,
                        MinValue = 0,
                        MaxValue = 100,
                        Unit = "%",
                        Supported = true,
                        Applied = true,
                        Verified = true
                    });
                }

                // Processor Throttle Max
                Guid maxGuid = GUID_PROCTHROTTLEMAX;
                if (PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref maxGuid, out uint acMax) == 0 &&
                    PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref maxGuid, out uint dcMax) == 0)
                {
                    plan.Settings.Add(new PowerPlanSettingItem
                    {
                        SettingGuid = maxGuid,
                        SubgroupGuid = subGroup,
                        Name = "Maximum Processor State",
                        Description = "Sets the maximum performance state (frequency) of processor cores.",
                        CurrentAC = acMax,
                        TargetAC = acMax,
                        CurrentDC = dcMax,
                        TargetDC = dcMax,
                        MinValue = 0,
                        MaxValue = 100,
                        Unit = "%",
                        Supported = true,
                        Applied = true,
                        Verified = true
                    });
                }

                // System Cooling Policy (0 = Active, 1 = Passive)
                Guid coolGuid = GUID_SYSCOOLPOL;
                if (PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref coolGuid, out uint acCool) == 0 &&
                    PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref coolGuid, out uint dcCool) == 0)
                {
                    plan.Settings.Add(new PowerPlanSettingItem
                    {
                        SettingGuid = coolGuid,
                        SubgroupGuid = subGroup,
                        Name = "System Cooling Policy",
                        Description = "0 = Active (Increase fan before throttling), 1 = Passive (Throttle before fan).",
                        CurrentAC = acCool,
                        TargetAC = acCool,
                        CurrentDC = dcCool,
                        TargetDC = dcCool,
                        MinValue = 0,
                        MaxValue = 1,
                        Unit = "",
                        Supported = true,
                        Applied = true,
                        Verified = true
                    });
                }

                // Processor Performance Boost Mode (0 = Disabled, 1 = Enabled, 2 = Aggressive, 3 = Efficient, 4 = Efficient Aggressive)
                Guid boostGuid = GUID_PERFBOOSTMODE;
                if (PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref boostGuid, out uint acBoost) == 0 &&
                    PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref boostGuid, out uint dcBoost) == 0)
                {
                    plan.Settings.Add(new PowerPlanSettingItem
                    {
                        SettingGuid = boostGuid,
                        SubgroupGuid = subGroup,
                        Name = "Processor Performance Boost Mode",
                        Description = "Controls dynamic CPU turbo frequency behavior (0=Disabled, 1=Enabled, 2=Aggressive).",
                        CurrentAC = acBoost,
                        TargetAC = acBoost,
                        CurrentDC = dcBoost,
                        TargetDC = dcBoost,
                        MinValue = 0,
                        MaxValue = 4,
                        Unit = "",
                        Supported = true,
                        Applied = true,
                        Verified = true
                    });
                }
            }
            catch { }
        }

        private static void ApplyAndVerifySetting(
            Guid schemeGuid,
            Guid subGroup,
            Guid settingGuid,
            string name,
            uint targetAC,
            uint targetDC,
            List<PowerPlanSettingItem> resultList)
        {
            var item = new PowerPlanSettingItem
            {
                SettingGuid = settingGuid,
                SubgroupGuid = subGroup,
                Name = name,
                TargetAC = targetAC,
                TargetDC = targetDC
            };

            // Write AC and DC values
            uint retAc = PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref settingGuid, targetAC);
            uint retDc = PowerWriteDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref settingGuid, targetDC);
            item.Applied = (retAc == 0 || retDc == 0);

            // Readback Verification
            uint readAc = 0, readDc = 0;
            uint readRetAc = PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref settingGuid, out readAc);
            uint readRetDc = PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref settingGuid, out readDc);

            item.CurrentAC = readAc;
            item.CurrentDC = readDc;
            item.Verified = (readRetAc == 0 && readAc == targetAC) && (readRetDc == 0 && readDc == targetDC);

            resultList.Add(item);
        }

        public void AssignMetadataExternal(PowerPlanItem plan) => AssignPlanMetadata(plan);

        private static void AssignPlanMetadata(PowerPlanItem plan)
        {
            if (plan.IsAiBatteryEfficiency)
            {
                plan.PerformanceProfileText = "AI BATTERY EFFICIENCY";
                plan.AcPerformanceProfile = "BALANCED EFFICIENCY";
                plan.DcPerformanceProfile = "MAX BATTERY EFFICIENCY";
                plan.BatteryImpact = "BATTERY EFFICIENCY";
                plan.RiskLevel = "SAFE";
                return;
            }

            if (plan.IsAiMaxPerformance)
            {
                plan.PerformanceProfileText = "AI MAX PERFORMANCE";
                plan.AcPerformanceProfile = "MAX PERFORMANCE";
                plan.DcPerformanceProfile = "PERFORMANCE / BATTERY LIMITED";
                plan.BatteryImpact = "MAX PERFORMANCE";
                plan.RiskLevel = "SAFE";
                return;
            }

            if (plan.IsErrorOptimizerOwned)
            {
                plan.PerformanceProfileText = "CUSTOM PERFORMANCE PROFILE";
                plan.AcPerformanceProfile = "CUSTOM AC";
                plan.DcPerformanceProfile = "CUSTOM DC";
                plan.BatteryImpact = "CUSTOM";
                plan.RiskLevel = "SAFE";
                return;
            }

            if (plan.Guid.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase) || plan.Name.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            {
                plan.PerformanceProfileText = "BALANCED ENERGY PROFILE";
                plan.AcPerformanceProfile = "BALANCED";
                plan.DcPerformanceProfile = "BALANCED EFFICIENCY";
                plan.BatteryImpact = "BALANCED";
                plan.RiskLevel = "SAFE";
            }
            else if (plan.Guid.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ||
                     plan.Guid.Equals(HighPerformanceAltGuid, StringComparison.OrdinalIgnoreCase) ||
                     plan.Name.Equals("High performance", StringComparison.OrdinalIgnoreCase))
            {
                plan.PerformanceProfileText = "HIGH PERFORMANCE PROFILE";
                plan.AcPerformanceProfile = "HIGH PERFORMANCE";
                plan.DcPerformanceProfile = "HIGH PERFORMANCE";
                plan.BatteryImpact = "HIGH PERFORMANCE";
                plan.RiskLevel = "LOW RISK";
            }
            else if (plan.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase) ||
                     plan.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase))
            {
                plan.PerformanceProfileText = "MAXIMUM PERFORMANCE PROFILE";
                plan.AcPerformanceProfile = "MAX PERFORMANCE";
                plan.DcPerformanceProfile = "MAX PERFORMANCE";
                plan.BatteryImpact = "MAX PERFORMANCE";
                plan.RiskLevel = "CAUTION";
            }
            else if (plan.Guid.Equals(PowerSaverGuid, StringComparison.OrdinalIgnoreCase) || plan.Name.Equals("Power Saver", StringComparison.OrdinalIgnoreCase))
            {
                plan.PerformanceProfileText = "POWER SAVER PROFILE";
                plan.AcPerformanceProfile = "ENERGY SAVER";
                plan.DcPerformanceProfile = "MAX BATTERY SAVINGS";
                plan.BatteryImpact = "POWER SAVER";
                plan.RiskLevel = "SAFE";
            }
            else
            {
                plan.PerformanceProfileText = "CUSTOM PERFORMANCE PROFILE";
                plan.AcPerformanceProfile = "CUSTOM AC";
                plan.DcPerformanceProfile = "CUSTOM DC";
                plan.BatteryImpact = "CUSTOM";
                plan.RiskLevel = "LOW RISK";
            }
        }

        #region Ownership Registry Management

        private static Dictionary<string, PowerPlanOwnershipRecord> GetOwnershipRecords()
        {
            var map = new Dictionary<string, PowerPlanOwnershipRecord>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REG_AI_PLANS_KEY);
                if (key != null)
                {
                    foreach (var val in key.GetValueNames())
                    {
                        string norm = NormalizeGuid(val);
                        string? data = key.GetValue(val)?.ToString();

                        if (!string.IsNullOrWhiteSpace(data) && data.TrimStart().StartsWith("{"))
                        {
                            try
                            {
                                var rec = System.Text.Json.JsonSerializer.Deserialize<PowerPlanOwnershipRecord>(data, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (rec != null)
                                {
                                    rec.Guid = norm;
                                    map[norm] = rec;
                                    continue;
                                }
                            }
                            catch { }
                        }

                        map[norm] = new PowerPlanOwnershipRecord
                        {
                            Guid = norm,
                            Name = "Error Optimizer AI — Max Performance",
                            PlanType = "AI_MAX_PERFORMANCE",
                            Version = "3.0"
                        };
                    }
                }
            }
            catch { }
            return map;
        }

        private static void RecordOwnership(PowerPlanOwnershipRecord record)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(REG_AI_PLANS_KEY);
                string json = System.Text.Json.JsonSerializer.Serialize(record);
                key?.SetValue(NormalizeGuid(record.Guid), json);
            }
            catch { }
        }

        private static void RemoveOwnership(string guid)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REG_AI_PLANS_KEY, writable: true);
                key?.DeleteValue(NormalizeGuid(guid), false);
            }
            catch { }
        }

        #endregion

        private static (string guid, string name) ParseSchemeLine(string line)
        {
            int guidIdx = line.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);
            if (guidIdx < 0) return (string.Empty, string.Empty);

            string after = line.Substring(guidIdx + 5).Trim();
            int openParen = after.IndexOf('(');
            int closeParen = after.LastIndexOf(')');

            string guid = openParen > 0 ? after.Substring(0, openParen).Trim() : after.Trim();
            string name = "Windows Scheme";
            if (openParen >= 0 && closeParen > openParen)
            {
                name = after.Substring(openParen + 1, closeParen - openParen - 1).Trim();
            }

            return (NormalizeGuid(guid), name);
        }

        private static string GetPowercfgPath()
        {
            string systemPath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
            return File.Exists(systemPath) ? systemPath : "powercfg.exe";
        }

        #endregion
    }
}


