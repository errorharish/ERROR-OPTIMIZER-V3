using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Bios;

public class ProviderManager : IProviderManager
{
    private readonly IEnvironmentDetector _detector;
    private readonly IAcpiEvaluator _acpiEvaluator;
    private EnvironmentContext? _context;

    public ProviderManager(IEnvironmentDetector detector, IAcpiEvaluator acpiEvaluator)
    {
        _detector = detector;
        _acpiEvaluator = acpiEvaluator;
    }

    public IBiosProvider GetActiveProvider()
    {
        _context ??= _detector.Detect();
        var manufacturer = _context.Manufacturer.ToLowerInvariant();
        var biosVendor = _context.BIOSVendor.ToLowerInvariant();

        if (manufacturer.Contains("dell") || biosVendor.Contains("dell"))
        {
            var p = new DellProvider();
            if (p.GetProviderInfo().Status == "Available") return p;
        }
        else if (manufacturer.Contains("hp") || manufacturer.Contains("hewlett-packard") || biosVendor.Contains("hp"))
        {
            var p = new HpProvider();
            if (p.GetProviderInfo().Status == "Available") return p;
        }
        else if (manufacturer.Contains("lenovo") || biosVendor.Contains("lenovo"))
        {
            var p = new LenovoProvider();
            if (p.GetProviderInfo().Status == "Available") return p;
        }
        else if (manufacturer.Contains("asus") || biosVendor.Contains("american megatrends"))
        {
            var p = new AsusProvider(_acpiEvaluator);
            if (p.GetProviderInfo().Status == "Available") return p;
        }

        return new GenericProvider(_context);
    }
}
