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

// --- UpdateUserAddress ---

public record AddressRegionResponse(string State, string Country);
public record AddressInfoResponse(string Street, string City, AddressRegionResponse Region);
public record PrimaryContactResponse(AddressInfoResponse Address);
public record ContactInfoResponse(PrimaryContactResponse Primary);
public record UpdateUserAddressResponse(string UserId, ContactInfoResponse Contact);

public record RegionFields
{
    public IFieldValue<string> State   { get; init; } = Static("IL");
    public IFieldValue<string> Country { get; init; } = Static("US");
}

public record AddressFields
{
    public IFieldValue<string>       Street { get; init; } = Static("123 Main St");
    public IFieldValue<string>       City   { get; init; } = Static("Springfield");
    public IFieldValue<RegionFields> Region { get; init; } = Static(new RegionFields());
}

public record PrimaryFields
{
    public IFieldValue<AddressFields> Address { get; init; } = Static(new AddressFields());
}

public record ContactFields
{
    public IFieldValue<PrimaryFields> Primary { get; init; } = Static(new PrimaryFields());
}

public record UpdateUserAddressRequest() : WorkflowRequest<UpdateUserAddressResponse>("updateUserAddress", "sample-api")
{
    public override IReadOnlyDictionary<string, IFieldValue<string>> PathParams { get; init; } = new Dictionary<string, IFieldValue<string>>
    {
        ["userId"] = From(ctx => ctx.Get<UserResponse>("createUser").Id)
    };
    public IFieldValue<ContactFields> Contact { get; init; } = Static(new ContactFields());
}

public class UpdateUserAddressStep : HttpStep<UpdateUserAddressRequest, UpdateUserAddressResponse>
{
    public override HttpMethod Method => HttpMethod.Put;
    public override string Path => "/users/{userId}/address";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
}

// --- GetUsersByRole ---

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
