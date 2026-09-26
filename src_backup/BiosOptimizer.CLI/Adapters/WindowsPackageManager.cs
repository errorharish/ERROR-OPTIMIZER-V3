using System.Runtime.InteropServices;
using Windows.Management.Deployment;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.CLI.Adapters;

public class WindowsPackageManager : IPackageManager
{
    public List<InstalledPackage> GetInstalledPackages()
    {
        var list = new List<InstalledPackage>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return list;

        try
        {
            var pm = new PackageManager();
            var packages = pm.FindPackagesForUser("");

            foreach (var pkg in packages)
            {
                var dispName = "";
                var pubDispName = "";
                try { dispName = pkg.DisplayName; } catch { }
                try { pubDispName = pkg.PublisherDisplayName; } catch { }
                
                if (string.IsNullOrWhiteSpace(dispName)) dispName = pkg.Id.Name;
                if (string.IsNullOrWhiteSpace(pubDispName)) pubDispName = GetFriendlyPublisher(pkg.Id.Publisher);
                
                list.Add(new InstalledPackage
                {
                    Name = pkg.Id.Name,
                    DisplayName = dispName,
                    FullName = pkg.Id.FullName,
                    FamilyName = pkg.Id.FamilyName,
                    Publisher = pkg.Id.Publisher,
                    PublisherDisplayName = pubDispName,
                    Version = $"{pkg.Id.Version.Major}.{pkg.Id.Version.Minor}.{pkg.Id.Version.Build}.{pkg.Id.Version.Revision}",
                    IsFramework = pkg.IsFramework,
                    IsResource = pkg.IsResourcePackage,
                    IsSystemComponent = pkg.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System,
                    UserInstalled = true,
                    Provisioned = false // We'd need to query provisioned packages separately or assume false for this basic list
                });
            }
        }
        catch (Exception)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-AppxPackage | Select-Object Name, PackageFullName, Publisher, Version, IsFramework, IsResourcePackage, SignatureKind | ConvertTo-Json -Depth 2\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    using var doc = System.Text.Json.JsonDocument.Parse(output);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            var name = elem.GetProperty("Name").GetString();
                            var fullName = elem.GetProperty("PackageFullName").GetString();
                            var publisher = elem.GetProperty("Publisher").GetString();
                            var version = elem.GetProperty("Version").GetString();
                            var isFramework = elem.TryGetProperty("IsFramework", out var f) && f.GetBoolean();
                            var isResource = elem.TryGetProperty("IsResourcePackage", out var r) && r.GetBoolean();
                            var signatureKind = elem.TryGetProperty("SignatureKind", out var s) ? s.ToString() : "";
                            
                            var dispName = name ?? "";
                            var pubDispName = GetFriendlyPublisher(publisher ?? "");
                            
                            list.Add(new InstalledPackage
                            {
                                Name = name ?? "",
                                DisplayName = dispName,
                                FullName = fullName ?? "",
                                FamilyName = "", // not queried
                                Publisher = publisher ?? "",
                                PublisherDisplayName = pubDispName,
                                Version = version ?? "",
                                IsFramework = isFramework,
                                IsResource = isResource,
                                IsSystemComponent = signatureKind == "System",
                                UserInstalled = true
                            });
                        }
                    }
                }
            }
            catch { }
        }

        return list;
    }

    private string GetFriendlyPublisher(string rawPublisher)
    {
        if (string.IsNullOrWhiteSpace(rawPublisher)) return string.Empty;
        
        var parts = rawPublisher.Split(',');
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.StartsWith("O=")) return p.Substring(2);
        }
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.StartsWith("CN=")) return p.Substring(3);
        }
        return rawPublisher;
    }

    public InstalledPackage? GetPackage(string name)
    {
        return GetInstalledPackages().FirstOrDefault(p => 
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
            p.FullName.Equals(name, StringComparison.OrdinalIgnoreCase) || 
            p.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public bool RemovePackage(string fullName, bool allUsers)
    {
        try
        {
            var pm = new PackageManager();
            var result = pm.RemovePackageAsync(fullName).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RestorePackage(string familyName)
    {
        // Restoration generally requires Store interaction or reinstalling from provisioned files.
        // Returning false as we cannot blindly restore without a specific path.
        return false;
    }
}
