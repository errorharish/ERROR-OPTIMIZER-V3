using System;
using System.Linq;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Models;
using Xunit;

namespace BiosOptimizer.Core.Tests
{
    public class SystemDiagnosticsAndHealthTests
    {
        [Fact]
        public void SystemDiagnosticsReader_ReadsAllSubsystems_WithoutThrowing()
        {
            var reader = new SystemDiagnosticsReader();
            var diag = reader.ReadCompleteDiagnostics();

            Assert.NotNull(diag);
            Assert.NotNull(diag.Cpu);
            Assert.NotNull(diag.Memory);
            Assert.NotNull(diag.Storage);
            Assert.NotNull(diag.Motherboard);
            Assert.NotNull(diag.Windows);
            Assert.NotNull(diag.Security);
            Assert.NotNull(diag.Graphics);
            Assert.NotNull(diag.Power);
            Assert.NotNull(diag.Network);
            Assert.NotNull(diag.Drivers);

            Assert.False(string.IsNullOrWhiteSpace(diag.Cpu.Name));
            Assert.True(diag.Cpu.PhysicalCores > 0);
            Assert.False(string.IsNullOrWhiteSpace(diag.Windows.Edition));
            Assert.False(string.IsNullOrWhiteSpace(diag.Security.SecureBootStatus));
        }

        [Fact]
        public async Task SystemHealthAnalysisEngine_Runs18Checks_GeneratesValidReport()
        {
            var engine = new SystemHealthAnalysisEngine();
            int progressEvents = 0;
            var progress = new Progress<SystemHealthScanProgress>(_ => progressEvents++);

            var report = await engine.RunHealthAnalysisAsync(progress);

            Assert.NotNull(report);
            Assert.True(report.TotalChecks >= 15);
            Assert.NotEmpty(report.Findings);
            Assert.Contains(report.OverallRating, new[] { "EXCELLENT", "GOOD", "FAIR", "WARNING", "CRITICAL" });
            Assert.True(report.OverallScore >= 0 && report.OverallScore <= 100);
            Assert.False(string.IsNullOrWhiteSpace(report.Summary));

            foreach (var finding in report.Findings)
            {
                Assert.False(string.IsNullOrWhiteSpace(finding.Title));
                Assert.False(string.IsNullOrWhiteSpace(finding.Category));
                Assert.Contains(finding.Severity, new[] { "PASS", "WARNING", "CRITICAL", "INFO" });
                Assert.False(string.IsNullOrWhiteSpace(finding.Reason));
            }
        }

        [Fact]
        public void GpuDetection_MaintainsSeparateAdapters()
        {
            var reader = new SystemDiagnosticsReader();
            var diag = reader.ReadCompleteDiagnostics();

            Assert.NotNull(diag.Gpus);
            if (diag.Gpus.Count > 1)
            {
                // Verify no duplicate names
                var distinctNames = diag.Gpus.Select(g => g.Name).Distinct().ToList();
                Assert.Equal(diag.Gpus.Count, distinctNames.Count);
            }
        }
    }
}
