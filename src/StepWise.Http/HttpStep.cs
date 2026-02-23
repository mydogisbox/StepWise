using StepWise.Core;
using StepWise.Http.Auth;

namespace StepWise.Http;

/// <summary>
/// Declares the HTTP-specific details for executing a request.
/// Subclasses define Method, Path, and optionally Auth.
/// HttpTarget discovers and instantiates these automatically.
/// </summary>
public abstract class HttpStep<TRequest, TResponse>
    where TRequest : WorkflowRequest<TResponse>
{
    public abstract HttpMethod Method { get; }
    public abstract string Path { get; }
    public virtual IAuthProvider Auth => NoAuth.Instance;
}
