using System.Reflection;
using Walkthrough.Core;

namespace Walkthrough.Http;

/// <summary>
/// An execution target that sends requests over HTTP.
/// Steps are registered explicitly via <see cref="Register{TRequest,TResponse}"/>.
/// </summary>
public class HttpTarget : ITarget
{
    private readonly string _baseUrl;
    private readonly IReadOnlyDictionary<string, IFieldValue<string>> _headers;
    private readonly IReadOnlyDictionary<Type, object> _steps;

    public HttpTarget(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _headers = new Dictionary<string, IFieldValue<string>>();
        _steps   = new Dictionary<Type, object>();
    }

    private HttpTarget(
        string baseUrl,
        IReadOnlyDictionary<string, IFieldValue<string>> headers,
        IReadOnlyDictionary<Type, object> steps)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _headers = headers;
        _steps   = steps;
    }

    /// <summary>Returns a new target that sends the given headers with every request.</summary>
    public HttpTarget WithHeaders(IReadOnlyDictionary<string, IFieldValue<string>> headers)
        => new(_baseUrl, headers, _steps);

    /// <summary>Returns a new target with the given step registered for its request type.</summary>
    public HttpTarget Register<TRequest, TResponse>(HttpStep<TRequest, TResponse> step)
        where TRequest : WorkflowRequest<TResponse>
    {
        var newSteps = new Dictionary<Type, object>(_steps) { [typeof(TRequest)] = step };
        return new(_baseUrl, _headers, newSteps);
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
        var step           = ResolveStep<TRequest, TResponse>(typeof(TRequest));
        var resolvedFields = FieldValueResolver.Resolve(request, context);
        var pathParams     = FieldValueResolver.ResolveGroup(request.PathParams, context);
        var queryOverrides = FieldValueResolver.ResolveGroup(request.Query, context);
        var targetHeaders  = FieldValueResolver.ResolveGroup(_headers, context);
        var requestHeaders = FieldValueResolver.ResolveGroup(request.Headers, context);

        return await step.RunAsync(
            _baseUrl, resolvedFields, pathParams, queryOverrides, targetHeaders, requestHeaders, context);
    }

    private HttpStep<TRequest, TResponse> ResolveStep<TRequest, TResponse>(Type requestType)
        where TRequest : WorkflowRequest<TResponse>
    {
        if (_steps.TryGetValue(requestType, out var step))
            return (HttpStep<TRequest, TResponse>)step;

        throw new HttpStepException(
            $"No step registered for {requestType.Name}. " +
            $"Call .Register(new YourStep()) on the HttpTarget.");
    }
}
