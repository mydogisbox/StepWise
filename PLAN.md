# StepWise — Project Plan

## What It Is

StepWise is a C# workflow testing library for APIs. It lets you write integration tests that express multi-step API workflows — login, create a user, place an order — with minimal noise. Each test only specifies what matters; everything else flows through sensible defaults.

## Core Design Principles

**Defaults flow through context, overrides are explicit.** Every request declares its own default field values. Tests override only the fields relevant to what's under test using `with` expressions. Fields that depend on prior step output use `From(ctx => ...)` to resolve automatically.

**Context is the coordinator.** `WorkflowContext` holds named targets and captures step responses. `ExecuteAsync` and `BuildAsync` are the only entry points. Tests never call `context.Get(...)` directly — that's internal plumbing for `From(...)` lookups.

**Protocol is a generic parameter on the request.** `LoginRequest<HttpProtocol>` declares which transport to use. Swapping to a different transport means changing the type parameter — the request data and defaults are unchanged.

**Step classes own the transport details.** `HttpStep<TRequest, TResponse>` subclasses declare `Method`, `Path`, and `Auth`. `HttpTarget` discovers and caches the right step for each request type by scanning the assembly.

**No registry. No step passed at call site.** The target finds the step. The test just calls `ExecuteAsync`.

**Buildable requests accumulate into context.** For requests that contain arrays, `BuildableRequest` subclasses are built up piece by piece via `context.BuildAsync(item)`. The accumulation is consumed and cleared when the parent request resolves it via `From(ctx => ctx.GetAccumulated<T>())`.

**One test class per test.** xUnit creates a new instance per class, so each test gets a fresh context. Tests run in parallel with no shared state.

---

## Architecture

```
StepWise.Core
├── WorkflowRequest<TResponse>     — base record for all requests
├── BuildableRequest               — base record for array item builders
├── WorkflowContext                — holds targets, captures, accumulations
├── ITarget                        — execution target interface
├── IFieldValue<T>                 — interface for resolvable field values
├── FieldValues                    — Static(), Generated(), From() factories
├── FieldValueResolver             — reflection-based resolver
└── WorkflowContextException       — descriptive errors

StepWise.Http
├── HttpTarget                     — ITarget impl, discovers steps by reflection
├── HttpStep<TRequest, TResponse>  — declares Method, Path, Auth
├── HttpStepException              — HTTP-specific errors
└── Auth/
    ├── IAuthProvider
    ├── NoAuth
    ├── BearerTokenAuth
    └── ApiKeyAuth
```

---

## Key Patterns

### Request with defaults

```csharp
public record CreateOrderRequest<TProtocol>() : WorkflowRequest<OrderResponse>("createOrder", "sample-api")
{
    public IFieldValue<string>  UserId { get; init; } = From(ctx => ctx.Get<UserResponse>("createUser").Id);
    public IFieldValue<List<Dictionary<string, object?>>> Items { get; init; } = From(ctx => ctx.GetAccumulated<AddOrderItem>());
}
```

### Step class

```csharp
public class CreateOrderStep : HttpStep<CreateOrderRequest<HttpProtocol>, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/orders";
    public override IAuthProvider Auth => BearerTokenAuth.From(
        ctx => ctx.Get<LoginResponse>("login").Token
    );
}
```

### Test

```csharp
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
```

### Building an array

```csharp
await BuildAsync(new AddOrderItem() with { ProductName = Static("Deluxe Widget"), Quantity = Static(3) });
await BuildAsync(new AddOrderItem() with { ProductName = Static("Basic Widget") });
var order = await ExecuteAsync(new CreateOrderRequest<HttpProtocol>());
```

---

## Milestones

- ✅ **M1** — Core abstractions (`IFieldValue<T>`, `Static`, `Generated`, `From`, `WorkflowContext`, `WorkflowRequest`)
- ✅ **M2** — HTTP transport (`HttpTarget`, `HttpStep`, `NoAuth`, `BearerTokenAuth`, `ApiKeyAuth`)
- ✅ **M3** — Sample API + end-to-end integration tests passing
- ✅ **M4** — Path parameter substitution, `BuildableRequest`, `GetAccumulated`
- ⬜ **M5** — JSON declarative workflows (optional)
- ⬜ **M6** — NuGet packaging

---

## Running Tests

```bash
# Start the sample API (Terminal 1)
dotnet run --project samples/StepWise.SampleApi

# Run tests (Terminal 2)
dotnet test

# Or use the script (handles API lifecycle automatically)
chmod +x test.sh
./test.sh
```
