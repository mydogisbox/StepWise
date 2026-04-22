using StepWise.Core;
using StepWise.Http;
using static StepWise.Core.FieldValues;

namespace StepWise.SampleWorkflows;

public record OrderItemResponse(string ProductName, int Quantity, decimal UnitPrice);
public record OrderResponse(string Id, string UserId, List<OrderItemResponse> Items, string Status);

public record AddOrderItemResponse(string ProductName, int Quantity, decimal UnitPrice);

public record AddOrderItem() : BuildableRequest<AddOrderItemResponse>
{
    public IFieldValue<string>  ProductName { get; init; } = Static("Widget");
    public IFieldValue<int>     Quantity    { get; init; } = Static(1);
    public IFieldValue<decimal> UnitPrice   { get; init; } = Static(9.99m);
}

public record CreateOrderRequest() : WorkflowRequest<OrderResponse>("createOrder", "sample-api")
{
    public IFieldValue<string>                            UserId { get; init; } = From(ctx => ctx.Get<UserResponse>("createUser").Id);
    public IFieldValue<List<Dictionary<string, object?>>> Items { get; init; } = From(ctx => ctx.GetAccumulated<AddOrderItem>());
}

public class CreateOrderStep : HttpStep<CreateOrderRequest, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/orders";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
}

public record GetOrderRequest() : WorkflowRequest<OrderResponse>("getOrder", "sample-api")
{
    public override IReadOnlyDictionary<string, IFieldValue<string>> PathParams { get; init; } = new Dictionary<string, IFieldValue<string>>
    {
        ["orderId"] = From(ctx => ctx.Get<OrderResponse>("createOrder").Id)
    };
}

public class GetOrderStep : HttpStep<GetOrderRequest, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/orders/{orderId}";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
}
