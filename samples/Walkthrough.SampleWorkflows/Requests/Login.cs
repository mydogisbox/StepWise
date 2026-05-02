using Walkthrough.Core;
using Walkthrough.Http;
using static Walkthrough.Core.FieldValues;

namespace Walkthrough.SampleWorkflows;

public record LoginResponse(string Token, string UserId);

public record LoginRequest() : WorkflowRequest<LoginResponse>("login")
{
    public IFieldValue<string> Username { get; init; } = Generated(() => $"user-{Guid.NewGuid():N}@test.com");
    public IFieldValue<string> Password { get; init; } = Static("Password123!");
}

public class LoginStep : HttpStep<LoginRequest, LoginResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/auth/login";
}
