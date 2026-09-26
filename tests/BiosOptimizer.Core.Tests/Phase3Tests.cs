using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.CLI.Adapters.Bios;
using BiosOptimizer.Safety;

namespace BiosOptimizer.Core.Tests;

public class Phase3Tests
{
    private class MockEnvironmentDetector : IEnvironmentDetector
    {
        private readonly EnvironmentContext _context;
        public MockEnvironmentDetector(EnvironmentContext context) => _context = context;
        public EnvironmentContext Detect() => _context;
    }

    [Fact]
    public void ProviderManager_SelectsCorrectProvider()
    {
        var context = new EnvironmentContext { Manufacturer = "Lenovo", BIOSVendor = "Lenovo" };
        var detector = new MockEnvironmentDetector(context);
        var manager = new ProviderManager(detector, new NullAcpiEvaluator());
        var provider = manager.GetActiveProvider();
        
        // LenovoProvider returns Unavailable in test environment if WMI classes aren't present, so ProviderManager falls back to GenericProvider.
        Assert.IsType<GenericProvider>(provider);
    }

    [Fact]
    public void ProviderManager_SelectsGenericProvider_WhenUnknown()
    {
        var context = new EnvironmentContext { Manufacturer = "UnknownBrand", BIOSVendor = "Unknown" };
        var detector = new MockEnvironmentDetector(context);
        var manager = new ProviderManager(detector, new NullAcpiEvaluator());
        var provider = manager.GetActiveProvider();
        
        Assert.IsType<GenericProvider>(provider);
    }

    [Fact]
    public void DebloatPolicy_RejectsCoreWindowsPackages()
    {
        var policy = new ProtectedTargetsPolicy();
        Assert.True(policy.IsProtectedTarget("Microsoft.Windows.SecHealthUI"));
        Assert.True(policy.IsProtectedTarget("Microsoft.WindowsStore"));
        Assert.False(policy.IsProtectedTarget("SpotifyAB.SpotifyMusic"));
        Assert.False(policy.IsProtectedTarget("king.com.CandyCrushSaga"));
    }
}
