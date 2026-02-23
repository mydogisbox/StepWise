using StepWise.Core;
using static StepWise.Core.FieldValues;

namespace StepWise.SampleWorkflows.Requests;

// --- Responses ---

public record LoginResponse(string Token, string UserId);
public record UserResponse(string Id, string Email, string FirstName, string LastName, string Role);
public record OrderResponse(string Id, string UserId, string ProductName, int Quantity, decimal UnitPrice, string Status);

// --- Request Records ---

public record LoginRequest<TProtocol>() : WorkflowRequest<LoginResponse>("login", "sample-api")
{
    public IFieldValue<string> Username { get; init; } = Generated(() => $"user-{Guid.NewGuid():N}@test.com");
    public IFieldValue<string> Password { get; init; } = Static("Password123!");
}

public record CreateUserRequest<TProtocol>() : WorkflowRequest<UserResponse>("createUser", "sample-api")
{
    public IFieldValue<string> Email     { get; init; } = Generated(() => $"user-{Guid.NewGuid():N}@test.com");
    public IFieldValue<string> FirstName { get; init; } = Static("Test");
    public IFieldValue<string> LastName  { get; init; } = Static("User");
    public IFieldValue<string> Role      { get; init; } = Static("user");
}

public record CreateOrderRequest<TProtocol>() : WorkflowRequest<OrderResponse>("createOrder", "sample-api")
{
    public IFieldValue<string>  UserId      { get; init; } = From(ctx => ctx.Get<UserResponse>("createUser").Id);
    public IFieldValue<string>  ProductName { get; init; } = Static("Widget");
    public IFieldValue<int>     Quantity    { get; init; } = Static(1);
    public IFieldValue<decimal> UnitPrice   { get; init; } = Static(9.99m);
}

public record GetOrderRequest<TProtocol>() : WorkflowRequest<OrderResponse>("getOrder", "sample-api")
{
    public IFieldValue<string> OrderId { get; init; } = From(ctx => ctx.Get<OrderResponse>("createOrder").Id);
}
