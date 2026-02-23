namespace StepWise.Core;

/// <summary>
/// Represents a single executable step in a workflow.
/// Implementations are responsible for resolving field values, executing
/// against a specific transport, capturing the response, and returning it.
/// </summary>
public interface IWorkflowStep<TRequest, TResponse>
    where TRequest : WorkflowRequest<TResponse>
{
    Task<TResponse> ExecuteAsync(TRequest request, WorkflowContext context);
}
