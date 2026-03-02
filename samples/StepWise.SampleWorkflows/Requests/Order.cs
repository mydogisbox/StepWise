using StepWise.Core;
using StepWise.Http;
using StepWise.Http.Auth;
using static StepWise.Core.FieldValues;

namespace StepWise.SampleWorkflows;

public record OrderItemResponse(string ProductName, int Quantity, decimal UnitPrice);
public record OrderResponse(string Id, string UserId, List<OrderItemResponse> Items, string Status);

public record AddOrderItem() : BuildableRequest
{
    public IFieldValue<string>  ProductName { get; init; } = Static("Widget");
    public IFieldValue<int>     Quantity    { get; init; } = Static(1);
    public IFieldValue<decimal> UnitPrice   { get; init; } = Static(9.99m);
}

public record CreateOrderRequest() : WorkflowRequest<OrderResponse>("createOrder", "sample-api")
{
    public IFieldValue<string>                            UserId { get; init; } = From(ctx => ctx.Get<UserResponse>("createUser").Id);
    public IFieldValue<List<Dictionary<string, object?>>> Items  { get; init; } = From(ctx => ctx.GetAccumulated<AddOrderItem>());
}

public class CreateOrderStep : HttpStep<CreateOrderRequest, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/orders";
    public override IAuthProvider Auth => BearerTokenAuth.From(
        ctx => ctx.Get<LoginResponse>("login").Token
    );
}

public record GetOrderRequest() : WorkflowRequest<OrderResponse>("getOrder", "sample-api")
{
    public IFieldValue<string> OrderId { get; init; } = From(ctx => ctx.Get<OrderResponse>("createOrder").Id);
}

public class GetOrderStep : HttpStep<GetOrderRequest, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/orders/{orderId}";
    public override IAuthProvider Auth => BearerTokenAuth.From(
        ctx => ctx.Get<LoginResponse>("login").Token
    );
}
