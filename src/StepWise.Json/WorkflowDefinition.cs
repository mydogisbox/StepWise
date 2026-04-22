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
/// A target entry in the targets file — a base URL with optional default headers.
/// </summary>
public record TargetDefinition
{
    public string BaseUrl { get; init; } = "";
    public Dictionary<string, FieldValueDefinition>? Headers { get; init; }

    public static implicit operator TargetDefinition(string url) => new() { BaseUrl = url };
}

/// <summary>
/// Defines how a named step is executed — method, path, target, auth, and defaults.
/// </summary>
public record StepDefinition
{
    public string Target { get; init; } = "";
    public string Method { get; init; } = "POST";
    public string Path   { get; init; } = "";
    public AuthDefinition? Auth { get; init; }

    /// <summary>
    /// Values substituted into <c>{placeholder}</c> segments of <see cref="Path"/>.
    /// Each key must match a placeholder name in the path template.
    /// Path parameters are never sent in the request body.
    /// </summary>
    public Dictionary<string, FieldValueDefinition>? PathParams { get; init; }

    /// <summary>
    /// Key-value pairs appended to the URL as a query string (<c>?key=value&amp;…</c>).
    /// Resolved independently of the request body.
    /// </summary>
    public Dictionary<string, FieldValueDefinition>? Query { get; init; }

    /// <summary>HTTP headers sent with every invocation of this step. Merged over target-level headers.</summary>
    public Dictionary<string, FieldValueDefinition>? Headers { get; init; }

    /// <summary>Default field values sent in the request body.</summary>
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

    /// <summary>
    /// Stores the resolved request payload under this key in addition to capturing the response.
    /// Useful when the server does not echo request fields back in the response and a downstream
    /// step or assertion needs to reference them. The captured value is a
    /// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}">Dictionary&lt;string, object?&gt;</see>
    /// containing all resolved field values sent in the request body.
    /// </summary>
    public string? CaptureRequestAs { get; init; }
    public string? Workflow { get; init; }

    /// <summary>Per-invocation body field overrides. Merged over the step definition's defaults.</summary>
    public Dictionary<string, FieldValueDefinition>? With { get; init; }

    /// <summary>Per-invocation path parameter overrides. Merged over the step definition's pathParams.</summary>
    public Dictionary<string, FieldValueDefinition>? PathParams { get; init; }

    /// <summary>Per-invocation query parameter overrides. Merged over the step definition's query.</summary>
    public Dictionary<string, FieldValueDefinition>? Query { get; init; }

    /// <summary>Per-invocation header overrides. Merged over target-level and step-level headers.</summary>
    public Dictionary<string, FieldValueDefinition>? Headers { get; init; }
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

    /// <summary>
    /// A string template where <c>{capture.path}</c> placeholders are substituted with resolved capture values.
    /// Use <c>{{</c> and <c>}}</c> for literal braces.
    /// Example: <c>"Bearer {login.token}"</c>
    /// </summary>
    [JsonPropertyName("template")]
    public string? Template { get; init; }

    /// <summary>
    /// Fallback value used when <see cref="From"/> resolves to null (capture missing or field absent).
    /// Supports any field value type: <c>{ "static": "…" }</c>, <c>{ "generated": "…" }</c>, or <c>{ "from": "…" }</c>.
    /// </summary>
    [JsonPropertyName("default")]
    public FieldValueDefinition? Default { get; init; }
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
    [JsonPropertyName("count")]    public List<string>? Count    { get; init; }
}
