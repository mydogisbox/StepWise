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

    private static async Task<HttpResponseMessage> SendCoreAsync(
        string baseUrl,
        HttpMethod method,
        string path,
        Dictionary<string, object?> pathParams,
        Dictionary<string, object?> queryParams,
        Dictionary<string, object?> bodyFields,
        Dictionary<string, object?> headers)
    {
        foreach (var (key, value) in pathParams)
            path = path.Replace($"{{{key}}}", Uri.EscapeDataString(value?.ToString() ?? ""),
                StringComparison.OrdinalIgnoreCase);

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

        foreach (var (key, value) in headers)
            httpRequest.Headers.TryAddWithoutValidation(key, value?.ToString() ?? "");

        return await SharedClient.SendAsync(httpRequest);
    }

    /// <summary>
    /// Executes an HTTP request, returning the raw response body.
    /// </summary>
    /// <param name="baseUrl">Target base URL.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Path template; <c>{placeholder}</c> segments are substituted from <paramref name="pathParams"/>.</param>
    /// <param name="pathParams">Values substituted into <c>{placeholder}</c> segments of <paramref name="path"/>. Never sent in the body.</param>
    /// <param name="queryParams">Key-value pairs appended to the URL as a query string.</param>
    /// <param name="bodyFields">Fields serialized as the JSON request body (ignored for GET and DELETE).</param>
    /// <param name="headers">HTTP headers sent with the request.</param>
    public static async Task<string> SendAsync(
        string baseUrl,
        HttpMethod method,
        string path,
        Dictionary<string, object?> pathParams,
        Dictionary<string, object?> queryParams,
        Dictionary<string, object?> bodyFields,
        Dictionary<string, object?> headers)
    {
        var response = await SendCoreAsync(baseUrl, method, path, pathParams, queryParams, bodyFields, headers);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var url = response.RequestMessage!.RequestUri;
            var locationHint = response.Headers.Location is { } loc
                ? $" Redirect location: {loc}."
                : string.Empty;
            throw new HttpStepException(
                $"HTTP {method} {url} failed with {(int)response.StatusCode} ({response.StatusCode})." +
                $"{locationHint} Body: {body}");
        }

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Executes an HTTP request, returning the status code and body regardless of success or failure.
    /// Never throws on non-2xx responses.
    /// </summary>
    public static async Task<(int StatusCode, string Body)> SendRawAsync(
        string baseUrl,
        HttpMethod method,
        string path,
        Dictionary<string, object?> pathParams,
        Dictionary<string, object?> queryParams,
        Dictionary<string, object?> bodyFields,
        Dictionary<string, object?> headers)
    {
        var response = await SendCoreAsync(baseUrl, method, path, pathParams, queryParams, bodyFields, headers);
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new HttpStepException($"Response deserialized to null for type '{typeof(T).Name}'.");

    public static object? DeserializeRaw(string json, Type type) =>
        JsonSerializer.Deserialize(json, type, JsonOptions);
}
