using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public interface IVerificationEngine
{
    bool Verify(string actionId, object expectedState);
}

public interface ISafetyPolicy
{
    bool IsProtectedTarget(string targetName);
}

public interface ISystemRestoreManager
{
    bool CreateRestorePoint(string description);
}
