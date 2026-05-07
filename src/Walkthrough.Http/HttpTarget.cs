using Walkthrough.Core;

namespace Walkthrough.Http;

/// <summary>
/// An execution target that sends requests over HTTP.
/// Register request types with their steps via Register() before use.
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

    private HttpTarget(string baseUrl,
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

    /// <summary>Registers a step to handle requests of type TRequest.</summary>
    public HttpTarget Register<TRequest, TResponse>(HttpStep<TRequest, TResponse> step)
        where TRequest : HttpWorkflowRequest<TResponse>
    {
        var newSteps = new Dictionary<Type, object>(_steps) { [typeof(TRequest)] = step };
        return new(_baseUrl, _headers, newSteps);
    }

    Task<TResponse> ITarget.ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request, WorkflowContext context)
    {
        if (request is not HttpWorkflowRequest<TResponse> httpRequest)
            throw new HttpStepException(
                $"HttpTarget requires an HttpWorkflowRequest. Got '{request.GetType().Name}'.");

        if (!_steps.TryGetValue(httpRequest.GetType(), out var step))
            throw new HttpStepException(
                $"No step registered for '{httpRequest.GetType().Name}'. " +
                $"Call .Register(new YourStep()) on the HttpTarget.");

        var targetHeaders = FieldValueResolver.ResolveGroup(_headers, context);
        return ((IHttpStep<TResponse>)step).RunAsync(_baseUrl, httpRequest, targetHeaders, context);
    }
}
