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
        var retrieved = await ExecuteAsync(new GetOrderRequest() with { OrderId = Static(first.Id) });

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

        var users  = await ExecuteAsync(new GetUsersByRoleRequest());
        var admins = await ExecuteAsync(new GetUsersByRoleRequest() with { Role = Static("admin") });

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

public class TargetAuthHeader_ReceivedByServer : WalkthroughTestBase
{
    [Fact]
    public async Task Test()
    {
        await ExecuteAsync(new LoginRequest());
        var echo = await ExecuteAsync(new EchoHeadersRequest());

        Assert.StartsWith("Bearer ", echo["authorization"]);
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

// Demonstrates overriding MapBody to explicitly control which fields are sent in the HTTP body.
// Useful when field names need transforming, or only a subset should be sent.
public class MapBody_ExplicitFieldMapping_WorksCorrectly
{
    private const string SampleApiUrl = "http://localhost:4200";

    private class ExplicitCreateUserStep : HttpStep<CreateUserRequest, UserResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string     Path   => "/users";

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
        var context = new WorkflowContext();

        var loginTarget = new HttpTarget(SampleApiUrl).Register(new LoginStep());
        var apiTarget   = new HttpTarget(SampleApiUrl)
            .Register(new ExplicitCreateUserStep())
            .WithHeaders(new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
            });

        var runner = new WorkflowRunner(context,
            stepName => stepName == "login" ? (ITarget)loginTarget : apiTarget);

        await runner.ExecuteAsync(new LoginRequest());
        var user = await runner.ExecuteAsync(new CreateUserRequest());

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
            .Register(new CreateOrderStep())
            .WithHeaders(new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
            });

        var runner = new WorkflowRunner(
            new WorkflowContext(),
            stepName => stepName == "login"
                ? (ITarget)new DirectLoginTarget(SampleApiUrl)
                : httpTarget);

        await runner.ExecuteAsync(new LoginRequest());
        await runner.ExecuteAsync(new CreateUserRequest());
        await runner.BuildAsync(new AddOrderItem());
        var order = await runner.ExecuteAsync(new CreateOrderRequest());

        Assert.Equal("pending", order.Status);
        Assert.Single(order.Items);
    }
}

// Demonstrates three targets routed by step name:
//   authTarget  — HttpTarget for login
//   apiTarget   — HttpTarget for create steps, auth via WithHeaders
//   directTarget — plain ITarget wrapping a raw HttpClient call for getOrder
// Captures flow freely across all three: the token from authTarget is read by
// directTarget, and the order id from apiTarget is resolved into the GET URL.
public class ThreeTargets_HttpAndDirectMixed
{
    private const string SampleApiUrl = "http://localhost:4200";

    private class DirectGetOrderTarget(string baseUrl) : ITarget
    {
        private static readonly HttpClient _http = new();
        private static readonly JsonSerializerOptions _readOptions =
            new() { PropertyNameCaseInsensitive = true };

        public async Task<TResponse> ExecuteAsync<TResponse>(
            WorkflowRequest<TResponse> request, WorkflowContext context)
        {
            var fields  = FieldValueResolver.Resolve(request, context);
            var orderId = fields["OrderId"]?.ToString();
            var token   = context.Get<LoginResponse>("login").Token;

            var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/orders/{orderId}");
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

            var response = await _http.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TResponse>(json, _readOptions)!;
        }
    }

    [Fact]
    public async Task Test()
    {
        var authTarget   = new HttpTarget(SampleApiUrl).Register(new LoginStep());
        var apiTarget    = new HttpTarget(SampleApiUrl)
            .Register(new CreateUserStep())
            .Register(new CreateOrderStep())
            .WithHeaders(new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
            });
        var directTarget = new DirectGetOrderTarget(SampleApiUrl);

        var runner = new WorkflowRunner(
            new WorkflowContext(),
            stepName => stepName switch
            {
                "login"    => (ITarget)authTarget,
                "getOrder" => directTarget,
                _          => apiTarget
            });

        await runner.ExecuteAsync(new LoginRequest());
        await runner.ExecuteAsync(new CreateUserRequest());
        await runner.BuildAsync(new AddOrderItem());
        var created   = await runner.ExecuteAsync(new CreateOrderRequest());
        var retrieved = await runner.ExecuteAsync(new GetOrderRequest());

        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("pending",  retrieved.Status);
    }
}

// Demonstrates that a workflow function is target-agnostic.
// PlaceOrder takes only a resolver — the runner is constructed inside.
// The same function runs unchanged against two different target configurations:
// one using registered HttpStep classes, one using a raw HttpClient for login.
public class SameWorkflow_DifferentTargetImplementations
{
    private const string SampleApiUrl = "http://localhost:4200";

    private static async Task<OrderResponse> PlaceOrder(Func<string, ITarget> resolver)
    {
        var runner = new WorkflowRunner(new WorkflowContext(), resolver);
        await runner.ExecuteAsync(new LoginRequest());
        await runner.ExecuteAsync(new CreateUserRequest());
        await runner.BuildAsync(new AddOrderItem());
        return await runner.ExecuteAsync(new CreateOrderRequest());
    }

    // Login via a raw HttpClient call; remaining steps via registered HttpStep classes.
    private class DirectLoginTarget(string baseUrl) : ITarget
    {
        private static readonly HttpClient _http = new();

        public async Task<TResponse> ExecuteAsync<TResponse>(
            WorkflowRequest<TResponse> request, WorkflowContext context)
        {
            var fields  = FieldValueResolver.Resolve(request, context);
            var content = new StringContent(
                JsonSerializer.Serialize(fields, HttpExecutor.JsonOptions), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{baseUrl}/auth/login", content);
            response.EnsureSuccessStatusCode();
            return HttpExecutor.Deserialize<TResponse>(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task ViaRegisteredSteps()
    {
        var authTarget = new HttpTarget(SampleApiUrl).Register(new LoginStep());
        var apiTarget  = new HttpTarget(SampleApiUrl)
            .Register(new CreateUserStep())
            .Register(new CreateOrderStep())
            .WithHeaders(new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
            });

        var order = await PlaceOrder(
            stepName => stepName == "login" ? (ITarget)authTarget : apiTarget);

        Assert.Equal("pending", order.Status);
        Assert.Single(order.Items);
    }

    [Fact]
    public async Task ViaCustomLoginTarget()
    {
        var apiTarget = new HttpTarget(SampleApiUrl)
            .Register(new CreateUserStep())
            .Register(new CreateOrderStep())
            .WithHeaders(new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
            });

        var order = await PlaceOrder(
            stepName => stepName == "login"
                ? (ITarget)new DirectLoginTarget(SampleApiUrl)
                : apiTarget);

        Assert.Equal("pending", order.Status);
        Assert.Single(order.Items);
    }
}
