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

    public HttpTarget(string baseUrl, params Assembly[] assemblies)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _assemblies = assemblies.Length > 0
            ? assemblies
            : [Assembly.GetCallingAssembly()];
    }

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

        var responseJson = await HttpExecutor.SendAsync(
            _baseUrl,
            step.Method,
            step.Path,
            pathParams,
            queryParams,
            bodyFields,
            req => step.Auth.ApplyAsync(req, context));

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
