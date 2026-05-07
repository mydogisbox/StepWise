using Walkthrough.Core;
using Walkthrough.Http;
using static Walkthrough.Core.FieldValues;

namespace Walkthrough.SampleWorkflows;

public record OrderItemResponse(string ProductName, int Quantity, decimal UnitPrice);
public record OrderResponse(string Id, string UserId, List<OrderItemResponse> Items, string Status);

public record AddOrderItemResponse(string ProductName, int Quantity, decimal UnitPrice);

public record AddOrderItem() : BuildableRequest<AddOrderItemResponse>
{
    public IFieldValue<string>  ProductName { get; init; } = Static("Widget");
    public IFieldValue<int>     Quantity    { get; init; } = Static(1);
    public IFieldValue<decimal> UnitPrice   { get; init; } = Static(9.99m);
}

public record CreateOrderRequest() : HttpWorkflowRequest<OrderResponse>("createOrder")
{
    public IFieldValue<string>                            UserId { get; init; } = From(ctx => ctx.Get<UserResponse>("createUser").Id);
    public IFieldValue<List<Dictionary<string, object?>>> Items  { get; init; } = From(ctx => ctx.GetAccumulated<AddOrderItem>());
}

public class CreateOrderStep : HttpStep<CreateOrderRequest, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string     Path   => "/orders";
}

public record GetOrderRequest() : HttpWorkflowRequest<OrderResponse>("getOrder")
{
    public IFieldValue<string> OrderId { get; init; } = From(ctx => ctx.Get<OrderResponse>("createOrder").Id);
}

public class GetOrderStep : HttpStep<GetOrderRequest, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string     Path   => "/orders/{orderId}";
}

// Polymorphic order items — shared fields live on the base; each subtype adds its own unique field.
// All variants accumulate under OrderLineItem because AccumulationKey is overridden on the base.
// Use GetAccumulated<OrderLineItem>() to retrieve all variants in insertion order.
public abstract record OrderLineItem() : BuildableRequest<AddOrderItemResponse>
{
    public override Type AccumulationKey => typeof(OrderLineItem);
    public IFieldValue<string>  ProductName { get; init; } = Static("Item");
    public IFieldValue<int>     Quantity    { get; init; } = Static(1);
    public IFieldValue<decimal> UnitPrice   { get; init; } = Static(9.99m);
}

// ShippingAddress is only meaningful for physical goods — not present on DigitalItem.
public record PhysicalItem() : OrderLineItem
{
    public IFieldValue<string> ShippingAddress { get; init; } = Static("123 Main St");
}

// DownloadUrl is only meaningful for digital goods — not present on PhysicalItem.
public record DigitalItem() : OrderLineItem
{
    public IFieldValue<string> DownloadUrl { get; init; } = Static("https://example.com/ebook");
}
