using System.Reflection;

namespace StepWise.Core;

/// <summary>
/// Resolves all IFieldValue&lt;T&gt; properties on a record,
/// returning a plain dictionary of field name → resolved value.
/// </summary>
public static class FieldValueResolver
{
    private static readonly Type FieldValueOpenGeneric = typeof(IFieldValue<>);

    private static readonly HashSet<string> ExcludedProperties = new()
    {
        nameof(WorkflowRequest<object>.StepName),
        nameof(WorkflowRequest<object>.TargetKey),
        nameof(WorkflowRequest<object>.PathParams),
        nameof(WorkflowRequest<object>.Query),
        nameof(WorkflowRequest<object>.Headers),
        "EqualityContract",
    };

    /// <summary>
    /// Resolves all IFieldValue&lt;T&gt; body properties on a WorkflowRequest.
    /// PathParams and Query are excluded — resolve them separately.
    /// </summary>
    public static Dictionary<string, object?> Resolve<TResponse>(
        WorkflowRequest<TResponse> request,
        WorkflowContext context)
        => ResolveProperties(request, context, ExcludedProperties,
            t => t == typeof(WorkflowRequest<TResponse>));

    /// <summary>Resolves a flat dictionary of named string field values into plain values.</summary>
    public static Dictionary<string, object?> ResolveGroup(
        IReadOnlyDictionary<string, IFieldValue<string>> fields,
        WorkflowContext context)
        => fields.ToDictionary(kv => kv.Key, kv => (object?)kv.Value.Resolve(context));

    /// <summary>
    /// Resolves all IFieldValue&lt;T&gt; properties on a BuildableRequest.
    /// </summary>
    public static Dictionary<string, object?> ResolveObject(
        BuildableRequest item,
        WorkflowContext context)
        => ResolveProperties(item, context,
            new HashSet<string> { "EqualityContract" },
            t => t == typeof(BuildableRequest));

    private static Dictionary<string, object?> ResolveProperties(
        object target,
        WorkflowContext context,
        HashSet<string> excludedNames,
        Func<Type, bool> isBaseType)
    {
        var result = new Dictionary<string, object?>();

        var properties = target.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !excludedNames.Contains(p.Name))
            .Where(p => !isBaseType(p.DeclaringType!));

        foreach (var property in properties)
        {
            var value = property.GetValue(target);

            if (value is null)
            {
                result[property.Name] = null;
                continue;
            }

            var fieldValueInterface = GetFieldValueInterface(property.PropertyType);

            if (fieldValueInterface is not null)
            {
                var resolveMethod = fieldValueInterface.GetMethod(nameof(IFieldValue<object>.Resolve))!;
                result[property.Name] = ResolveRecursively(resolveMethod.Invoke(value, [context]), context);
            }
            else
            {
                result[property.Name] = value;
            }
        }

        return result;
    }

    private static object? ResolveRecursively(object? value, WorkflowContext context)
    {
        if (value is null) return null;

        var type = value.GetType();

        if (type.IsPrimitive || value is string || value is decimal || type.IsEnum)
            return value;

        if (value is System.Collections.IList list)
        {
            var result = new List<object?>(list.Count);
            foreach (var item in list)
                result.Add(ResolveRecursively(item, context));
            return result;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (properties.Any(p => GetFieldValueInterface(p.PropertyType) is not null))
            return ResolveProperties(value, context, new HashSet<string> { "EqualityContract" }, _ => false);

        return value;
    }

    private static Type? GetFieldValueInterface(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == FieldValueOpenGeneric)
            return type;

        return type
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == FieldValueOpenGeneric);
    }
}
