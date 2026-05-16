using Walkthrough.Core;

namespace Walkthrough.Http;

/// <summary>
/// An execution target that sends requests over HTTP.
/// Register request types with their steps via Register&lt;TStep&gt;() before use.
/// </summary>
public class HttpTarget : Target<HttpTarget, HttpStep>, ITarget, IRawTarget
{
    private readonly string _baseUrl;
    private IReadOnlyDictionary<string, IFieldValue<string>> _headers = new Dictionary<string, IFieldValue<string>>();

    public HttpTarget(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>Sets the headers sent with every request.</summary>
    public HttpTarget WithHeaders(IReadOnlyDictionary<string, IFieldValue<string>> headers)
    {
        _headers = headers;
        return this;
    }

    /// <summary>Registers a step to handle requests of type TConcreteStep.</summary>
    public HttpTarget Register<TConcreteStep>()
        where TConcreteStep : HttpStep, IHttpStep, new()
    {
        return Register(new TConcreteStep());
    }

    Task<TResponse> ITarget.ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request, Dictionary<string, object?> resolvedFields, WorkflowContext context)
    {
        var step          = GetStep(request);
        var targetHeaders = FieldValueResolver.ResolveGroup(_headers, context);
        return ((IHttpStep<TResponse>)step).RunAsync(_baseUrl, resolvedFields, targetHeaders);
    }

    Task<object> IRawTarget.ExecuteRawAsync<TResponse>(WorkflowRequest<TResponse> request, Dictionary<string, object?> resolvedFields, WorkflowContext context)
    {
        var step          = GetStep(request);
        var targetHeaders = FieldValueResolver.ResolveGroup(_headers, context);
        return ((IHttpStep<TResponse>)step).RunRawAsync(_baseUrl, resolvedFields, targetHeaders);
    }

    private HttpStep GetStep<TResponse>(WorkflowRequest<TResponse> request)
    {
        if (!_steps.TryGetValue(request.GetType(), out var step))
            throw new HttpStepException(
                $"No step registered for '{request.GetType().Name}'. " +
                $"Call .Register<YourStep>() on the HttpTarget.");
        return step;
    }
}
