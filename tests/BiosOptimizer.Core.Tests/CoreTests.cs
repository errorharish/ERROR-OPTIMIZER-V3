using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Safety;
using BiosOptimizer.Safety.Mocks;
using System.IO;
using System.Text.Json;

namespace BiosOptimizer.Core.Tests;

public class CoreTests
{
    [Fact]
    public void ConditionEvaluator_IsWindows11_EvaluatesCorrectly()
    {
        var evaluator = new ConditionEvaluator();
        var env = new EnvironmentContext { IsWindows11 = true };
        
        var conditions = new List<ConditionDef> { new ConditionDef { Type = "Windows11", Value = "True" } };
        
        Assert.True(evaluator.Evaluate(conditions, env));
    }

    [Fact]
    public void ProfileRepo_ValidatesProfile()
    {
        var repo = new ProfileRepository("mock");
        var profile = new ProfileDef { Id = "Test", Entries = new List<OptimizationEntry> { new OptimizationEntry { Action = "Action", Target = "Target", Risk = "Low" } } };
        Assert.True(repo.ValidateProfile(profile));
    }

    [Fact]
    public void SafetyPolicy_RejectsProtectedTargets()
    {
        var policy = new ProtectedTargetsPolicy();
        Assert.True(policy.IsProtectedTarget("WinDefend"));
        Assert.False(policy.IsProtectedTarget("DiagTrack"));
    }

    [Fact]
    public void ProfileRepo_ResolvesInheritanceAndConflicts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var parent = new ProfileDef
            {
                Id = "Parent",
                Entries = new List<OptimizationEntry>
                {
                    new OptimizationEntry { Action = "SetServiceStartup", Target = "ServiceA", Value = "Disabled", Risk = "Low" },
                    new OptimizationEntry { Action = "SetServiceStartup", Target = "ServiceB", Value = "Manual", Risk = "High" }
                }
            };

            var child = new ProfileDef
            {
                Id = "Child",
                Extends = new List<string> { "Parent" },
                Entries = new List<OptimizationEntry>
                {
                    new OptimizationEntry { Action = "SetServiceStartup", Target = "ServiceB", Value = "Disabled", Risk = "Low" },
                    new OptimizationEntry { Action = "SetServiceStartup", Target = "ServiceC", Value = "Disabled", Risk = "Medium" }
                }
            };

            File.WriteAllText(Path.Combine(tempDir, "Parent.json"), JsonSerializer.Serialize(parent));
            File.WriteAllText(Path.Combine(tempDir, "Child.json"), JsonSerializer.Serialize(child));

            var repo = new ProfileRepository(tempDir);
            var resolved = repo.LoadResolvedProfile("Child");

            Assert.NotNull(resolved);
            Assert.Equal(3, resolved.Entries.Count);
            
            var serviceA = resolved.Entries.Find(e => e.Target == "ServiceA");
            Assert.NotNull(serviceA);
            Assert.Equal("Disabled", serviceA.Value?.ToString());
            Assert.Equal("Low", serviceA.Risk);
            Assert.Equal("Parent", serviceA.SourceProfile);

            var serviceB = resolved.Entries.Find(e => e.Target == "ServiceB");
            Assert.NotNull(serviceB);
            Assert.Equal("Disabled", serviceB.Value?.ToString()); // Child overrides value
            Assert.Equal("High", serviceB.Risk); // Risk should be High (Parent=High, Child=Low)
            Assert.Equal("Child", serviceB.SourceProfile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
