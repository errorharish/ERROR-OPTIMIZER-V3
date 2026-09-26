using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BiosOptimizer.Core.Implementations;

/// <summary>
/// Real scoring engine: scans all profiles, evaluates which targets are available
/// on the current machine, and returns genuine counts per category.
/// </summary>
public class OptimizationScoreEngine : IOptimizationScoreEngine
{
    private static readonly string[] ProfileIds = { "Normal", "Pro", "Ultimate", "Debloat", "BiosSafe", "MaximumPerformance" };

    public OptimizationScoreDto CalculateScore(
        EnvironmentContext environmentContext,
        IProfileRepository profileRepository,
        IActionRegistry actionRegistry)
    {
        var conditionEvaluator = new ConditionEvaluator();
        int totalAvailable = 0;
        int applied = 0;
        int unsupported = 0;
        int requiresConfirmation = 0;
        var categoryScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var profileId in ProfileIds)
        {
            var profile = profileRepository.LoadResolvedProfile(profileId);
            if (profile == null) continue;

            foreach (var entry in profile.Entries)
            {
                // OS gate
                bool osMatch = entry.Os == null || entry.Os.Count == 0
                    || (environmentContext.IsWindows10 && entry.Os.Contains("win10", StringComparer.OrdinalIgnoreCase))
                    || (environmentContext.IsWindows11 && entry.Os.Contains("win11", StringComparer.OrdinalIgnoreCase));
                if (!osMatch) continue;

                // Condition gate
                if (!conditionEvaluator.Evaluate(entry.Conditions, environmentContext)) continue;

                // Handler availability
                var handler = actionRegistry.GetHandler(entry.Action);
                if (handler == null) { unsupported++; continue; }

                var availability = handler.CheckAvailability(entry, environmentContext);
                if (availability == TargetState.NotAvailable || availability == TargetState.NotApplicable)
                {
                    unsupported++;
                    continue;
                }

                totalAvailable++;

                if (entry.RequiresConfirmation || entry.Risk.Equals("High", StringComparison.OrdinalIgnoreCase))
                    requiresConfirmation++;

                // Check if already applied
                var currentVal = handler.GetCurrentValueDisplay(entry);
                var targetVal = entry.Value?.ToString() ?? "";
                if (string.Equals(currentVal, targetVal, StringComparison.OrdinalIgnoreCase))
                {
                    applied++;
                    var cat = string.IsNullOrWhiteSpace(entry.Category) ? "General" : entry.Category;
                    categoryScores.TryGetValue(cat, out int catScore);
                    categoryScores[cat] = catScore + 1;
                }
            }
        }

        int remaining = Math.Max(0, totalAvailable - applied);
        int overallScore = totalAvailable > 0
            ? (int)Math.Round((double)applied / totalAvailable * 100.0)
            : 0;

        return new OptimizationScoreDto
        {
            OverallScore = overallScore,
            TotalAvailable = totalAvailable,
            Applied = applied,
            Remaining = remaining,
            Skipped = 0,
            Unsupported = unsupported,
            RequiresConfirmation = requiresConfirmation,
            Failed = 0,
            CategoryScores = categoryScores
        };
    }

    public OptimizationSummaryDto GenerateSummary(
        EnvironmentContext environmentContext,
        IProfileRepository profileRepository,
        IActionRegistry actionRegistry)
    {
        var scoreDto = CalculateScore(environmentContext, profileRepository, actionRegistry);
        return new OptimizationSummaryDto
        {
            TotalOptimizationsAvailable = scoreDto.TotalAvailable,
            OptimizationsApplied = scoreDto.Applied,
            RemainingOptimizations = scoreDto.Remaining,
            Skipped = scoreDto.Skipped,
            Unsupported = scoreDto.Unsupported,
            RequiresConfirmation = scoreDto.RequiresConfirmation,
            Failed = scoreDto.Failed,
            CategoryBreakdown = scoreDto.CategoryScores
        };
    }
}
