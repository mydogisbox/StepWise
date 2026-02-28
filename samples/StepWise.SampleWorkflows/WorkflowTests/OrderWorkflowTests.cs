using StepWise.SampleWorkflows.Requests;
using StepWise.SampleWorkflows.Steps;
using static StepWise.Core.FieldValues;
using Xunit;

namespace StepWise.SampleWorkflows.WorkflowTests;

public class NewUser_CanPlaceOrder_StatusIsPending : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest<HttpProtocol>());
        await ExecuteAsync(new CreateUserRequest<HttpProtocol>());

        await BuildAsync(new AddOrderItem());

        var order = await ExecuteAsync(new CreateOrderRequest<HttpProtocol>());

        Assert.Equal("pending", order.Status);
        Assert.Single(order.Items);
    }
}

public class NewUser_CanPlaceOrder_WithSpecificItems : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest<HttpProtocol>());
        await ExecuteAsync(new CreateUserRequest<HttpProtocol>());

        await BuildAsync(new AddOrderItem() with { ProductName = Static("Deluxe Widget"), Quantity = Static(3) });
        await BuildAsync(new AddOrderItem() with { ProductName = Static("Basic Widget") });

        var order = await ExecuteAsync(new CreateOrderRequest<HttpProtocol>());

        Assert.Equal("pending", order.Status);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal("Deluxe Widget", order.Items[0].ProductName);
        Assert.Equal("Basic Widget", order.Items[1].ProductName);
    }
}

public class PlacedOrder_CanBeRetrieved : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest<HttpProtocol>());
        await ExecuteAsync(new CreateUserRequest<HttpProtocol>());

        await BuildAsync(new AddOrderItem());

        var created = await ExecuteAsync(new CreateOrderRequest<HttpProtocol>());
        var retrieved = await ExecuteAsync(new GetOrderRequest<HttpProtocol>());

        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("pending", retrieved.Status);
    }
}
