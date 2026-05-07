using System.Text;
using System.Text.Json;
using Walkthrough.Core;
using Walkthrough.Http;
using Walkthrough.SampleWorkflows;
using static Walkthrough.Core.FieldValues;
using Xunit;

namespace Walkthrough.SampleWorkflows.WorkflowTests;

public class NewUser_CanPlaceOrder_StatusIsPending : WalkthroughTestBase
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

public class NewUser_CanPlaceOrder_WithSpecificItems : WalkthroughTestBase
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

public class PlacedOrder_CanBeRetrieved : WalkthroughTestBase
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

public class GetOrder_UsesPathParam : WalkthroughTestBase
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

public class GetUsersByRole_UsesQueryParam : WalkthroughTestBase
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

public class StepHeaders_ReceivedByServer : WalkthroughTestBase
{
    [Fact]
    public async Task Test()
    {
        var echo = await ExecuteAsync(new EchoHeadersWithStepHeaderRequest());

        Assert.Equal("from-step", echo["x-step-header"]);
    }
}

public class InvocationHeaders_ReceivedByServer : WalkthroughTestBase
{
    [Fact]
    public async Task Test()
    {
        var echo = await ExecuteAsync(new EchoHeadersRequest(),
            headers: new() { ["x-invocation-header"] = "from-invocation" });

        Assert.Equal("from-invocation", echo["x-invocation-header"]);
    }
}

public class UpdateUserAddress_NestedFieldValuesResolvedRecursively : WalkthroughTestBase
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

public class BuildItem_ReturnsResolvedResponse : WalkthroughTestBase
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

public class FromHeader_ConstructsBearerToken_ReceivedByServer : WalkthroughTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        var echo = await ExecuteAsync(new EchoHeadersWithFromAuthRequest());

        Assert.StartsWith("Bearer ", echo["authorization"]);
    }
}

// Demonstrates overriding MapBody to explicitly control which fields are sent in the HTTP body.
// Useful when field names need transforming, or only a subset should be sent.
public class MapBody_ExplicitFieldMapping_WorksCorrectly
{
    private const string SampleApiUrl = "http://localhost:4200";

    private class ExplicitCreateUserStep : HttpStep<CreateUserRequest, UserResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string Path => "/users";
        public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
            new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
            };

        public override Dictionary<string, object?> MapBody(Dictionary<string, object?> resolvedFields) => new()
        {
            ["Email"]     = resolvedFields["Email"],
            ["FirstName"] = resolvedFields["FirstName"],
            ["LastName"]  = resolvedFields["LastName"],
            ["Role"]      = resolvedFields["Role"],
        };
    }

    [Fact]
    public async Task Test()
    {
        var target = new HttpTarget(SampleApiUrl)
            .Register(new LoginStep())
            .Register(new ExplicitCreateUserStep());
        var context = new WorkflowContext().WithTargetResolver(_ => target);

        await context.ExecuteAsync(new LoginRequest());
        var user = await context.ExecuteAsync(new CreateUserRequest());

        Assert.NotEmpty(user.Id);
        Assert.Equal("Test", user.FirstName);
    }
}

// Demonstrates AccumulationKey: PhysicalItem and DigitalItem have genuinely different fields
// (ShippingAddress vs DownloadUrl), so they use subtypes rather than static factory methods.
// Both accumulate under OrderLineItem because AccumulationKey is overridden on the base.
public class MixedItemTypes_AccumulateUnderBaseType : WalkthroughTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        await ExecuteAsync(new CreateUserRequest());

        await BuildAsync(new PhysicalItem() with { ProductName = Static("Physical Widget") });
        await BuildAsync(new DigitalItem()  with { ProductName = Static("Premium E-Book") });

        var order = await ExecuteAsync(new CreateOrderRequest() with
        {
            Items = From(ctx => ctx.GetAccumulated<OrderLineItem>())
        });

        Assert.Equal(2,                 order.Items.Count);
        Assert.Equal("Physical Widget", order.Items[0].ProductName);
        Assert.Equal("Premium E-Book",  order.Items[1].ProductName);
    }
}

// Demonstrates plugging a custom ITarget — a plain function wrapping an HttpClient call —
// into the context alongside a regular HttpTarget for the remaining steps.
public class Login_ViaCustomTarget_CanPlaceOrder
{
    private const string SampleApiUrl = "http://localhost:4200";

    private class DirectLoginTarget(string baseUrl) : ITarget
    {
        private static readonly HttpClient _http = new();
        private static readonly JsonSerializerOptions _readOptions = new() { PropertyNameCaseInsensitive = true };

        public async Task<TResponse> ExecuteAsync<TResponse>(
            WorkflowRequest<TResponse> request, WorkflowContext context)
        {
            var fields = FieldValueResolver.Resolve(request, context);
            var content = new StringContent(
                JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{baseUrl}/auth/login", content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TResponse>(json, _readOptions)!;
        }
    }

    [Fact]
    public async Task Test()
    {
        var httpTarget = new HttpTarget(SampleApiUrl)
            .Register(new CreateUserStep())
            .Register(new CreateOrderStep());

        var context = new WorkflowContext().WithTargetResolver(stepName =>
            stepName == "login"
                ? (ITarget)new DirectLoginTarget(SampleApiUrl)
                : httpTarget);

        await context.ExecuteAsync(new LoginRequest());
        await context.ExecuteAsync(new CreateUserRequest());
        await context.BuildAsync(new AddOrderItem());
        var order = await context.ExecuteAsync(new CreateOrderRequest());

        Assert.Equal("pending", order.Status);
        Assert.Single(order.Items);
    }
}
