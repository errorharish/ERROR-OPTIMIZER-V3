using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations;

public class NullHardwareAccess : Interfaces.IHardwareAccess
{
    public HardwareAccessResult ReadEcByte(ushort port, byte register) => HardwareAccessResult.NotSupported;
    public HardwareAccessResult WriteEcByte(ushort port, byte register, byte value) => HardwareAccessResult.NotSupported;
    public HardwareAccessResult ReadMsr(uint msr) => HardwareAccessResult.NotSupported;
    public HardwareAccessResult WriteMsr(uint msr, ulong value) => HardwareAccessResult.NotSupported;
}

public class NullProvider : Interfaces.IProvider
{
    public ProviderInfo GetProviderInfo() => new ProviderInfo { Name = "Phase 1 Stub Provider", Version = "1.0", IsReadOnly = true };
    public IReadOnlyList<Capability> GetCapabilities() => new List<Capability>();
    public IReadOnlyList<BiosSetting> GetSettings() => new List<BiosSetting>();
    public OperationResult SetSetting(string settingId, object? value) => new OperationResult { Status = ResultStatus.NotApplicable, Message = "Not implemented in Phase 1" };
    public VerificationResult Verify(string settingId, object? expectedValue) => new VerificationResult { IsMatch = false, Message = "Not implemented in Phase 1" };
}
