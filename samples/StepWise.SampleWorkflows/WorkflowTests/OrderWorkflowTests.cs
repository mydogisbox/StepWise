using System.Reflection;
using StepWise.Http;
using StepWise.SampleWorkflows.Requests;
using StepWise.SampleWorkflows.Steps;
using static StepWise.Core.FieldValues;
using Xunit;

namespace StepWise.SampleWorkflows.WorkflowTests;

/// <summary>
/// Integration tests for the order placement workflow.
/// Requires StepWise.SampleApi to be running on http://localhost:5000.
/// Start it with: dotnet run --project samples/StepWise.SampleApi
/// </summary>
public class OrderWorkflowTests : StepWiseTestBase
{
    private const string SampleApiUrl = "http://localhost:5000";

    private static StepWise.Core.WorkflowContext HttpContext() =>
        NewContext(ctx => ctx.WithTarget("sample-api",
            new HttpTarget(SampleApiUrl, Assembly.GetExecutingAssembly())));

    [Fact]
    public async Task NewUser_CanPlaceOrder_StatusIsPending()
    {
        var context = HttpContext();

        await context.ExecuteAsync(new LoginRequest<HttpProtocol>());

        var user = await context.ExecuteAsync(
            new CreateUserRequest<HttpProtocol>() with { Email = Static("buyer@test.com") }
        );

        var order = await context.ExecuteAsync(new CreateOrderRequest<HttpProtocol>());

        Assert.Equal("pending", order.Status);
        Assert.Equal(user.Id, order.UserId);
    }

    [Fact]
    public async Task NewUser_CanPlaceOrder_WithSpecificProduct()
    {
        var context = HttpContext();

        await context.ExecuteAsync(new LoginRequest<HttpProtocol>());
        await context.ExecuteAsync(new CreateUserRequest<HttpProtocol>());

        var order = await context.ExecuteAsync(
            new CreateOrderRequest<HttpProtocol>() with
            {
                ProductName = Static("Deluxe Widget"),
                Quantity    = Static(3),
                UnitPrice   = Static(19.99m)
            }
        );

        Assert.Equal("pending", order.Status);
        Assert.Equal("Deluxe Widget", order.ProductName);
        Assert.Equal(3, order.Quantity);
    }

    [Fact]
    public async Task PlacedOrder_CanBeRetrieved()
    {
        var context = HttpContext();

        await context.ExecuteAsync(new LoginRequest<HttpProtocol>());
        await context.ExecuteAsync(new CreateUserRequest<HttpProtocol>());
        var created = await context.ExecuteAsync(new CreateOrderRequest<HttpProtocol>());

        var retrieved = await context.ExecuteAsync(
            new GetOrderRequest<HttpProtocol>() with { OrderId = Static(created.Id) }
        );

        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("pending", retrieved.Status);
    }
}
