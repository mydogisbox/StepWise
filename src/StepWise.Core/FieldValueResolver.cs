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
        "EqualityContract",
    };

    /// <summary>
    /// Resolves all IFieldValue&lt;T&gt; properties on a WorkflowRequest.
    /// </summary>
    public static Dictionary<string, object?> Resolve<TResponse>(
        WorkflowRequest<TResponse> request,
        WorkflowContext context)
        => ResolveProperties(request, context, ExcludedProperties,
            t => t == typeof(WorkflowRequest<TResponse>));

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
                result[property.Name] = resolveMethod.Invoke(value, [context]);
            }
            else
            {
                result[property.Name] = value;
            }
        }

        return result;
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
