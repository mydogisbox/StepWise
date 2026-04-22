using StepWise.Core;
using StepWise.Http;
using static StepWise.Core.FieldValues;

namespace StepWise.SampleWorkflows;

public record UserResponse(string Id, string Email, string FirstName, string LastName, string Role);

public record CreateUserRequest() : WorkflowRequest<UserResponse>("createUser", "sample-api")
{
    public IFieldValue<string> Email     { get; init; } = Generated(() => $"user-{Guid.NewGuid():N}@test.com");
    public IFieldValue<string> FirstName { get; init; } = Static("Test");
    public IFieldValue<string> LastName  { get; init; } = Static("User");
    public IFieldValue<string> Role      { get; init; } = Static("user");
}

public class CreateUserStep : HttpStep<CreateUserRequest, UserResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/users";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
}

public record GetUsersByRoleRequest() : WorkflowRequest<List<UserResponse>>("getUsersByRole", "sample-api");

public class GetUsersByRoleStep : HttpStep<GetUsersByRoleRequest, List<UserResponse>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/users";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
    public override IReadOnlyDictionary<string, IFieldValue<string>> Query { get; } = new Dictionary<string, IFieldValue<string>>
    {
        ["role"] = Static("user")
    };
}
