namespace StepWise.Core;

/// <summary>
/// A field value that always returns the same hardcoded value.
/// </summary>
internal sealed class StaticValue<T>(T value) : IFieldValue<T>
{
    public T Resolve(WorkflowContext context) => value;
}

/// <summary>
/// A field value that invokes a lambda each time it is resolved.
/// </summary>
internal sealed class GeneratedValue<T>(Func<T> generator) : IFieldValue<T>
{
    public T Resolve(WorkflowContext context) => generator();
}

/// <summary>
/// A field value that looks up a value from the workflow context using a typed lambda.
/// References the captured response of a previous step.
/// </summary>
internal sealed class FromValue<T>(Func<WorkflowContext, T> selector) : IFieldValue<T>
{
    public T Resolve(WorkflowContext context) => selector(context);
}

/// <summary>
/// Factory methods for creating IFieldValue instances.
/// Import with: using static StepWise.Core.FieldValues;
/// </summary>
public static class FieldValues
{
    /// <summary>
    /// A field value that always returns the given hardcoded value.
    /// </summary>
    public static IFieldValue<T> Static<T>(T value) =>
        new StaticValue<T>(value);

    /// <summary>
    /// A field value that invokes the given lambda each time it is resolved.
    /// Use this for generated data such as random emails, GUIDs, etc.
    /// </summary>
    public static IFieldValue<T> Generated<T>(Func<T> generator) =>
        new GeneratedValue<T>(generator);

    /// <summary>
    /// A field value that resolves by looking up a value from the workflow context.
    /// Use this in Default definitions to reference the response of a previous step.
    /// In test bodies, prefer using the return value of the previous step directly.
    /// </summary>
    public static IFieldValue<T> From<T>(Func<WorkflowContext, T> selector) =>
        new FromValue<T>(selector);
}
