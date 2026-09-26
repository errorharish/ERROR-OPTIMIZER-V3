using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public interface ITierEngine
{
    Task<OptimizationPreview> PreviewAsync(
        string tierId,
        CancellationToken cancellationToken);

    Task<List<OptimizationResult>> ApplyAsync(
        string tierId,
        ConfirmationContext confirmation,
        bool dryRun,
        CancellationToken cancellationToken,
        IProgress<OptimizationResult>? progress = null,
        System.Collections.Generic.HashSet<string>? allowedItemIds = null);
        
    Task<List<OptimizationResult>> ApplyAsync(
        ProfileDef profile,
        ConfirmationContext confirmation,
        bool dryRun,
        CancellationToken cancellationToken,
        IProgress<OptimizationResult>? progress = null,
        System.Collections.Generic.HashSet<string>? allowedItemIds = null);

}
