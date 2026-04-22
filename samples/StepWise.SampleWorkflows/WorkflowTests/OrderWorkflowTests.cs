using StepWise.SampleWorkflows;
using static StepWise.Core.FieldValues;
using Xunit;

namespace StepWise.SampleWorkflows.WorkflowTests;

public class NewUser_CanPlaceOrder_StatusIsPending : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        await ExecuteAsync(new CreateUserRequest());

        await BuildAsync(new AddOrderItem());

        var order = await ExecuteAsync(new CreateOrderRequest());

        Assert.Equal("pending", order.Status);
        Assert.Single(order.Items);
    }
}

public class NewUser_CanPlaceOrder_WithSpecificItems : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        await ExecuteAsync(new CreateUserRequest());

        await BuildAsync(new AddOrderItem() with { ProductName = Static("Deluxe Widget"), Quantity = Static(3) });
        await BuildAsync(new AddOrderItem() with { ProductName = Static("Basic Widget") });

        var order = await ExecuteAsync(new CreateOrderRequest());

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
        await ExecuteAsync(new LoginRequest());
        await ExecuteAsync(new CreateUserRequest());

        await BuildAsync(new AddOrderItem());

        var created   = await ExecuteAsync(new CreateOrderRequest());
        var retrieved = await ExecuteAsync(new GetOrderRequest());

        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("pending", retrieved.Status);
    }
}

public class GetOrder_UsesPathParam : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        await ExecuteAsync(new CreateUserRequest());
        await BuildAsync(new AddOrderItem());
        var first  = await ExecuteAsync(new CreateOrderRequest());
        var second = await ExecuteAsync(new CreateOrderRequest());

        // Explicitly retrieve the first order by id to verify path param routing
        var retrieved = await ExecuteAsync(new GetOrderRequest(), pathParams: new() { ["orderId"] = first.Id });

        Assert.Equal(first.Id, retrieved.Id);
        Assert.NotEqual(second.Id, retrieved.Id);
    }
}

public class GetUsersByRole_UsesQueryParam : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        await ExecuteAsync(new CreateUserRequest() with { Role = Static("user") });
        await ExecuteAsync(new CreateUserRequest() with { Role = Static("admin") });

        // Uses step default: Query = { "role" = "user" }
        var users  = await ExecuteAsync(new GetUsersByRoleRequest());
        // Override per-call to get admins
        var admins = await ExecuteAsync(new GetUsersByRoleRequest(), query: new() { ["role"] = "admin" });

        Assert.All(users,  u => Assert.Equal("user",  u.Role));
        Assert.All(admins, u => Assert.Equal("admin", u.Role));
    }
}

public class StepHeaders_ReceivedByServer : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        var echo = await ExecuteAsync(new EchoHeadersWithStepHeaderRequest());

        Assert.Equal("from-step", echo["x-step-header"]);
    }
}

public class InvocationHeaders_ReceivedByServer : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        var echo = await ExecuteAsync(new EchoHeadersRequest(),
            headers: new() { ["x-invocation-header"] = "from-invocation" });

        Assert.Equal("from-invocation", echo["x-invocation-header"]);
    }
}

public class UpdateUserAddress_NestedFieldValuesResolvedRecursively : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        await ExecuteAsync(new CreateUserRequest());

        var result = await ExecuteAsync(
            new UpdateUserAddressRequest() with
            {
                Contact = Static(new ContactFields
                {
                    Primary = Static(new PrimaryFields
                    {
                        Address = Static(new AddressFields
                        {
                            City   = Static("Boston"),
                            Region = Static(new RegionFields { State = Static("MA") })
                        })
                    })
                })
            });

        Assert.Equal("Boston",      result.Contact.Primary.Address.City);
        Assert.Equal("MA",          result.Contact.Primary.Address.Region.State);
        Assert.Equal("123 Main St", result.Contact.Primary.Address.Street);   // default preserved
        Assert.Equal("US",          result.Contact.Primary.Address.Region.Country); // default preserved
    }
}

public class BuildItem_ReturnsResolvedResponse : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        var widget = await BuildAsync(new AddOrderItem() with { ProductName = Static("Deluxe Widget"), Quantity = Static(3) });

        Assert.Equal("Deluxe Widget", widget.ProductName);
        Assert.Equal(3, widget.Quantity);
        Assert.Equal(9.99m, widget.UnitPrice);
    }
}

public class FromHeader_ConstructsBearerToken_ReceivedByServer : StepWiseTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        var echo = await ExecuteAsync(new EchoHeadersWithFromAuthRequest());

        Assert.StartsWith("Bearer ", echo["authorization"]);
    }
}
