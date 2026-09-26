using System.Text.Json.Serialization;

namespace BiosOptimizer.Core.Models;

public class TargetProcessReduction
{
    [JsonPropertyName("min")]
    public int Min { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}

public class ConditionDef
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class OptimizationEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("os")]
    public List<string> Os { get; set; } = new();

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = string.Empty;

    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; set; }

    [JsonPropertyName("warning")]
    public string Warning { get; set; } = string.Empty;

    [JsonPropertyName("conditions")]
    public List<ConditionDef> Conditions { get; set; } = new();

    [JsonPropertyName("verification")]
    public string Verification { get; set; } = string.Empty;

    [JsonPropertyName("rollback")]
    public string Rollback { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceProfile { get; set; } = string.Empty;
}

public class ProfileDef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("targetProcessReduction")]
    public TargetProcessReduction TargetProcessReduction { get; set; } = new();

    [JsonPropertyName("extends")]
    public List<string> Extends { get; set; } = new();

    [JsonPropertyName("entries")]
    public List<OptimizationEntry> Entries { get; set; } = new();
}
