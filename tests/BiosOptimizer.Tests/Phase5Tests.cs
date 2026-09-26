using BiosOptimizer.Core.Implementations.Cleaners;
using BiosOptimizer.Core.ActionHandlers;
using BiosOptimizer.Core.Implementations.Services;
using BiosOptimizer.Core.Models;
using Moq;

namespace BiosOptimizer.Tests;

public class Phase5Tests
{
    [Fact]
    public void CleanerEngine_RamCleanup_ReturnsScanDto()
    {
        var engine = new RamCleanupEngine();
        var scan = engine.Scan();

        Assert.Equal("RAM", scan.Category);
        Assert.True(scan.IsSafeToClean);
    }

    [Fact]
    public void NetworkHandler_NetworkThrottling_AppliesCorrectly()
    {
        var handler = new NetworkThrottlingHandler();
        var entry = new OptimizationEntry
        {
            Action = "SetNetworkThrottling",
            Target = "NetworkThrottlingIndex",
            Value = "ffffffff"
        };
        var context = new EnvironmentContext();

        var canApply = handler.CanApply(entry, context);
        Assert.True(canApply);
    }

    [Fact]
    public void ServiceDebloater_ClassifiesProtectedService()
    {
        var mockSafety = new Mock<BiosOptimizer.Core.Interfaces.ISafetyPolicy>();
        mockSafety.Setup(s => s.IsProtectedTarget("WinDefend")).Returns(true);

        var engine = new ServiceDebloaterEngine(mockSafety.Object);
        var classification = engine.ClassifyService("WinDefend", "Windows Defender Antivirus Service");

        Assert.Equal("Protected", classification.Category);
        Assert.Equal("Keep", classification.TargetState);
    }
}
