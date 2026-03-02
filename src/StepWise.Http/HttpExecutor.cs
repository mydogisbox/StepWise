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
    /// Executes an HTTP request given resolved fields, returning the raw response body.
    /// </summary>
    public static async Task<string> SendAsync(
        string baseUrl,
        HttpMethod method,
        string path,
        Dictionary<string, object?> resolvedFields,
        Func<HttpRequestMessage, Task> applyAuth)
    {
        // Substitute path parameters
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

        var url = baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        var httpRequest = new HttpRequestMessage(method, url);

        // Build body excluding path params
        var bodyFields = resolvedFields
            .Where(kv => !pathParamNames.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

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
