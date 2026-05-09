using System.Text.RegularExpressions;
using Walkthrough.Core;

namespace Walkthrough.Http;

internal interface IHttpStep<TResponse>
{
    Task<TResponse> RunAsync(
        string baseUrl,
        HttpWorkflowRequest<TResponse> request,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context);

    Task<object> RunRawAsync(
        string baseUrl,
        HttpWorkflowRequest<TResponse> request,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context);
}

/// <summary>
/// Declares the HTTP execution details for a request type.
/// Subclasses define Method and Path. Override MapBody, MapQuery, and MapHeaders to
/// control how resolved request fields are routed to the body, query string, and headers.
/// Path parameters are extracted automatically by matching {placeholder} names to request field names.
/// </summary>
public abstract class HttpStep<TRequest, TResponse> : IHttpStep<TResponse>
    where TRequest : HttpWorkflowRequest<TResponse>
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{(\w+)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public abstract HttpMethod Method { get; }
    public abstract string Path { get; }

    /// <summary>
    /// Maps resolved request fields to the HTTP request body.
    /// Default: all fields except those consumed as path params ({placeholder} names).
    /// Override to rename, filter, or transform fields before serialization.
    /// </summary>
    public virtual Dictionary<string, object?> MapBody(Dictionary<string, object?> resolvedFields)
    {
        var pathParamNames = PlaceholderRegex.Matches(Path)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return resolvedFields
            .Where(kv => !pathParamNames.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Maps resolved request fields to URL query parameters.
    /// Default: no query parameters.
    /// </summary>
    public virtual Dictionary<string, string> MapQuery(Dictionary<string, object?> resolvedFields) => [];

    /// <summary>
    /// Maps resolved request fields to HTTP headers.
    /// Default: no step-level headers. Merged over target-level headers; step wins for matching keys.
    /// </summary>
    public virtual Dictionary<string, string> MapHeaders(Dictionary<string, object?> resolvedFields) => [];

    Task<TResponse> IHttpStep<TResponse>.RunAsync(
        string baseUrl,
        HttpWorkflowRequest<TResponse> request,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context)
    {
        var resolvedFields = FieldValueResolver.Resolve(request, context);
        return RunAsync(baseUrl, resolvedFields, targetHeaders, context);
    }

    Task<object> IHttpStep<TResponse>.RunRawAsync(
        string baseUrl,
        HttpWorkflowRequest<TResponse> request,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context)
    {
        var resolvedFields = FieldValueResolver.Resolve(request, context);
        return RunRawAsync(baseUrl, resolvedFields, targetHeaders, context);
    }

    internal async Task<TResponse> RunAsync(
        string baseUrl,
        Dictionary<string, object?> resolvedFields,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context)
    {
        var (pathParams, query, headers, body) = PrepareRequest(resolvedFields, targetHeaders);
        var json = await HttpExecutor.SendAsync(baseUrl, Method, Path, pathParams, query, body, headers);
        return HttpExecutor.Deserialize<TResponse>(json);
    }

    internal async Task<object> RunRawAsync(
        string baseUrl,
        Dictionary<string, object?> resolvedFields,
        Dictionary<string, object?> targetHeaders,
        WorkflowContext context)
    {
        var (pathParams, query, headers, body) = PrepareRequest(resolvedFields, targetHeaders);
        var (_, json) = await HttpExecutor.SendRawAsync(baseUrl, Method, Path, pathParams, query, body, headers);
        return HttpExecutor.Deserialize<TResponse>(json)!;
    }

    private (Dictionary<string, object?> pathParams,
             Dictionary<string, object?> query,
             Dictionary<string, object?> headers,
             Dictionary<string, object?> body) PrepareRequest(
        Dictionary<string, object?> resolvedFields,
        Dictionary<string, object?> targetHeaders)
    {
        // Auto-extract path params by matching {placeholder} names to resolved field names
        var pathParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PlaceholderRegex.Matches(Path))
        {
            var name = match.Groups[1].Value;
            var field = resolvedFields.Keys.FirstOrDefault(k =>
                string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
                pathParams[name] = resolvedFields[field];
        }

        var query = MapQuery(resolvedFields)
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        // Merge: target headers first, then step headers (step wins for matching keys)
        var headers = new Dictionary<string, object?>(targetHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in MapHeaders(resolvedFields))
            headers[kv.Key] = kv.Value;

        var body = MapBody(resolvedFields);
        return (pathParams, query, headers, body);
    }

}
