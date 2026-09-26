using BiosOptimizer.Safety;
using BiosOptimizer.Safety.Mocks;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Safety.Tests;

public class SafetyTests
{
    [Fact]
    public void ActionHandler_FailingVerification_ReportsFailure()
    {
        var svcManager = new FailingMockServiceManager();
        svcManager.AddMockService("TestService", BiosOptimizer.Core.Interfaces.ServiceStartupType.Automatic);
        
        var safetyPolicy = new ProtectedTargetsPolicy();
        var handler = new BiosOptimizer.Core.ActionHandlers.SetServiceStartupHandler(svcManager, safetyPolicy, new VerificationEngine());

        var entry = new OptimizationEntry { Id = "test", Target = "TestService", Value = "Disabled" };
        var result = handler.Apply(entry);

        // The mock will report Disabled when set, but immediately fail verification (it will return Automatic on readback).
        // Since rollback is removed, it should just fail.
        Assert.Equal(BiosOptimizer.Core.Models.ResultStatus.Failed, result.Status);
    }
}

public class FailingMockServiceManager : MockServiceManager
{
    private bool _firstSetDone = false;

    // Simulate verification failure by returning original value after change, 
    // unless it's the rollback setting it back to Automatic.
    public override ServiceStartupType GetStartupType(string serviceName)
    {
        if (serviceName == "TestService")
        {
            if (_firstSetDone) return ServiceStartupType.Automatic;
        }
        return base.GetStartupType(serviceName);
    }

    public override bool SetStartupType(string serviceName, ServiceStartupType startupType)
    {
        if (serviceName == "TestService")
        {
            if (startupType == ServiceStartupType.Disabled)
            {
                _firstSetDone = true;
                return true;
            }
            if (startupType == ServiceStartupType.Automatic)
            {
                _firstSetDone = false;
                return true;
            }
        }
        return base.SetStartupType(serviceName, startupType);
    }
}
