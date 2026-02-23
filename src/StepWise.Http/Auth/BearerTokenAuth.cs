using System.Net.Http.Headers;
using StepWise.Core;

namespace StepWise.Http.Auth;

/// <summary>
/// Applies a Bearer token to the Authorization header.
/// The token can be a static value or resolved from a previous step's response.
/// </summary>
public sealed class BearerTokenAuth : IAuthProvider
{
    private readonly IFieldValue<string> _token;

    private BearerTokenAuth(IFieldValue<string> token) => _token = token;

    /// <summary>
    /// Creates a BearerTokenAuth with a hardcoded static token.
    /// </summary>
    public static BearerTokenAuth WithStaticToken(string token) =>
        new(FieldValues.Static(token));

    /// <summary>
    /// Creates a BearerTokenAuth that resolves the token from the workflow context.
    /// Use this to reference the token captured from a prior login step.
    /// Example: BearerTokenAuth.From(ctx => ctx.Get&lt;LoginResponse&gt;("login").Token)
    /// </summary>
    public static BearerTokenAuth From(Func<WorkflowContext, string> selector) =>
        new(FieldValues.From(selector));

    public Task ApplyAsync(HttpRequestMessage request, WorkflowContext context)
    {
        var token = _token.Resolve(context);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Task.CompletedTask;
    }
}
