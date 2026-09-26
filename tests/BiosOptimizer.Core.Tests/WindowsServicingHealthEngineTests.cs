#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations;

namespace BiosOptimizer.Core.Tests
{
    public class WindowsServicingHealthEngineTests
    {
        // ────────────────────────────────────────────────────────────────────────
        // 1. Environment Detection
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void DetectServicingEnvironment_PopulatesAllFields()
        {
            var env = WindowsServicingHealthEngine.Instance.DetectServicingEnvironment();
            Assert.False(string.IsNullOrWhiteSpace(env.OsEdition), "OsEdition must be populated");
            Assert.False(string.IsNullOrWhiteSpace(env.OsBuild), "OsBuild must be populated");
            Assert.False(string.IsNullOrWhiteSpace(env.Architecture), "Architecture must be populated");
            Assert.False(string.IsNullOrWhiteSpace(env.SystemDrive), "SystemDrive must be populated");
            Assert.True(env.FreeDiskSpaceGb >= 0, "FreeDiskSpaceGb must be non-negative");
        }

        [Fact]
        public void DetectServicingEnvironment_ArchitectureIsXxxBit()
        {
            var env = WindowsServicingHealthEngine.Instance.DetectServicingEnvironment();
            Assert.True(
                env.Architecture == "x64" || env.Architecture == "x86",
                $"Architecture must be x64 or x86, got: {env.Architecture}");
        }

        [Fact]
        public void DetectServicingEnvironment_RequiredServicesPresent()
        {
            var env = WindowsServicingHealthEngine.Instance.DetectServicingEnvironment();
            Assert.True(env.RequiredServices.ContainsKey("TrustedInstaller"), "Must contain TrustedInstaller");
            Assert.True(env.RequiredServices.ContainsKey("wuauserv"), "Must contain wuauserv");
            Assert.True(env.RequiredServices.ContainsKey("CryptSvc"), "Must contain CryptSvc");
        }

        [Fact]
        public void DetectServicingEnvironment_RebootPendingFieldsConsistent()
        {
            var env = WindowsServicingHealthEngine.Instance.DetectServicingEnvironment();
            if (env.IsRebootPending)
            {
                Assert.False(string.IsNullOrWhiteSpace(env.RebootPendingReason),
                    "RebootPendingReason must be non-empty when IsRebootPending is true");
            }
        }

        [Fact]
        public void DetectServicingEnvironment_SfcPathInSystem32()
        {
            var env = WindowsServicingHealthEngine.Instance.DetectServicingEnvironment();
            Assert.True(
                env.SfcPath.Contains("System32", StringComparison.OrdinalIgnoreCase) ||
                env.SfcPath.Contains("Sysnative", StringComparison.OrdinalIgnoreCase),
                $"SfcPath must reside in System32/Sysnative, got: {env.SfcPath}");
        }

        // ────────────────────────────────────────────────────────────────────────
        // 2. Prerequisite Auto-Remediation
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void EnsureServicingPrerequisites_Sfc_DoesNotThrow()
        {
            var (_, msg) = WindowsServicingHealthEngine.Instance.EnsureServicingPrerequisites("tool.repair.sfc");
            Assert.NotNull(msg);
        }

        [Fact]
        public void EnsureServicingPrerequisites_Dism_DoesNotThrow()
        {
            var (_, msg) = WindowsServicingHealthEngine.Instance.EnsureServicingPrerequisites("tool.repair.dism_restore");
            Assert.NotNull(msg);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 3. Error-Code Diagnosis
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void DiagnoseExitCode_ExitCode87_Sfc_IsNotApplicable()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(87, "", "", "sfc");
            Assert.True(d.IsNotApplicable, "Exit code 87 for SFC must map to IsNotApplicable");
        }

        [Fact]
        public void DiagnoseExitCode_ExitCode87_Dism_IsNotApplicable()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(87, "", "", "dism");
            Assert.True(d.IsNotApplicable, "Exit code 87 for DISM must map to IsNotApplicable");
        }

        [Fact]
        public void DiagnoseExitCode_ExitCode5_AccessDenied_Sfc()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(5, "", "", "sfc");
            Assert.False(d.IsNotApplicable, "Exit code 5 must NOT be IsNotApplicable");
            Assert.True(
                d.RootCause.Contains("Administrator", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("privilege", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("access", StringComparison.OrdinalIgnoreCase),
                $"Exit 5 should mention elevation, got: {d.RootCause}");
        }

        [Fact]
        public void DiagnoseExitCode_ExitCode5_AccessDenied_Dism()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(5, "", "", "dism");
            Assert.False(d.IsNotApplicable);
            Assert.True(
                d.RootCause.Contains("Access Denied", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("privilege", StringComparison.OrdinalIgnoreCase),
                $"Exit 5 DISM should mention access denied: {d.RootCause}");
        }

        [Fact]
        public void DiagnoseExitCode_ExitCode112_DiskFull_Dism()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(112, "", "", "dism");
            Assert.False(d.IsNotApplicable);
            Assert.True(
                d.RootCause.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("space", StringComparison.OrdinalIgnoreCase),
                $"Exit 112 should mention disk space: {d.RootCause}");
        }

        [Fact]
        public void DiagnoseExitCode_0x80070422_ServiceDisabled()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(unchecked((int)0x80070422), "", "", "dism");
            Assert.True(
                d.RootCause.Contains("service", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("TrustedInstaller", StringComparison.OrdinalIgnoreCase),
                $"0x80070422 should mention disabled service: {d.RootCause}");
        }

        [Fact]
        public void DiagnoseExitCode_0x800F081F_SourceNotFound()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(unchecked((int)0x800F081F), "", "", "dism");
            Assert.True(
                d.RootCause.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("Windows Update", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("CBS", StringComparison.OrdinalIgnoreCase),
                $"0x800F081F should mention source: {d.RootCause}");
        }

        [Fact]
        public void DiagnoseExitCode_0x80070020_SharingViolation()
        {
            var d = WindowsServicingHealthEngine.Instance.DiagnoseExitCode(unchecked((int)0x80070020), "", "", "dism");
            Assert.True(
                d.RootCause.Contains("lock", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("sharing", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("TiWorker", StringComparison.OrdinalIgnoreCase) ||
                d.RootCause.Contains("reboot", StringComparison.OrdinalIgnoreCase),
                $"0x80070020 should mention lock/TiWorker: {d.RootCause}");
        }

        // ────────────────────────────────────────────────────────────────────────
        // 4. Process State Rules
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteServicingTool_PreCancelledToken_DoesNotThrow()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = await WindowsServicingHealthEngine.Instance.ExecuteServicingToolAsync(
                "tool.repair.sfc", null, TimeSpan.FromSeconds(5), cts.Token);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ExecuteServicingTool_UnknownToolId_ReturnsNotApplicableOrFailed()
        {
            var result = await WindowsServicingHealthEngine.Instance
                .ExecuteServicingToolAsync("tool.repair.__fake_xyz__", null, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(result);
            if (!result.Success)
            {
                Assert.False(string.IsNullOrWhiteSpace(result.DiagnosedRootCause),
                    "Failed exec must set DiagnosedRootCause");
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 5. Singleton contract
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Instance_SameSingleton()
        {
            Assert.Same(WindowsServicingHealthEngine.Instance, WindowsServicingHealthEngine.Instance);
        }
    }
}
