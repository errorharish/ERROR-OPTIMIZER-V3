namespace BiosOptimizer.Core.Models;

public class MachineIdentity
{
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string BIOSVersion { get; set; } = string.Empty;
    public string WindowsVersion { get; set; } = string.Empty;
}
