namespace BiosOptimizer.Core.Interfaces;

public class InstalledPackage
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string PublisherDisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsFramework { get; set; }
    public bool IsResource { get; set; }
    public bool IsSystemComponent { get; set; }
    public bool UserInstalled { get; set; }
    public bool Provisioned { get; set; }
}

public interface IPackageManager
{
    List<InstalledPackage> GetInstalledPackages();
    InstalledPackage? GetPackage(string name);
    bool RemovePackage(string fullName, bool allUsers);
    bool RestorePackage(string familyName);
}

public interface IOemApplicationDetector
{
    List<string> DetectOemApplications();
}
