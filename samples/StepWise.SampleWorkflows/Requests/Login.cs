using StepWise.Core;
using StepWise.Http;
using static StepWise.Core.FieldValues;

namespace StepWise.SampleWorkflows;

public record LoginResponse(string Token, string UserId);

public record LoginRequest() : WorkflowRequest<LoginResponse>("login", "sample-api")
{
    public IFieldValue<string> Username { get; init; } = Generated(() => $"user-{Guid.NewGuid():N}@test.com");
    public IFieldValue<string> Password { get; init; } = Static("Password123!");
}

public class LoginStep : HttpStep<LoginRequest, LoginResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/auth/login";
}
