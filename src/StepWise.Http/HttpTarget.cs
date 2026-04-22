using System.Reflection;
using StepWise.Core;

namespace StepWise.Http;

/// <summary>
/// An execution target that sends requests over HTTP.
/// Discovers the matching HttpStep for each request type by scanning provided assemblies.
/// Step resolution is cached per request type.
/// </summary>
public class HttpTarget : ITarget
{
    private readonly string _baseUrl;
    private readonly IEnumerable<Assembly> _assemblies;
    private readonly Dictionary<Type, object> _stepCache = new();
    private readonly IReadOnlyDictionary<string, IFieldValue<string>> _headers;

    public HttpTarget(string baseUrl, params Assembly[] assemblies)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _headers = new Dictionary<string, IFieldValue<string>>();
        _assemblies = assemblies.Length > 0
            ? assemblies
            : [Assembly.GetCallingAssembly()];
    }

    private HttpTarget(
        string baseUrl,
        IReadOnlyDictionary<string, IFieldValue<string>> headers,
        IEnumerable<Assembly> assemblies)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _headers = headers;
        _assemblies = assemblies;
    }

    /// <summary>Returns a new target that sends the given headers with every request.</summary>
    public HttpTarget WithHeaders(IReadOnlyDictionary<string, IFieldValue<string>> headers)
        => new(_baseUrl, headers, _assemblies);

    public Task<TResponse> ExecuteAsync<TResponse>(
        WorkflowRequest<TResponse> request,
        WorkflowContext context)
    {
        var executeMethod = typeof(HttpTarget)
            .GetMethod(nameof(ExecuteTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(request.GetType(), typeof(TResponse));

        return (Task<TResponse>)executeMethod.Invoke(this, [request, context])!;
    }

    private async Task<TResponse> ExecuteTypedAsync<TRequest, TResponse>(
        TRequest request,
        WorkflowContext context)
        where TRequest : WorkflowRequest<TResponse>
    {
        var step = ResolveStep<TRequest, TResponse>(typeof(TRequest));
        var pathParams  = FieldValueResolver.ResolveGroup(request.PathParams, context);
        var queryParams = FieldValueResolver.ResolveGroup(step.Query, context);
        foreach (var kv in FieldValueResolver.ResolveGroup(request.Query, context))
            queryParams[kv.Key] = kv.Value;
        var bodyFields  = FieldValueResolver.Resolve(request, context);

        // Headers: target → step → request (each layer wins over the one before)
        var headers = FieldValueResolver.ResolveGroup(_headers, context);
        foreach (var kv in FieldValueResolver.ResolveGroup(step.Headers, context))
            headers[kv.Key] = kv.Value;
        foreach (var kv in FieldValueResolver.ResolveGroup(request.Headers, context))
            headers[kv.Key] = kv.Value;

        var responseJson = await HttpExecutor.SendAsync(
            _baseUrl,
            step.Method,
            step.Path,
            pathParams,
            queryParams,
            bodyFields,
            headers);

        return HttpExecutor.Deserialize<TResponse>(responseJson);
    }

    private HttpStep<TRequest, TResponse> ResolveStep<TRequest, TResponse>(Type requestType)
        where TRequest : WorkflowRequest<TResponse>
    {
        if (_stepCache.TryGetValue(requestType, out var cached))
            return (HttpStep<TRequest, TResponse>)cached;

        var stepType = _assemblies
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t =>
                !t.IsAbstract &&
                t.BaseType is { IsGenericType: true } bt &&
                bt.GetGenericTypeDefinition() == typeof(HttpStep<,>) &&
                bt.GetGenericArguments()[0] == requestType);

        if (stepType is null)
            throw new HttpStepException(
                $"No HttpStep<{requestType.Name}, {typeof(TResponse).Name}> found in the scanned assemblies. " +
                $"Define a class that extends HttpStep<{requestType.Name}, {typeof(TResponse).Name}>.");

        var step = (HttpStep<TRequest, TResponse>)Activator.CreateInstance(stepType)!;
        _stepCache[requestType] = step;
        return step;
    }
}
