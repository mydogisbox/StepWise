using StepWise.Core;

namespace StepWise.Http.Auth;

/// <summary>
/// Applies an API key as either a request header or a query parameter.
/// The key value can be static or resolved from the workflow context.
/// </summary>
public sealed class ApiKeyAuth : IAuthProvider
{
    private enum Placement { Header, QueryParam }

    private readonly Placement _placement;
    private readonly string _keyName;
    private readonly IFieldValue<string> _keyValue;

    private ApiKeyAuth(Placement placement, string keyName, IFieldValue<string> keyValue)
    {
        _placement = placement;
        _keyName = keyName;
        _keyValue = keyValue;
    }

    /// <summary>
    /// Sends the API key as a request header.
    /// Example: ApiKeyAuth.Header("X-Api-Key", Static("my-secret-key"))
    /// </summary>
    public static ApiKeyAuth Header(string headerName, IFieldValue<string> value) =>
        new(Placement.Header, headerName, value);

    /// <summary>
    /// Sends the API key as a query string parameter.
    /// Example: ApiKeyAuth.QueryParam("api_key", Static("my-secret-key"))
    /// </summary>
    public static ApiKeyAuth QueryParam(string paramName, IFieldValue<string> value) =>
        new(Placement.QueryParam, paramName, value);

    public Task ApplyAsync(HttpRequestMessage request, WorkflowContext context)
    {
        var value = _keyValue.Resolve(context);

        if (_placement == Placement.Header)
        {
            request.Headers.TryAddWithoutValidation(_keyName, value);
        }
        else
        {
            var uri = request.RequestUri!;
            var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
            request.RequestUri = new Uri($"{uri}{separator}{Uri.EscapeDataString(_keyName)}={Uri.EscapeDataString(value)}");
        }

        return Task.CompletedTask;
    }
}
