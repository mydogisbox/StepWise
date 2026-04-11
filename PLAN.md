# StepWise — Project Plan

## What It Is

StepWise is a C# workflow testing library for APIs. It lets you write integration tests that express multi-step API workflows — login, create a user, place an order — with minimal noise. Each test only specifies what matters; everything else flows through sensible defaults.

---

## Core Design Principles

**Defaults flow through context, overrides are explicit.** Every request declares its own default field values. Tests override only the fields relevant to what's under test using `with` expressions. Fields that depend on prior step output use `From(ctx => ...)` to resolve automatically.

**Context is the coordinator.** `WorkflowContext` holds named targets and captures step responses. `ExecuteAsync` and `BuildAsync` are the only entry points. Tests never call `context.Get(...)` directly — that's internal plumbing for `From(...)` lookups.

**Step classes own the transport details.** `HttpStep<TRequest, TResponse>` subclasses declare `Method`, `Path`, and `Auth`. `HttpTarget` discovers and caches the right step for each request type by scanning the assembly.

**No registry. No step passed at call site.** The target finds the step. The test just calls `ExecuteAsync`.

**Requests and steps live in the same file.** Each file in `Requests/` contains the response type, request record, and step class for a single API concept. No hunting across files to understand a request.

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
├── HttpExecutor                   — shared HTTP send/deserialize logic
├── HttpStep<TRequest, TResponse>  — declares Method, Path, Auth
├── HttpStepException              — HTTP-specific errors
└── Auth/
    ├── IAuthProvider
    ├── NoAuth
    ├── BearerTokenAuth
    └── ApiKeyAuth

StepWise.Json
├── JsonWorkflowRunner             — pure engine, no xUnit dependency
├── JsonWorkflowTestBase           — thin xUnit wrapper over the runner
├── WorkflowDefinition             — JSON workflow model
├── WorkflowResult / StepResult    — execution results
├── JsonValueResolver              — resolves FieldValueDefinition at runtime
└── JsonWorkflowException          — descriptive errors
```

---

## File Structure (Sample)

```
samples/StepWise.SampleWorkflows/
├── Requests/
│   ├── Login.cs          — LoginResponse, LoginRequest, LoginStep
│   ├── User.cs           — UserResponse, CreateUserRequest, CreateUserStep
│   └── Order.cs          — OrderResponse, AddOrderItem,
│                            CreateOrderRequest, CreateOrderStep,
│                            GetOrderRequest, GetOrderStep
├── StepWiseTestBase.cs
└── WorkflowTests/
    ├── OrderWorkflowTests.cs          — C# tests
    └── Json/
        ├── JsonOrderWorkflowTests.cs  — JSON tests (xUnit runner)
        ├── targets.json
        ├── Requests/
        │   ├── auth.requests.json
        │   └── order.requests.json
        ├── place-order.workflow.json
        ├── place-order-specific-items.workflow.json
        ├── retrieve-order.workflow.json
        └── two-orders.workflow.json
```

---

## Key Patterns

### Request file (C#)

```csharp
// Order.cs — response, request, and step together
public record OrderResponse(string Id, string UserId, List<OrderItemResponse> Items, string Status);

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
```

### C# test

```csharp
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
```

### Building an array

```csharp
await BuildAsync(new AddOrderItem() with { ProductName = Static("Deluxe Widget"), Quantity = Static(3) });
await BuildAsync(new AddOrderItem() with { ProductName = Static("Basic Widget") });
var order = await ExecuteAsync(new CreateOrderRequest());
```

---

## JSON Workflows

JSON workflow files reference separate `.requests.json` files and a `targets.json`. The workflow itself contains only the step sequence and assertions — fully portable across environments.

### Workflow file

```json
{
  "name": "PlacedOrder_CanBeRetrieved",
  "targets": {
    "sample-api": { "baseUrl": "http://localhost:5000" }
  },
  "requests": [
    "Requests/auth.requests.json",
    "Requests/order.requests.json"
  ],
  "steps": [
    { "step": "login" },
    { "step": "createUser" },
    { "build": "addOrderItem" },
    { "step": "createOrder" },
    { "step": "getOrder" }
  ],
  "assertions": [
    { "equal": ["createOrder.id",    "getOrder.id"] },
    { "equal": ["getOrder.status",   "pending"] }
  ]
}
```

### Requests file

```json
{
  "steps": {
    "addOrderItem": {
      "accumulateAs": "orderItems",
      "defaults": {
        "productName": { "static": "Widget" },
        "quantity":    { "static": 1 },
        "unitPrice":   { "static": 9.99 }
      }
    },
    "createOrder": {
      "target": "sample-api",
      "method": "POST",
      "path": "/orders",
      "auth": { "type": "bearer", "from": "login.token" },
      "defaults": {
        "userId": { "from": "createUser.id" },
        "items":  { "from": "orderItems" }
      }
    },
    "getOrder": {
      "target": "sample-api",
      "method": "GET",
      "path": "/orders/{orderId}",
      "auth": { "type": "bearer", "from": "login.token" },
      "defaults": {
        "orderId": { "from": "createOrder.id" }
      }
    }
  }
}
```

### Field value types

```json
{ "static": "some value" }     // literal — any JSON primitive
{ "generated": "guid" }        // built-in: guid, email, int, decimal
{ "from": "login.token" }      // capture path: stepName.propertyName
```

### Auth types

```json
{ "type": "none" }
{ "type": "bearer", "from": "login.token" }
{ "type": "bearer", "token": "static-token" }
{ "type": "apikey", "header": "X-Api-Key", "key": { "static": "my-key" } }
{ "type": "apikey", "queryParam": "api_key", "key": { "from": "login.apiKey" } }
```

### captureAs — running the same step twice

Use `captureAs` when the same step appears more than once in a workflow and you need to distinguish the captures:

```json
{
  "steps": [
    { "step": "createOrder", "captureAs": "firstOrder" },
    { "step": "createOrder", "captureAs": "secondOrder" }
  ],
  "assertions": [
    { "notEqual": ["firstOrder.id", "secondOrder.id"] }
  ]
}
```

Without `captureAs`, each step is captured under its definition name (`createOrder`), which is the right default for most workflows.

### Targets file

```json
{
  "sample-api": "http://localhost:4200"
}
```

### xUnit runner

```csharp
public class JsonOrderWorkflowTests : JsonWorkflowTestBase
{
    protected override string TargetsPath => "WorkflowTests/Json/targets.json";

    [Fact]
    public Task PlacedOrder_CanBeRetrieved() =>
        RunWorkflowAsync("WorkflowTests/Json/retrieve-order.workflow.json");
}
```

### Standalone (future CLI)

```bash
stepwise run retrieve-order.workflow.json --targets targets.json
```

`JsonWorkflowRunner` is a pure engine with no xUnit or assembly-scanning dependencies. The same engine powers the CLI and API. `JsonWorkflowTestBase` is a thin xUnit wrapper that calls the runner and throws on assertion failures.

---

## Milestones

- ✅ **M1** — Core abstractions (`IFieldValue<T>`, `Static`, `Generated`, `From`, `WorkflowContext`, `WorkflowRequest`)
- ✅ **M2** — HTTP transport (`HttpTarget`, `HttpExecutor`, `HttpStep`, `NoAuth`, `BearerTokenAuth`, `ApiKeyAuth`)
- ✅ **M3** — Sample API + end-to-end integration tests passing
- ✅ **M4** — Path parameter substitution, `BuildableRequest`, `GetAccumulated`
- ✅ **M5** — JSON declarative workflows (`JsonWorkflowRunner`, `.requests.json`, `.workflow.json`, `targets.json`)
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
