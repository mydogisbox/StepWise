using StepWise.Core;

namespace StepWise.Http.Auth;

/// <summary>
/// An auth provider that does nothing. Use for unauthenticated endpoints
/// such as login or public API endpoints.
/// </summary>
public sealed class NoAuth : IAuthProvider
{
    public static readonly NoAuth Instance = new();

    private NoAuth() { }

    public Task ApplyAsync(HttpRequestMessage request, WorkflowContext context) =>
        Task.CompletedTask;
}
