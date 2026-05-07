using Walkthrough.Core;

namespace Walkthrough.Http;

internal interface IHttpStep<TResponse>
{
    Task<TResponse> RunAsync(
        string baseUrl,
        HttpWorkflowRequest<TResponse> request,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context);
}

/// <summary>
/// Declares the HTTP-specific execution details for a request type.
/// Subclasses define Method, Path, and optionally Query and Headers.
/// Registered with an <see cref="HttpTarget"/> via Register().
/// </summary>
public abstract class HttpStep<TRequest, TResponse> : IHttpStep<TResponse>
    where TRequest : HttpWorkflowRequest<TResponse>
{
    public abstract HttpMethod Method { get; }
    public abstract string Path { get; }

    /// <summary>
    /// Key-value pairs appended to the URL as a query string (<c>?key=value&amp;…</c>).
    /// Defined alongside the path as part of the URL shape, not per-request data.
    /// </summary>
    public virtual IReadOnlyDictionary<string, IFieldValue<string>> Query { get; } =
        new Dictionary<string, IFieldValue<string>>();

    /// <summary>
    /// HTTP headers sent with every invocation of this step.
    /// Merged over target-level headers; request-level headers (from <see cref="HttpWorkflowRequest{TResponse}.Headers"/>) override these.
    /// </summary>
    public virtual IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>();

    /// <summary>
    /// Maps already-resolved request fields to the HTTP body.
    /// Default: pass all resolved fields through unchanged.
    /// Override to rename, filter, or transform fields before they are serialized.
    /// </summary>
    public virtual Dictionary<string, object?> MapBody(Dictionary<string, object?> resolvedFields)
        => resolvedFields;

    private static readonly HashSet<string> _httpExclusions = ["PathParams", "Query", "Headers"];

    Task<TResponse> IHttpStep<TResponse>.RunAsync(
        string baseUrl,
        HttpWorkflowRequest<TResponse> request,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context)
    {
        var resolvedFields = FieldValueResolver.Resolve(request, context, _httpExclusions);
        var pathParams     = FieldValueResolver.ResolveGroup(request.PathParams, context);
        var queryOverrides = FieldValueResolver.ResolveGroup(request.Query, context);
        var requestHeaders = FieldValueResolver.ResolveGroup(request.Headers, context);
        return RunAsync(baseUrl, resolvedFields, pathParams, queryOverrides, targetHeaders, requestHeaders, context);
    }

    internal async Task<TResponse> RunAsync(
        string baseUrl,
        Dictionary<string, object?> resolvedFields,
        Dictionary<string, object?> pathParams,
        Dictionary<string, object?> requestQueryOverrides,
        Dictionary<string, object?> targetHeaders,
        Dictionary<string, object?> requestHeaders,
        WorkflowContext context)
    {
        var query = FieldValueResolver.ResolveGroup(Query, context);
        foreach (var kv in requestQueryOverrides) query[kv.Key] = kv.Value;

        var headers = new Dictionary<string, object?>(targetHeaders);
        foreach (var kv in FieldValueResolver.ResolveGroup(Headers, context))
            headers[kv.Key] = kv.Value;
        foreach (var kv in requestHeaders) headers[kv.Key] = kv.Value;

        var body = MapBody(resolvedFields);
        var json = await HttpExecutor.SendAsync(baseUrl, Method, Path, pathParams, query, body, headers);
        return HttpExecutor.Deserialize<TResponse>(json);
    }
}
