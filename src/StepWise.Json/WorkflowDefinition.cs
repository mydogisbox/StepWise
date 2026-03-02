using System.Text.Json.Serialization;

namespace StepWise.Json;

/// <summary>
/// A workflow definition loaded from a .workflow.json file.
/// References external .requests.json files rather than embedding step definitions inline.
/// </summary>
public record WorkflowDefinition(
    string Name,
    Dictionary<string, TargetDefinition> Targets,

    /// <summary>
    /// Paths to .requests.json files, resolved relative to the workflow file.
    /// Step definitions are merged from all listed files in order.
    /// </summary>
    List<string> Requests,

    List<StepInvocation> Steps,
    List<AssertionDefinition>? Assertions = null
);

/// <summary>
/// The content of a .requests.json file — a dictionary of named step definitions.
/// </summary>
public record RequestsDefinition(
    Dictionary<string, StepDefinition> Steps
);

/// <summary>
/// A named execution target — for now always HTTP.
/// </summary>
public record TargetDefinition(
    string BaseUrl,
    string Type = "http"
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
/// </summary>
public record StepInvocation
{
    public string? Step  { get; init; }
    public string? Build { get; init; }
    public string? CaptureAs { get; init; }
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
