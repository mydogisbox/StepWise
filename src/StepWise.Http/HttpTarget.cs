using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StepWise.Core;

namespace StepWise.Http;

/// <summary>
/// An execution target that sends requests over HTTP.
/// Discovers the matching HttpStep for each request type by scanning provided assemblies.
/// Step resolution is cached per request type.
/// </summary>
public class HttpTarget : ITarget
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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
        // Dispatch to the concrete typed implementation via reflection
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

        // Resolve all IFieldValue<T> fields
        var resolvedFields = FieldValueResolver.Resolve<TResponse>(request, context);

        // Substitute path parameters
        var path = step.Path;
        var pathParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in resolvedFields)
        {
            var placeholder = $"{{{key}}}";
            if (path.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Replace(placeholder,
                    Uri.EscapeDataString(value?.ToString() ?? ""),
                    StringComparison.OrdinalIgnoreCase);
                pathParamNames.Add(key);
            }
        }

        var url = _baseUrl + "/" + path.TrimStart('/');
        var httpRequest = new HttpRequestMessage(step.Method, url);

        // Build body excluding path params
        var bodyFields = resolvedFields
            .Where(kv => !pathParamNames.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (step.Method != HttpMethod.Get && step.Method != HttpMethod.Delete && bodyFields.Count > 0)
        {
            var json = JsonSerializer.Serialize(bodyFields, JsonOptions);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        // Apply auth
        await step.Auth.ApplyAsync(httpRequest, context);

        // Send
        using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var response = await httpClient.SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var locationHint = response.Headers.Location is { } loc
                ? $" Redirect location: {loc}."
                : string.Empty;
            throw new HttpStepException(
                $"Step '{request.StepName}' failed with status {(int)response.StatusCode} ({response.StatusCode}). " +
                $"URL: {url}.{locationHint} Body: {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions)
            ?? throw new HttpStepException($"Step '{request.StepName}' returned a null response body.");
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
