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
            return new FromJsonValue(def.From);

        throw new JsonWorkflowException(
            "A field value definition must have exactly one of: 'static', 'generated', or 'from'.");
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
                element.TryGetProperty("generated", out _))
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

public sealed class FromJsonValue(string path) : IJsonFieldValue
{
    public object? Resolve(Dictionary<string, object?> captures) =>
        JsonWorkflowRunner.ResolveCapturePath(path, captures);
}
