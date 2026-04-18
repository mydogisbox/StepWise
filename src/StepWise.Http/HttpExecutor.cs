using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StepWise.Core;

namespace StepWise.Http;

/// <summary>
/// Shared HTTP execution logic used by both HttpTarget (C# path)
/// and JsonWorkflowRunner (JSON path).
/// </summary>
public static class HttpExecutor
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HttpClient SharedClient =
        new(new HttpClientHandler { AllowAutoRedirect = false });

    /// <summary>
    /// Executes an HTTP request, returning the raw response body.
    /// </summary>
    /// <param name="baseUrl">Target base URL.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Path template; <c>{placeholder}</c> segments are substituted from <paramref name="pathParams"/>.</param>
    /// <param name="pathParams">Values substituted into <c>{placeholder}</c> segments of <paramref name="path"/>. Never sent in the body.</param>
    /// <param name="queryParams">Key-value pairs appended to the URL as a query string.</param>
    /// <param name="bodyFields">Fields serialized as the JSON request body (ignored for GET and DELETE).</param>
    /// <param name="applyAuth">Delegate that applies authentication headers to the request.</param>
    public static async Task<string> SendAsync(
        string baseUrl,
        HttpMethod method,
        string path,
        Dictionary<string, object?> pathParams,
        Dictionary<string, object?> queryParams,
        Dictionary<string, object?> bodyFields,
        Func<HttpRequestMessage, Task> applyAuth)
    {
        // Substitute path parameters
        foreach (var (key, value) in pathParams)
            path = path.Replace($"{{{key}}}", Uri.EscapeDataString(value?.ToString() ?? ""),
                StringComparison.OrdinalIgnoreCase);

        // Append query string
        if (queryParams.Count > 0)
        {
            var qs = string.Join("&", queryParams.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}"));
            path = path.TrimEnd('?', '&') + (path.Contains('?') ? "&" : "?") + qs;
        }

        var url = baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        var httpRequest = new HttpRequestMessage(method, url);

        if (method != HttpMethod.Get && method != HttpMethod.Delete && bodyFields.Count > 0)
        {
            var json = JsonSerializer.Serialize(bodyFields, JsonOptions);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        await applyAuth(httpRequest);

        var response = await SharedClient.SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var locationHint = response.Headers.Location is { } loc
                ? $" Redirect location: {loc}."
                : string.Empty;
            throw new HttpStepException(
                $"HTTP {method} {url} failed with {(int)response.StatusCode} ({response.StatusCode})." +
                $"{locationHint} Body: {body}");
        }

        return await response.Content.ReadAsStringAsync();
    }

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new HttpStepException($"Response deserialized to null for type '{typeof(T).Name}'.");

    public static object? DeserializeRaw(string json, Type type) =>
        JsonSerializer.Deserialize(json, type, JsonOptions);
}
