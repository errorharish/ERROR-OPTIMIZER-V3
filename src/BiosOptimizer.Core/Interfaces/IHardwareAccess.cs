using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public interface IHardwareAccess
{
    HardwareAccessResult ReadEcByte(ushort port, byte register);
    HardwareAccessResult WriteEcByte(ushort port, byte register, byte value);
    HardwareAccessResult ReadMsr(uint msr);
    HardwareAccessResult WriteMsr(uint msr, ulong value);
}

public interface IProvider
{
    BiosOptimizer.Core.Models.ProviderInfo GetProviderInfo();
    IReadOnlyList<Capability> GetCapabilities();
    IReadOnlyList<BiosSetting> GetSettings();
    OperationResult SetSetting(string settingId, object? value);
    VerificationResult Verify(string settingId, object? expectedValue);
}
