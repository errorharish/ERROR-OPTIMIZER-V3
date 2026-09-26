using BiosOptimizer.Safety.Mocks;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Detection.Tests;

public class DetectionTests
{
    [Fact]
    public void MockEnvironment_HasExpectedValues()
    {
        var detector = new MockEnvironmentDetector();
        var env = detector.Detect();
        
        Assert.True(env.IsWindows10);
        Assert.False(env.IsWindows11);
        Assert.True(env.IsDesktop);
        Assert.False(env.HasBattery);
        Assert.Equal(16, env.RamSizeGb);
    }
    
    [Fact]
    public void MockProcessSnapshot_ReturnsExpectedCount()
    {
        var snapshot = new MockProcessSnapshot { MockCount = 100 };
        var result = snapshot.TakeSnapshot();
        
        Assert.Equal(100, result.ProcessCount);
    }
}
