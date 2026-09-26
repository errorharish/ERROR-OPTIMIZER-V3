using System.Management;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.CLI.Adapters;

public class WindowsSystemRestoreManager : ISystemRestoreManager
{
    public bool CreateRestorePoint(string description)
    {
#pragma warning disable CA1416 // Validate platform compatibility
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            var path = new ManagementPath("SystemRestore");
            var options = new ObjectGetOptions();
            
            using var wmiClass = new ManagementClass(scope, path, options);
            var inParams = wmiClass.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = 0; // APPLICATION_INSTALL
            inParams["EventType"] = 100;      // BEGIN_SYSTEM_CHANGE

            var outParams = wmiClass.InvokeMethod("CreateRestorePoint", inParams, null);
            return outParams != null;
        }
        catch
        {
            return false;
        }
#pragma warning restore CA1416
    }
}
