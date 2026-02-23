namespace StepWise.Core;

/// <summary>
/// Represents a field value on a workflow request that is resolved at execution time.
/// Use Static(), Generated(), or From() to create instances.
/// </summary>
public interface IFieldValue<T>
{
    T Resolve(WorkflowContext context);
}
