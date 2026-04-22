using System.Text.Json;
using StepWise.Core;

namespace StepWise.Json;

/// <summary>
/// Resolves a FieldValueDefinition to a runtime value, using the captures
/// dictionary for From(...) lookups.
/// </summary>
public static class JsonValueResolver
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, Func<object?>> Generators = new()
    {
        ["guid"]    = () => Guid.NewGuid().ToString(),
        ["email"]   = () => $"user-{Guid.NewGuid():N}@test.com",
        ["int"]     = () => (object)Random.Shared.Next(1, 10000),
        ["decimal"] = () => (object)(decimal)Math.Round(Random.Shared.NextDouble() * 100, 2),
    };

    public static IJsonFieldValue Resolve(FieldValueDefinition def)
    {
        if (def.Static.HasValue)
        {
            var el = def.Static.Value;
            return el.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? new NestedStaticJsonValue(el)
                : new StaticJsonValue(JsonElementToObject(el));
        }

        if (def.Generated is not null)
        {
            if (!Generators.TryGetValue(def.Generated.ToLower(), out var generator))
                throw new JsonWorkflowException(
                    $"Unknown generator '{def.Generated}'. Available: {string.Join(", ", Generators.Keys)}");
            return new GeneratedJsonValue(generator);
        }

        if (def.From is not null)
        {
            var fallback = def.Default is not null ? Resolve(def.Default) : null;
            return new FromJsonValue(def.From, fallback);
        }

        if (def.Template is not null)
            return new TemplateJsonValue(def.Template);

        throw new JsonWorkflowException(
            "A field value definition must have exactly one of: 'static', 'generated', 'from', or 'template'.");
    }

    public static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String  => element.GetString(),
        JsonValueKind.Number  => element.TryGetInt32(out var i)  ? (object?)i
                               : element.TryGetInt64(out var l)  ? l
                               : element.GetDouble(),
        JsonValueKind.True    => true,
        JsonValueKind.False   => false,
        JsonValueKind.Null    => null,
        JsonValueKind.Array   => element.EnumerateArray().Select(JsonElementToObject).ToList(),
        JsonValueKind.Object  => element.EnumerateObject()
                                    .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
        _                     => null
    };

    /// <summary>
    /// Resolves a JsonElement, treating any JSON object that has a "static", "from", or
    /// "generated" key as a FieldValueDefinition to resolve. All other objects are treated
    /// as structural nodes whose children are resolved recursively.
    /// </summary>
    internal static object? ResolveElement(JsonElement element, Dictionary<string, object?> captures)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("static", out _) ||
                element.TryGetProperty("from",   out _) ||
                element.TryGetProperty("generated", out _) ||
                element.TryGetProperty("template", out _) ||
                element.TryGetProperty("default", out _))
            {
                var def = JsonSerializer.Deserialize<FieldValueDefinition>(element, DeserializeOptions)!;
                return Resolve(def).Resolve(captures);
            }
            return element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ResolveElement(p.Value, captures));
        }
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray()
                .Select(e => (object?)ResolveElement(e, captures))
                .ToList();
        return JsonElementToObject(element);
    }
}

/// <summary>
/// A resolved field value in the JSON workflow engine.
/// Resolves against a captures dictionary rather than WorkflowContext.
/// </summary>
public interface IJsonFieldValue
{
    object? Resolve(Dictionary<string, object?> captures);
}

public sealed class StaticJsonValue(object? value) : IJsonFieldValue
{
    public object? Resolve(Dictionary<string, object?> _) => value;
}

/// <summary>
/// A static value whose children are JSON objects and may themselves be FieldValueDefinitions.
/// Resolved lazily so that nested "from" references can be evaluated against current captures.
/// </summary>
public sealed class NestedStaticJsonValue(System.Text.Json.JsonElement element) : IJsonFieldValue
{
    public object? Resolve(Dictionary<string, object?> captures) =>
        JsonValueResolver.ResolveElement(element, captures);
}

public sealed class GeneratedJsonValue(Func<object?> generator) : IJsonFieldValue
{
    public object? Resolve(Dictionary<string, object?> _) => generator();
}

/// <summary>
/// Resolves a string template where <c>{capture.path}</c> placeholders are substituted with
/// resolved capture values. Use <c>{{</c> and <c>}}</c> for literal braces.
/// Throws <see cref="JsonWorkflowException"/> if any placeholder cannot be resolved.
/// </summary>
public sealed class TemplateJsonValue(string template) : IJsonFieldValue
{
    public object? Resolve(Dictionary<string, object?> captures)
    {
        var result = new System.Text.StringBuilder();
        var i = 0;

        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                // Escaped brace: {{ → {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    result.Append('{');
                    i += 2;
                    continue;
                }

                var close = template.IndexOf('}', i + 1);
                if (close < 0)
                    throw new JsonWorkflowException(
                        $"Unclosed '{{' in template: '{template}'.");

                var path = template[(i + 1)..close];
                var value = JsonWorkflowRunner.ResolveCapturePath(path, captures);
                if (value is null)
                    throw new JsonWorkflowException(
                        $"Template placeholder '{{{path}}}' could not be resolved in template: '{template}'.");

                result.Append(value);
                i = close + 1;
            }
            else if (template[i] == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                // Escaped brace: }} → }
                result.Append('}');
                i += 2;
            }
            else
            {
                result.Append(template[i++]);
            }
        }

        return result.ToString();
    }
}

public sealed class FromJsonValue(string path, IJsonFieldValue? fallback = null) : IJsonFieldValue
{
    // Root key is everything before the first '.' or '['.
    private static string RootKey(string p)
    {
        var d = p.IndexOf('.');
        var b = p.IndexOf('[');
        var end = (d, b) switch
        {
            (< 0, < 0) => p.Length,
            (< 0, _)   => b,
            (_, < 0)   => d,
            _          => Math.Min(d, b)
        };
        return p[..end];
    }

    public object? Resolve(Dictionary<string, object?> captures)
    {
        // Use the default only when the root capture key is entirely absent —
        // not when the root ran but a nested field is missing.
        if (!captures.ContainsKey(RootKey(path)))
            return fallback?.Resolve(captures);

        var resolved = JsonWorkflowRunner.ResolveCapturePath(path, captures);

        if (resolved is null && fallback is not null)
            throw new JsonWorkflowException(
                $"'{path}' could not be resolved: '{RootKey(path)}' was captured but the field is null or missing. " +
                $"The default is only applied when the step has not run.");

        return resolved;
    }
}
