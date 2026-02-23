using System.Reflection;

namespace StepWise.Core;

/// <summary>
/// Resolves all IFieldValue&lt;T&gt; properties on a workflow request record,
/// returning a plain dictionary of field name → resolved value suitable
/// for serialization by transport implementations.
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

    public static Dictionary<string, object?> Resolve<TResponse>(
        WorkflowRequest<TResponse> request,
        WorkflowContext context)
    {
        var result = new Dictionary<string, object?>();

        var properties = request.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !ExcludedProperties.Contains(p.Name))
            .Where(p => p.DeclaringType != typeof(WorkflowRequest<TResponse>));

        foreach (var property in properties)
        {
            var value = property.GetValue(request);

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
