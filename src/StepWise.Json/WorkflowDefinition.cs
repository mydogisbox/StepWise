using System.Text.Json.Serialization;

namespace StepWise.Json;

/// <summary>
/// A workflow definition loaded from a .workflow.json file — step names, optional assertions.
/// Request files and execution targets are supplied by the caller, not embedded in this JSON.
/// </summary>
public record WorkflowDefinition(
    string Name,
    List<StepInvocation> Steps,
    List<AssertionDefinition>? Assertions = null);

/// <summary>
/// The content of a .requests.json file — a dictionary of named step definitions.
/// </summary>
public record RequestsDefinition(
    Dictionary<string, StepDefinition> Steps
);

/// <summary>
/// Defines how a named step is executed — method, path, target, auth, and defaults.
/// </summary>
public record StepDefinition
{
    public string Target { get; init; } = "";
    public string Method { get; init; } = "POST";
    public string Path   { get; init; } = "";
    public AuthDefinition? Auth { get; init; }
    public Dictionary<string, FieldValueDefinition>? Defaults { get; init; }

    /// <summary>Required for build steps — names the collection that accumulated items are stored under.</summary>
    public string? AccumulateAs { get; init; }
}

/// <summary>
/// Auth configuration for a step definition.
/// </summary>
public record AuthDefinition
{
    /// <summary>none | bearer | apikey</summary>
    public string Type { get; init; } = "none";

    [JsonPropertyName("from")]
    public string? From { get; init; }

    public string? Token { get; init; }
    public string? Header { get; init; }
    public string? QueryParam { get; init; }
    public FieldValueDefinition? Key { get; init; }
}

/// <summary>
/// A single step invocation — references a step by name with optional overrides.
/// Exactly one of Step, Build, or Poll must be set.
/// </summary>
public record StepInvocation
{
    public string? Step  { get; init; }
    public string? Build { get; init; }

    /// <summary>
    /// Name of a step definition to re-execute until <see cref="Until"/> passes or
    /// <see cref="TimeoutMs"/> elapses.
    /// </summary>
    public string? Poll { get; init; }

    /// <summary>A single assertion evaluated after each poll attempt.</summary>
    public AssertionDefinition? Until { get; init; }

    /// <summary>Milliseconds between poll attempts. Default: 500.</summary>
    public int IntervalMs { get; init; } = 500;

    /// <summary>Maximum milliseconds to wait before failing. Default: 10000.</summary>
    public int TimeoutMs { get; init; } = 10000;

    public string? CaptureAs { get; init; }
    public string? CaptureRequestAs { get; init; }
    public Dictionary<string, FieldValueDefinition>? With { get; init; }
}

/// <summary>
/// An IFieldValue&lt;T&gt; equivalent in JSON.
/// </summary>
public record FieldValueDefinition
{
    [JsonPropertyName("static")]
    public System.Text.Json.JsonElement? Static { get; init; }

    [JsonPropertyName("generated")]
    public string? Generated { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }
}

/// <summary>
/// An assertion to evaluate after all steps complete.
/// </summary>
public record AssertionDefinition
{
    [JsonPropertyName("equal")]    public List<string>? Equal    { get; init; }
    [JsonPropertyName("notEqual")] public List<string>? NotEqual { get; init; }
    [JsonPropertyName("single")]   public string? Single   { get; init; }
    [JsonPropertyName("empty")]    public string? Empty    { get; init; }
    [JsonPropertyName("notEmpty")] public string? NotEmpty { get; init; }
}
