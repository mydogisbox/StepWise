namespace StepWise.Core;

/// <summary>
/// Represents an execution target — a combination of location and protocol.
/// Each target knows how to execute requests against a specific endpoint
/// using a specific transport (HTTP, gRPC, etc.).
/// </summary>
public interface ITarget
{
    Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request, WorkflowContext context);
}
