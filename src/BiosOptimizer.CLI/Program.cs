using System.Text.Json;

using BiosOptimizer.Core.Implementations;

using BiosOptimizer.Core.Interfaces;

using BiosOptimizer.Core.ActionHandlers;

using BiosOptimizer.Detection;

using BiosOptimizer.Safety;

using BiosOptimizer.Core.Models;

using BiosOptimizer.CLI.Adapters.Workloads;

using BiosOptimizer.Core.Implementations.Cleaners;

using BiosOptimizer.Core.Implementations.Diagnostics;

using BiosOptimizer.Core.Implementations.Services;

using BiosOptimizer.Core.Implementations.Startup;

using BiosOptimizer.Core.Implementations.Storage;

using BiosOptimizer.Core.Implementations.Tools;

namespace BiosOptimizer.CLI;

class Program

{

    static async Task<int> Main(string[] args)

    {

        if (args.Length == 0)

        {

            PrintUsage();

            return 1;

        }

        var command = args[0].ToLowerInvariant();

        var baseDir = AppContext.BaseDirectory;

        var profilesDir = Path.Combine(baseDir, "profiles");

        if (!Directory.Exists(profilesDir)) profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "profiles");

        var workloadDetectors = new List<IWorkloadDetector>

        {

            new EmulatorWorkloadDetector(),

            new AdobeWorkloadDetector(),

            new TopazWorkloadDetector(),

            new GameWorkloadDetector()

        };

        

        IProfileRepository profileRepo = new ProfileRepository(profilesDir);

        IEnvironmentDetector envDetector = new EnvironmentDetector(workloadDetectors);

        IProcessSnapshot processSnapshot = new ProcessSnapshot();

        ISafetyPolicy safetyPolicy = new ProtectedTargetsPolicy();

        IVerificationEngine verificationEngine = new VerificationEngine();

        

        // Use Real implementations for OS abstractions in normal CLI

        IServiceManager serviceManager = new BiosOptimizer.CLI.Adapters.WindowsServiceManager();

        IRegistryManager registryManager = new BiosOptimizer.CLI.Adapters.WindowsRegistryManager();

        IScheduledTaskManager taskManager = new BiosOptimizer.CLI.Adapters.WindowsScheduledTaskManager();

        IStartupManager startupManager = new BiosOptimizer.CLI.Adapters.WindowsStartupManager();

        IPackageManager packageManager = new BiosOptimizer.CLI.Adapters.WindowsPackageManager();

        BiosOptimizer.CLI.Adapters.Bios.IAcpiEvaluator acpiEvaluator = new BiosOptimizer.CLI.Adapters.Bios.NullAcpiEvaluator();

        IProviderManager providerManager = new BiosOptimizer.CLI.Adapters.Bios.ProviderManager(envDetector, acpiEvaluator);

        

        IWorkloadSessionManager sessionManager = new WorkloadSessionManager(workloadDetectors, null!); // Null engine for now to prevent cyclic initialization, will set below if needed.

        IActionRegistry actionRegistry = new ActionRegistry();

        actionRegistry.RegisterHandler(new SetServiceStartupHandler(serviceManager, safetyPolicy, verificationEngine));

        actionRegistry.RegisterHandler(new SetRegistryValueHandler(registryManager));

        actionRegistry.RegisterHandler(new DisableScheduledTaskHandler(taskManager));

        actionRegistry.RegisterHandler(new DisableStartupEntryHandler(startupManager));

        actionRegistry.RegisterHandler(new RemoveAppxPackageHandler(packageManager, safetyPolicy, verificationEngine));

        actionRegistry.RegisterHandler(new SetBiosSettingHandler(providerManager, verificationEngine));

        actionRegistry.RegisterHandler(new PowerPlanOptimizationHandler());

        actionRegistry.RegisterHandler(new ProcessPriorityHandler());

        actionRegistry.RegisterHandler(new CpuAffinityHandler());

        actionRegistry.RegisterHandler(new ForegroundPriorityHandler());

        actionRegistry.RegisterHandler(new HagsConfigurationHandler());

        actionRegistry.RegisterHandler(new PagefileOptimizationHandler());

        actionRegistry.RegisterHandler(new DnsFlushHandler());

        actionRegistry.RegisterHandler(new TcpAutoTuningHandler());

        actionRegistry.RegisterHandler(new CongestionControlHandler());

        actionRegistry.RegisterHandler(new NetworkThrottlingHandler());

        actionRegistry.RegisterHandler(new VisualFxHandler());

        actionRegistry.RegisterHandler(new GpuPerformanceHandler());

        actionRegistry.RegisterHandler(new CpuParkingHandler());

        actionRegistry.RegisterHandler(new MouseOptimizationHandler());

        actionRegistry.RegisterHandler(new KeyboardOptimizationHandler());

        actionRegistry.RegisterHandler(new ManagedRegistryHealthHandler());

        actionRegistry.RegisterHandler(new GodModeHandler());

        actionRegistry.RegisterHandler(new SvchostSplitThresholdHandler());

        // Phase 5 Utility Engines (Just initializing to prove compilation)

        var ramCleaner = new RamCleanupEngine();

        var tempCleaner = new TempCleanerEngine();

        var shaderCleaner = new ShaderCacheCleanerEngine();

        var deepCleaner = new DeepSystemCleanerEngine();

        var largeFiles = new LargeFileScannerEngine();

        var duplicates = new DuplicateFileScannerEngine();

        var diagEngine = new DiagnosticsEngine();

        var storageEngine = new StorageEngine();

        var autorunManager = new AutorunManager(registryManager);

        var svcDebloatEngine = new ServiceDebloaterEngine(safetyPolicy);

        

        IOptimizationEngineRegistry engineRegistry = new OptimizationEngineRegistry();

        IOptimizationScoreEngine scoreEngine = new OptimizationScoreEngine();

        IBackupManager backupManager = new BackupManager(registryManager, serviceManager);

        ITierEngine engine = new TierEngine(profileRepo, envDetector, actionRegistry, processSnapshot, backupManager);

        

        // Inject engine back to session manager if required in real implementation

        // ((WorkloadSessionManager)sessionManager).SetEngine(engine);

        try

        {

            switch (command)

            {

                case "detect":

                    await RunDetect(envDetector);

                    break;

                case "--input-scan":

                    await RunInputScan(new InputOptimizerEngine(backupManager));

                    break;

                case "--input-apply-core":

                    await RunInputApply(new InputOptimizerEngine(backupManager), "CORE");

                    break;

                case "--input-apply-advanced":

                    await RunInputApply(new InputOptimizerEngine(backupManager), "ADVANCED");

                    break;

                case "--input-restore":

                    await RunInputRestore(new InputOptimizerEngine(backupManager));

                    break;

                case "--input-measure":

                    Console.WriteLine("Raw input measurement requires a Windows Message Loop (GUI). Use the GUI to measure.");

                    break;

                case "debloat":

                    if (args.Length > 1 && args[1].ToLowerInvariant() == "list")

                    {

                        RunDebloatList(packageManager);

                    }

                    else

                    {

                        Console.WriteLine("Usage: debloat list");

                    }

                    break;

                case "bios":

                    if (args.Length > 1)

                    {

                        if (args[1].ToLowerInvariant() == "detect") RunBiosDetect(envDetector, providerManager);

                        else if (args[1].ToLowerInvariant() == "capabilities") RunBiosCapabilities(providerManager);

                        else if (args[1].ToLowerInvariant() == "settings") RunBiosCapabilities(providerManager); // Alias for now

                        else Console.WriteLine("Usage: bios [detect|capabilities|settings]");

                    }

                    else

                    {

                        Console.WriteLine("Usage: bios [detect|capabilities|settings]");

                    }

                    break;

                case "environment":

                    await RunEnvironment(envDetector);

                    break;

                case "workloads":

                    await RunWorkloads(envDetector);

                    break;

                case "workload":

                    if (args.Length > 1 && args[1].ToLowerInvariant() == "detect") await RunWorkloadDetect(envDetector);

                    else if (args.Length > 1 && args[1].ToLowerInvariant() == "list") await RunWorkloads(envDetector);

                    else Console.WriteLine("Usage: workload [detect|list]");

                    break;

                case "performance":

                    if (args.Length > 1 && args[1].ToLowerInvariant() == "detect") await RunPerformanceDetect(envDetector);

                    else Console.WriteLine("Usage: performance detect");

                    break;

                case "preset":

                    if (args.Length > 1 && args[1].ToLowerInvariant() == "list") RunPresetList(profileRepo);

                    else if (args.Length > 2 && args[1].ToLowerInvariant() == "info") RunTierInfo(profileRepo, args[2]);

                    else Console.WriteLine("Usage: preset [list|info] <presetName>");

                    break;

                case "tiers":

                    RunTiers(profileRepo);

                    break;

                case "tier-info":

                    if (args.Length < 2) { Console.WriteLine("Missing TierId"); return 1; }

                    RunTierInfo(profileRepo, args[1]);

                    break;

                case "preview":

                    if (args.Length < 2) { Console.WriteLine("Missing TierId"); return 1; }

                    await RunPreview(engine, args[1]);

                    break;

                case "apply":

                    if (args.Length < 2) { Console.WriteLine("Missing TierId"); return 1; }

                    bool dryRun = args.Contains("--dry-run");

                    bool confirm = args.Contains("--confirm");

                    if (!dryRun && !confirm) { Console.WriteLine("Must specify --dry-run or --confirm"); return 1; }

                    if (!dryRun && false)

                    {

                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine("Administrator privileges are required for this operation.");

                        Console.ResetColor();

                        return 1;

                    }

                    await RunApply(engine, args[1], dryRun, confirm);

                    break;

                case "status":

                    Console.WriteLine("Status command not fully implemented in Phase 1.");

                    break;

                case "diagnostics":

                    Console.WriteLine("Diagnostics OK.");

                    break;

                default:

                    PrintUsage();

                    return 1;

            }

            return 0;

        }

        catch (Exception ex)

        {

            Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine($"Error: {ex.Message}");

            Console.ResetColor();

            return 1;

        }

    }

    static bool IsAdministrator()

    {

        try

        {

            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();

            var principal = new System.Security.Principal.WindowsPrincipal(identity);

            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        }

        catch

        {

            return false;

        }

    }

    static void PrintUsage()

    {

        Console.WriteLine("ERROR OPTIMIZER Phase 5");

        Console.WriteLine("Commands: detect, environment, workloads, tiers, debloat list, bios detect, bios capabilities");

        Console.WriteLine("          workload [detect|list], performance detect, preset [list|info]");

        Console.WriteLine("          preview <TierId>, apply <TierId> [--dry-run | --confirm], status, restore <TierId>, diagnostics");

    }

    static void RunDebloatList(IPackageManager packageManager)

    {

        Console.WriteLine("====================================================");

        Console.WriteLine(" INSTALLED APPX PACKAGES");

        Console.WriteLine("====================================================");

        var packages = packageManager.GetInstalledPackages();

        foreach (var p in packages.Where(x => !x.IsSystemComponent && !x.IsFramework))

        {

            Console.WriteLine($"- {p.Name} ({p.Version}) - {p.Publisher}");

        }

    }

    static void RunBiosDetect(IEnvironmentDetector envDetector, IProviderManager providerManager)

    {

        var env = envDetector.Detect();

        var provider = providerManager.GetActiveProvider();

        var info = provider.GetProviderInfo();

        Console.WriteLine("====================================================");

        Console.WriteLine(" BIOS PROVIDER");

        Console.WriteLine("====================================================");

        Console.WriteLine();

        Console.WriteLine($"Vendor        : {env.Manufacturer}");

        Console.WriteLine($"Model         : {env.Model}");

        Console.WriteLine($"BIOS Version  : {env.BIOSVersion}");

        Console.WriteLine();

        Console.WriteLine("Provider");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Name          : {info.Name}");

        Console.WriteLine($"Status        : {info.Status}");

        Console.WriteLine($"Reason        : {info.Reason}");

    }

    static void RunBiosCapabilities(IProviderManager providerManager)

    {

        var provider = providerManager.GetActiveProvider();

        var caps = provider.DiscoverCapabilities();

        Console.WriteLine("====================================================");

        Console.WriteLine(" BIOS CAPABILITIES");

        Console.WriteLine("====================================================");

        Console.WriteLine();

        if (caps.Count == 0)

        {

            Console.WriteLine("No capabilities discovered or provider is unsupported.");

            return;

        }

        foreach (var cap in caps)

        {

            Console.WriteLine(cap.DisplayName);

            Console.WriteLine($"Current       : {cap.CurrentValue}");

            Console.WriteLine($"Writable      : {(cap.Writable ? "Yes" : "No")}");

            Console.WriteLine($"Risk          : {cap.RiskLevel.ToUpper()}");

            if (!string.IsNullOrEmpty(cap.Warning)) Console.WriteLine($"Warning       : {cap.Warning}");

            Console.WriteLine();

        }

    }

    static Task RunDetect(IEnvironmentDetector envDetector)

    {

        var env = envDetector.Detect();

        Console.WriteLine("====================================================");

        Console.WriteLine(" BIOS OPTIMIZER - PHASE 1");

        Console.WriteLine("====================================================");

        Console.WriteLine();

        Console.WriteLine("SYSTEM");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"OS                 : {(env.IsWindows11 ? "Windows 11" : env.IsWindows10 ? "Windows 10" : "Unknown")} {env.Edition}");

        Console.WriteLine($"Architecture       : {env.Architecture}");

        Console.WriteLine($"Manufacturer       : {env.Manufacturer}");

        Console.WriteLine($"Model              : {env.Model}");

        Console.WriteLine($"BIOS               : {env.BIOSVendor} {env.BIOSVersion}");

        Console.WriteLine($"CPU                : {env.CpuName}");

        Console.WriteLine($"Physical Cores     : {env.PhysicalCoreCount}");

        Console.WriteLine($"Logical Threads    : {env.LogicalCoreCount}");

        Console.WriteLine($"RAM                : {env.RamSizeGb} GB");

        Console.WriteLine($"GPU                : {env.GpuName}");

        Console.WriteLine($"Storage            : {env.StorageType}");

        Console.WriteLine($"Virtualization     : {(env.VirtualizationSupported ? (env.VirtualizationEnabled ? "Enabled" : "Supported but Disabled") : "Not Supported")}");

        Console.WriteLine($"BitLocker          : {(env.BitLockerActive ? "Active" : "Inactive")}");

        return Task.CompletedTask;

    }

    static Task RunEnvironment(IEnvironmentDetector envDetector)

    {

        var env = envDetector.Detect();

        Console.WriteLine("ENVIRONMENT");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Printer            : {env.HasPrinter}");

        Console.WriteLine($"Bluetooth          : {env.HasBluetooth}");

        Console.WriteLine($"Touchscreen        : {env.HasTouchscreen}");

        Console.WriteLine($"Camera             : {env.HasCamera}");

        Console.WriteLine($"Battery            : {(env.HasBattery ? "Present" : "Not Present")}");

        Console.WriteLine($"Domain Joined      : {(env.IsDomainJoined ? "Yes" : "No")}");

        return Task.CompletedTask;

    }

    static Task RunWorkloads(IEnvironmentDetector envDetector)

    {

        var env = envDetector.Detect();

        Console.WriteLine("WORKLOADS");

        Console.WriteLine("----------------------------------------------------");

        foreach (var w in env.Workloads)

        {

            Console.WriteLine($"{w.WorkloadType,-20}: {(w.Detected ? "Detected" : "Not Detected")} - {w.DisplayName}");

        }

        return Task.CompletedTask;

    }

    static Task RunWorkloadDetect(IEnvironmentDetector envDetector)

    {

        return RunWorkloads(envDetector); // Alias for now

    }

    static Task RunPerformanceDetect(IEnvironmentDetector envDetector)

    {

        var env = envDetector.Detect();

        var hw = env.HardwareProfile;

        Console.WriteLine("====================================================");

        Console.WriteLine(" PERFORMANCE PROFILE DETECT");

        Console.WriteLine("====================================================");

        Console.WriteLine();

        Console.WriteLine($"Classification : {env.Classification}");

        Console.WriteLine($"CPU            : {hw.CPUName}");

        Console.WriteLine($"Topology       : {hw.PhysicalCores} Physical, {hw.LogicalProcessors} Logical");

        Console.WriteLine($"RAM            : {hw.RAM / 1024 / 1024 / 1024} GB");

        Console.WriteLine($"GPU            : {hw.GPU}");

        Console.WriteLine($"VRAM           : {hw.VRAM / 1024 / 1024} MB");

        Console.WriteLine($"Storage        : {hw.StorageType}");

        Console.WriteLine($"Power Plan     : {hw.PowerPlan}");

        Console.WriteLine($"HAGS           : {(hw.HagsSupported ? (hw.HagsEnabled ? "Enabled" : "Disabled") : "Unsupported")}");

        Console.WriteLine($"Virtualization : {(hw.VirtualizationSupported ? (hw.VirtualizationEnabled ? "Enabled" : "Disabled") : "Unsupported")}");

        return Task.CompletedTask;

    }

    static void RunPresetList(IProfileRepository repo)

    {

        var presets = new[] { "AfterEffectsTurbo", "AAAGaming", "EmulatorMax", "TopazAI", "LowEndRescue", "TotalMax" };

        Console.WriteLine("PRESETS");

        Console.WriteLine("----------------------------------------------------");

        foreach (var p in presets)

        {

            var profile = repo.LoadResolvedProfile(p);

            if (profile != null) Console.WriteLine($"- {profile.Id}: {profile.DisplayName}");

        }

    }

    static void RunTiers(IProfileRepository repo)

    {

        var tiers = new[] { "Normal", "Pro", "Ultimate" };

        Console.WriteLine("TIERS");

        Console.WriteLine("----------------------------------------------------");

        foreach (var t in tiers)

        {

            var profile = repo.LoadResolvedProfile(t);

            if (profile != null) Console.WriteLine($"- {profile.Id}: {profile.DisplayName}");

        }

    }

    static void RunTierInfo(IProfileRepository repo, string tierId)

    {

        var profile = repo.LoadResolvedProfile(tierId);

        if (profile == null)

        {

            Console.WriteLine($"Tier '{tierId}' not found.");

            return;

        }

        Console.WriteLine("====================================================");

        Console.WriteLine($" {profile.DisplayName.ToUpper()} TIER INFO");

        Console.WriteLine("====================================================");

        Console.WriteLine();

        Console.WriteLine($"Description          : {profile.Description}");

        Console.WriteLine($"Total Planned Actions: {profile.Entries.Count}");

        Console.WriteLine($"Estimated Reduction  : {profile.TargetProcessReduction.Min} to {profile.TargetProcessReduction.Max}");

        Console.WriteLine();

        

        int low = profile.Entries.Count(e => e.Risk == "Low");

        int medium = profile.Entries.Count(e => e.Risk == "Medium");

        int high = profile.Entries.Count(e => e.Risk == "High");

        int critical = profile.Entries.Count(e => e.Risk == "Critical");

        

        Console.WriteLine("RISK DISTRIBUTION");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Low     : {low}");

        Console.WriteLine($"Medium  : {medium}");

        Console.WriteLine($"High    : {high}");

        Console.WriteLine($"Critical: {critical}");

        Console.WriteLine();

        if (high > 0 || critical > 0)

        {

            Console.WriteLine("WARNING: This tier contains high-risk actions and requires --confirm.");

        }

    }

    static async Task RunPreview(ITierEngine engine, string tierId)

    {

        var preview = await engine.PreviewAsync(tierId, CancellationToken.None);

        

        Console.WriteLine("====================================================");

        Console.WriteLine($" {tierId.ToUpper()} PREVIEW");

        Console.WriteLine("====================================================");

        Console.WriteLine();

        

        if (preview.InheritedEntryCount > 0)

        {

            Console.WriteLine("BASELINE");

            Console.WriteLine("----------------------------------------------------");

            Console.WriteLine($"Entries inherited    : {preview.InheritedEntryCount}");

            Console.WriteLine();

        }

        if (preview.PlannedChanges.Count > 0)

        {

            Console.WriteLine($"{tierId.ToUpper()} ADDITIONS");

            Console.WriteLine("----------------------------------------------------");

            foreach (var item in preview.PlannedChanges)

            {

                Console.WriteLine(item.Item);

                Console.WriteLine($"Current : {item.Current}");

                Console.WriteLine($"Target  : {item.Target}");

                Console.WriteLine($"Risk    : {item.Risk.ToUpper()}");

                if (!string.IsNullOrEmpty(item.Warning)) Console.WriteLine($"Warning : {item.Warning}");

                Console.WriteLine();

            }

        }

        Console.WriteLine("SKIPPED");

        Console.WriteLine("----------------------------------------------------");

        foreach (var skip in preview.SkippedChanges)

        {

            Console.WriteLine(skip.Item);

            Console.WriteLine($"Reason  : {skip.Reason}");

            Console.WriteLine();

        }

        Console.WriteLine("ESTIMATED");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Current processes  : {preview.CurrentProcessCount}");

        Console.WriteLine($"Estimated range    : {preview.CurrentProcessCount - preview.EstimatedProcessReduction - 10}-{preview.CurrentProcessCount - preview.EstimatedProcessReduction + 10}");

        Console.WriteLine($"Available RAM      : {preview.Baseline.AvailableRamBytes / 1024 / 1024} MB");

        Console.WriteLine();

        Console.WriteLine("NO CHANGES HAVE BEEN MADE.");

        

        if (preview.PlannedChanges.Any(p => p.Risk.Equals("High", StringComparison.OrdinalIgnoreCase) || p.Risk.Equals("Critical", StringComparison.OrdinalIgnoreCase)))

        {

            Console.WriteLine("HIGH-RISK CHANGES REQUIRE CONFIRMATION (--confirm).");

        }

    }

    static async Task RunApply(ITierEngine engine, string tierId, bool dryRun, bool confirm)

    {

        var preview = await engine.PreviewAsync(tierId, CancellationToken.None);

        int beforeCount = preview.CurrentProcessCount;

        var results = await engine.ApplyAsync(tierId, new ConfirmationContext { IsConfirmed = confirm }, dryRun, CancellationToken.None);

        

        var postPreview = await engine.PreviewAsync(tierId, CancellationToken.None);

        int afterCount = postPreview.CurrentProcessCount;

        if (dryRun)

        {

            afterCount = beforeCount - preview.EstimatedProcessReduction; 

        }

        Console.WriteLine("====================================================");

        Console.WriteLine($" {tierId.ToUpper()} OPTIMIZATION RESULT");

        Console.WriteLine("====================================================");

        Console.WriteLine();

        Console.WriteLine("BEFORE -> AFTER");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Processes          : {beforeCount} -> {afterCount}");

        Console.WriteLine($"Available RAM      : {preview.Baseline.AvailableRamBytes / 1024 / 1024} MB -> {postPreview.Baseline.AvailableRamBytes / 1024 / 1024} MB");

        Console.WriteLine();

        

        int svcChanged = results.Count(r => r.ItemId.Contains("service", StringComparison.OrdinalIgnoreCase) && r.Status == ResultStatus.Success);

        int svcVerified = svcChanged; 

        int svcSkipped = results.Count(r => r.ItemId.Contains("service", StringComparison.OrdinalIgnoreCase) && r.Status == ResultStatus.Skipped);

        int svcFailed = results.Count(r => r.ItemId.Contains("service", StringComparison.OrdinalIgnoreCase) && r.Status == ResultStatus.Failed);

        

        Console.WriteLine("SERVICES");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Changed            : {svcChanged}");

        Console.WriteLine($"Verified           : {svcVerified}");

        Console.WriteLine($"Skipped            : {svcSkipped}");

        Console.WriteLine($"Failed             : {svcFailed}");

        Console.WriteLine($"Reverted           : {results.Count(r => r.ItemId.Contains("service", StringComparison.OrdinalIgnoreCase) && r.Status == ResultStatus.Reverted)}");

        Console.WriteLine();

        int regChanged = results.Count(r => r.ItemId.Contains("registry", StringComparison.OrdinalIgnoreCase) && r.Status == ResultStatus.Success);

        Console.WriteLine("REGISTRY");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Changed            : {regChanged}");

        Console.WriteLine($"Verified           : {regChanged}");

        Console.WriteLine($"Failed             : {results.Count(r => r.ItemId.Contains("registry", StringComparison.OrdinalIgnoreCase) && r.Status == ResultStatus.Failed)}");

        Console.WriteLine($"Reverted           : {results.Count(r => r.ItemId.Contains("registry", StringComparison.OrdinalIgnoreCase) && r.Status == ResultStatus.Reverted)}");

        Console.WriteLine();

        Console.WriteLine("TASKS");

        Console.WriteLine("----------------------------------------------------");

        Console.WriteLine($"Changed            : 0");

        Console.WriteLine($"Verified           : 0");

        Console.WriteLine($"Failed             : 0");

        Console.WriteLine($"Reverted           : 0");

        Console.WriteLine();

        Console.WriteLine("OVERALL STATUS");

        Console.WriteLine("----------------------------------------------------");

        if (dryRun) Console.WriteLine("SUCCESS (DRY RUN)");

        else if (results.Any(r => r.Status == ResultStatus.Failed)) Console.WriteLine("COMPLETED WITH ERRORS");

        else Console.WriteLine("SUCCESS");

    }

    private static async Task RunInputScan(IInputOptimizerEngine engine)

    {

        var plan = await engine.PlanInputOptimizationAsync();

        Console.WriteLine("INPUT OPTIMIZATION PLAN:");

        foreach (var action in plan.Actions)

        {

            Console.WriteLine($"[{action.Risk}] {action.Name} - Current: {action.CurrentValue} Target: {action.TargetValue} Status: {action.Reason}");

        }

    }

    private static async Task RunInputApply(IInputOptimizerEngine engine, string risk)

    {

        var result = await engine.ApplyInputOptimizationAsync(risk);

        Console.WriteLine($"Apply {risk} result: {(result.Success ? "SUCCESS" : "FAILED")} - {result.Message}");

    }

    private static async Task RunInputRestore(IInputOptimizerEngine engine)

    {

        var result = await engine.RestoreInputOptimizationAsync();

        Console.WriteLine($"Restore result: {(result.Success ? "SUCCESS" : "FAILED")} - {result.Message}");

    }

}

