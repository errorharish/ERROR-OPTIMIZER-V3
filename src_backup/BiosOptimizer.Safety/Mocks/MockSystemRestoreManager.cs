using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Safety.Mocks;

public class MockSystemRestoreManager : ISystemRestoreManager
{
    public bool SimulateFailure { get; set; } = false;
    public bool WasCalled { get; private set; } = false;

    public bool CreateRestorePoint(string description)
    {
        WasCalled = true;
        return !SimulateFailure;
    }
}
