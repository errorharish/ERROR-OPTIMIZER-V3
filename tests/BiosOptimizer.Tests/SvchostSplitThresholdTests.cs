using BiosOptimizer.Core.ActionHandlers;
using BiosOptimizer.Core.Models;
using Xunit;

namespace BiosOptimizer.Tests;

public class SvchostSplitThresholdTests
{
    private readonly SvchostSplitThresholdHandler _handler = new();

    [Theory]
    [InlineData(4L * 1024 * 1024 * 1024, 0x4000000u)]
    [InlineData(6L * 1024 * 1024 * 1024, 0x600000u)]
    [InlineData(8L * 1024 * 1024 * 1024, 0x800000u)]
    [InlineData(12L * 1024 * 1024 * 1024, 0xC00000u)]
    [InlineData(16L * 1024 * 1024 * 1024, 0x1000000u)]
    [InlineData(24L * 1024 * 1024 * 1024, 0x1800000u)]
    [InlineData(32L * 1024 * 1024 * 1024, 0x2000000u)]
    [InlineData(64L * 1024 * 1024 * 1024, 0x4000000u)]
    public void SupportedRam_ReturnsCorrectTargetValue(long ramBytes, uint expectedValue)
    {
        var context = new EnvironmentContext();
        context.HardwareProfile.RAM = ramBytes;
        
        var entry = new OptimizationEntry();
        
        Assert.True(_handler.CanApply(entry, context));
        
        var authoritativeValue = _handler.GetAuthoritativeValueForRam(ramBytes);
        Assert.NotNull(authoritativeValue);
        Assert.Equal(expectedValue, authoritativeValue.Value);
    }

    [Theory]
    [InlineData(2L * 1024 * 1024 * 1024)] // 2 GB
    [InlineData(10L * 1024 * 1024 * 1024)] // 10 GB
    [InlineData(128L * 1024 * 1024 * 1024)] // 128 GB
    public void UnsupportedRam_ReturnsFalseForCanApply(long ramBytes)
    {
        var context = new EnvironmentContext();
        context.HardwareProfile.RAM = ramBytes;
        var entry = new OptimizationEntry();
        
        Assert.False(_handler.CanApply(entry, context));
        
        var authoritativeValue = _handler.GetAuthoritativeValueForRam(ramBytes);
        Assert.Null(authoritativeValue);
    }

    [Fact]
    public void Apply_MissingValue_ReturnsFailed()
    {
        var entry = new OptimizationEntry();
        var result = _handler.Apply(entry, "tx-123");
        
        Assert.Equal(ResultStatus.Failed, result.Status);
        Assert.Equal("Missing target value in entry", result.Message);
    }

    [Fact]
    public void Apply_InvalidValue_ReturnsFailed()
    {
        var entry = new OptimizationEntry { Value = "not-a-number" };
        var result = _handler.Apply(entry, "tx-123");
        
        Assert.Equal(ResultStatus.Failed, result.Status);
    }
}
