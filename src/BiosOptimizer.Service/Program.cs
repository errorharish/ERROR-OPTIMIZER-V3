using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.ActionHandlers;
using BiosOptimizer.Detection;
using BiosOptimizer.Safety;
using BiosOptimizer.Core.Models;

using BiosOptimizer.CLI.Adapters.Workloads;
using BiosOptimizer.Core.Implementations.Cleaners;
using BiosOptimizer.Core.Implementations.Diagnostics;
using BiosOptimizer.Core.Implementations.Storage;

namespace BiosOptimizer.Service
{
    public class Program
    {
        private static System.Threading.Mutex? _mutex;

        public static void Main(string[] args)
        {
            EnforceSingleInstanceAndCleanup();

            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options => options.ServiceName = "BiosOptimizerService")
                .ConfigureServices((ctx, services) =>
                {
                    RegisterBackendServices(services);
                    services.AddHostedService<NamedPipeServer>();
                    services.AddHostedService<BiosOptimizer.Service.Metrics.LiveSystemMetricsService>();
                })
                .Build();

            ValidateDependencyInjection(host);
            host.Run();
        }

        private static void EnforceSingleInstanceAndCleanup()
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                if (p.ProcessName.StartsWith("BiosOptimizer.Service") && p.Id != currentProcess.Id)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                    catch { /* Ignore access denied or already exited */ }
                }
            }

            // Enforce Mutex
            _mutex = new System.Threading.Mutex(true, "Global\\BiosOptimizerServiceMutex", out bool createdNew);
            if (!createdNew)
            {
                System.Console.WriteLine("Fatal: Could not acquire global mutex. Another instance is persistently locking it.");
                System.Environment.Exit(1);
            }
        }

        private static bool IsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private static bool IsRunningAsWindowsService()
        {
            return !System.Environment.UserInteractive;
        }

        private static void ElevatePrivileges(string[] args)
        {
            var exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exeName)) return;

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = System.Environment.CurrentDirectory,
                FileName = exeName,
                Verb = "runas"
            };

            if (args != null && args.Length > 0)
            {
                startInfo.Arguments = string.Join(" ", args);
            }

            try
            {
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                System.Console.WriteLine("This service requires administrative privileges to perform optimizations.");
            }
        }

        private static void ValidateDependencyInjection(IHost host)
        {
            // Fail fast on startup if DI is misconfigured
            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            
            sp.GetRequiredService<IServiceManager>();
            sp.GetRequiredService<IStartupManager>();
            sp.GetRequiredService<ITierEngine>();
            sp.GetRequiredService<IOptimizationScoreEngine>();
            sp.GetRequiredService<IProfileRepository>();
            sp.GetRequiredService<IActionRegistry>();
            
            try
            {
                var pkgMgr = sp.GetRequiredService<IPackageManager>();
                System.Console.WriteLine($"[DI] IPackageManager registration FOUND: {pkgMgr.GetType().FullName}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DI] IPackageManager registration FAILED: {ex.Message}");
                throw;
            }
        }

        private static void RegisterBackendServices(IServiceCollection services)
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            var baseDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : AppContext.BaseDirectory;
            var profilesDir = Path.Combine(baseDir ?? AppContext.BaseDirectory, "profiles");
            
            if (!Directory.Exists(profilesDir))
                profilesDir = Path.Combine(baseDir ?? "", "..", "profiles");
            if (!Directory.Exists(profilesDir))
                profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "profiles");
            if (!Directory.Exists(profilesDir))
                profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            if (!Directory.Exists(profilesDir))
                profilesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer", "profiles");

            var workloadDetectors = new System.Collections.Generic.List<IWorkloadDetector>
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

            IServiceManager serviceManager = new BiosOptimizer.CLI.Adapters.WindowsServiceManager();
            IRegistryManager registryManager = new BiosOptimizer.CLI.Adapters.WindowsRegistryManager();
            IScheduledTaskManager taskManager = new BiosOptimizer.CLI.Adapters.WindowsScheduledTaskManager();
            IStartupManager startupManager = new BiosOptimizer.CLI.Adapters.WindowsStartupManager();
            IPackageManager packageManager = new BiosOptimizer.CLI.Adapters.WindowsPackageManager();
            BiosOptimizer.CLI.Adapters.Bios.IAcpiEvaluator acpiEvaluator = new BiosOptimizer.CLI.Adapters.Bios.NullAcpiEvaluator();
            IProviderManager providerManager = new BiosOptimizer.CLI.Adapters.Bios.ProviderManager(envDetector, acpiEvaluator);

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
            actionRegistry.RegisterHandler(new SvchostSplitThresholdHandler());

            IOptimizationScoreEngine scoreEngine = new OptimizationScoreEngine();
            IBackupManager backupManager = new BackupManager(registryManager, serviceManager);
            ITierEngine tierEngine = new TierEngine(profileRepo, envDetector, actionRegistry, processSnapshot, backupManager);
            var diagEngine = new DiagnosticsEngine();
            var storageEngine = new StorageEngine();
            var ramCleaner = new RamCleanupEngine();
            var tempCleaner = new TempCleanerEngine();

            // Singleton registrations
            services.AddSingleton<IEnvironmentDetector>(envDetector);
            services.AddSingleton<IProfileRepository>(profileRepo);
            services.AddSingleton<IActionRegistry>(actionRegistry);
            services.AddSingleton<IBackupManager>(backupManager);
            services.AddSingleton<IProcessReductionEngine, ProcessReductionEngine>();
            services.AddSingleton<IInputOptimizerEngine, InputOptimizerEngine>();
            services.AddSingleton<IRegistryTweakEngine, RegistryTweakEngine>();
            services.AddSingleton<INetworkOptimizerEngine, NetworkOptimizerEngine>();
            services.AddSingleton<ITierEngine>(tierEngine);
            services.AddSingleton<IProcessSnapshot>(processSnapshot);
            services.AddSingleton<IOptimizationScoreEngine>(scoreEngine);
            services.AddSingleton<IStartupManager>(startupManager);
            services.AddSingleton<IServiceManager>(serviceManager);
            services.AddSingleton<IProviderManager>(providerManager);
            services.AddSingleton(diagEngine);
            services.AddSingleton(storageEngine);
            services.AddSingleton(ramCleaner);
            services.AddSingleton(tempCleaner);
            services.AddSingleton<IPackageManager>(packageManager);
        }
    }
}
