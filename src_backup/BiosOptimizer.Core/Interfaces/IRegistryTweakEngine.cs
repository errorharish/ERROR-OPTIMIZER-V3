using System.Threading.Tasks;


namespace BiosOptimizer.Core.Interfaces
{
    public class RegistryTweakPlan
    {
        public ulong DetectedRamBytes { get; set; }
        public string DetectedRamGb { get; set; } = string.Empty;
        public string SelectedBaselineGb { get; set; } = string.Empty;
        
        public string CurrentValueHex { get; set; } = string.Empty;
        public string CurrentValueDec { get; set; } = string.Empty;
        public string CurrentType { get; set; } = string.Empty;

        public string TargetValueHex { get; set; } = string.Empty;
        public string TargetValueDec { get; set; } = string.Empty;
        
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
    public interface IRegistryTweakEngine
    {
        Task<RegistryTweakPlan> PlanRegistryTweakAsync();
        Task<(bool Success, string Message, string Status)> ApplyRegistryTweakAsync();
        Task<(bool Success, string Message, string Status)> RestoreRegistryTweakAsync();
    }
}



