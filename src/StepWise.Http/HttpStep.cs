using StepWise.Core;
using StepWise.Http.Auth;

namespace StepWise.Http;

/// <summary>
/// Declares the HTTP-specific details for executing a request.
/// Subclasses define Method, Path, and optionally Auth and Query.
/// HttpTarget discovers and instantiates these automatically.
/// </summary>
public abstract class HttpStep<TRequest, TResponse>
    where TRequest : WorkflowRequest<TResponse>
{
    public abstract HttpMethod Method { get; }
    public abstract string Path { get; }
    public virtual IAuthProvider Auth => NoAuth.Instance;

    /// <summary>
    /// Key-value pairs appended to the URL as a query string (<c>?key=value&amp;…</c>).
    /// Defined alongside the path as part of the URL shape, not per-request data.
    /// </summary>
    public virtual IReadOnlyDictionary<string, IFieldValue<string>> Query { get; } =
        new Dictionary<string, IFieldValue<string>>();

    /// <summary>
    /// HTTP headers sent with every invocation of this step.
    /// Merged over target-level headers; request-level headers (from <see cref="WorkflowRequest{TResponse}.Headers"/>) override these.
    /// </summary>
    public virtual IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>();
}
