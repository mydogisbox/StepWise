namespace Walkthrough.Core;

/// <summary>
/// Implemented by targets that can execute a step without throwing on non-2xx responses.
/// HttpTarget implements this — custom targets may opt in.
/// </summary>
public interface IRawTarget
{
    Task<object> ExecuteRawAsync<TResponse>(WorkflowRequest<TResponse> request, Dictionary<string, object?> resolvedFields, WorkflowContext context);
}
