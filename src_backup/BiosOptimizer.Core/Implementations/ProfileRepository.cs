using System.Text.Json;
using System.Text.Json.Serialization;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations;

public class ProfileRepository : Interfaces.IProfileRepository
{
    private readonly string _profilesDirectory;

    public ProfileRepository(string profilesDirectory)
    {
        _profilesDirectory = profilesDirectory;
    }

    public ProfileDef? LoadProfile(string tierId)
    {
        var path = Path.Combine(_profilesDirectory, $"{tierId}.json");
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<ProfileDef>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (profile != null)
        {
            foreach (var entry in profile.Entries)
            {
                entry.SourceProfile = tierId;
            }
        }

        return profile;
    }

    public ProfileDef? LoadResolvedProfile(string tierId)
    {
        var profile = LoadProfile(tierId);
        if (profile == null) return null;

        var resolvedEntries = new Dictionary<string, OptimizationEntry>();
        var resolvedProcessReductionMin = profile.TargetProcessReduction.Min;
        var resolvedProcessReductionMax = profile.TargetProcessReduction.Max;

        // Recursively load inherited profiles
        LoadAndMergeInheritedProfiles(profile, resolvedEntries, new HashSet<string> { tierId });

        // Add current profile entries, resolving conflicts
        foreach (var entry in profile.Entries)
        {
            MergeEntry(resolvedEntries, entry);
        }

        profile.Entries = resolvedEntries.Values.ToList();
        return profile;
    }

    private void LoadAndMergeInheritedProfiles(ProfileDef profile, Dictionary<string, OptimizationEntry> resolvedEntries, HashSet<string> seen)
    {
        if (profile.Extends == null) return;

        foreach (var parentId in profile.Extends)
        {
            if (seen.Contains(parentId)) continue;
            seen.Add(parentId);

            var parent = LoadProfile(parentId);
            if (parent != null)
            {
                // Recursively load this parent's parents
                LoadAndMergeInheritedProfiles(parent, resolvedEntries, seen);

                foreach (var entry in parent.Entries)
                {
                    MergeEntry(resolvedEntries, entry);
                }
            }
        }
    }

    private void MergeEntry(Dictionary<string, OptimizationEntry> resolvedEntries, OptimizationEntry entry)
    {
        // Key uniquely identifies the target of the optimization
        var key = $"{entry.Action}:{entry.Target}".ToLowerInvariant();

        if (!resolvedEntries.TryGetValue(key, out var existing))
        {
            // Create a deep copy or just add it (we'll serialize/deserialize to copy later if needed, but simple reference is okay for now since we mutate SourceProfile earlier)
            var newEntry = CloneEntry(entry);
            resolvedEntries[key] = newEntry;
        }
        else
        {
            // Conflict resolution: Child entry overrides Target/Value/Conditions but retains highest risk
            var childEntry = CloneEntry(entry);
            childEntry.Risk = GetHighestRisk(existing.Risk, entry.Risk);
            resolvedEntries[key] = childEntry;
        }
    }

    private OptimizationEntry CloneEntry(OptimizationEntry source)
    {
        var json = JsonSerializer.Serialize(source);
        var clone = JsonSerializer.Deserialize<OptimizationEntry>(json)!;
        clone.SourceProfile = source.SourceProfile;
        return clone;
    }

    private string GetHighestRisk(string risk1, string risk2)
    {
        var risks = new[] { "Low", "Medium", "High", "Critical" };
        var idx1 = Array.FindIndex(risks, r => r.Equals(risk1, StringComparison.OrdinalIgnoreCase));
        var idx2 = Array.FindIndex(risks, r => r.Equals(risk2, StringComparison.OrdinalIgnoreCase));

        if (idx1 < 0) idx1 = 0;
        if (idx2 < 0) idx2 = 0;

        return risks[Math.Max(idx1, idx2)];
    }

    public bool ValidateProfile(ProfileDef profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id)) return false;
        if (profile.Entries == null || profile.Entries.Count == 0) return false;

        foreach (var entry in profile.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Action)) return false;
            if (string.IsNullOrWhiteSpace(entry.Target)) return false;
            
            var validRisks = new[] { "Low", "Medium", "High", "Critical" };
            if (!validRisks.Contains(entry.Risk, StringComparer.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
