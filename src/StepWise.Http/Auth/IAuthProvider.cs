using StepWise.Core;

namespace StepWise.Http.Auth;

/// <summary>
/// Applies authentication to an outgoing HTTP request.
/// Implement this interface to create custom auth strategies.
/// </summary>
public interface IAuthProvider
{
    Task ApplyAsync(HttpRequestMessage request, WorkflowContext context);
}
