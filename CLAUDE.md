# StepWise — Claude Guidance

StepWise is a C# workflow testing library for APIs. It lets you write integration tests that express multi-step API workflows — login, create a user, place an order — with minimal noise. Each test only specifies what matters; everything else flows through sensible defaults.

---

## Running tests

Always use `./test.sh`. It starts the sample API, runs all test projects, and tears the API down. Do not run `dotnet test` directly — integration tests depend on the API being up.

---

## Architecture

```
StepWise.Core
├── WorkflowRequest<TResponse>     — base record for all requests
├── BuildableRequest               — base record for array item builders
├── WorkflowContext                — holds targets, captures, accumulations
├── IFieldValue<T>                 — interface for resolvable field values
├── FieldValues                    — Static(), Generated(), From() factories
└── FieldValueResolver             — reflection-based resolver

StepWise.Http
├── HttpTarget                     — discovers steps by reflection, caches them
├── HttpExecutor                   — shared HTTP send/deserialize logic
├── HttpStep<TRequest, TResponse>  — declares Method, Path, Auth
└── Auth/                          — NoAuth, BearerTokenAuth, ApiKeyAuth

StepWise.Json
├── JsonWorkflowRunner             — pure engine: step execution, path resolution, assertion evaluation
├── JsonWorkflowTestBase           — thin xUnit wrapper over the runner
├── WorkflowDefinition             — all JSON model types
├── WorkflowResult / StepResult    — execution results
└── JsonValueResolver              — FromJsonValue, JsonElementToObject, field value types
```

Sample structure:

```
samples/StepWise.SampleWorkflows/
├── Requests/
│   ├── Login.cs          — LoginResponse, LoginRequest, LoginStep
│   ├── User.cs           — UserResponse, CreateUserRequest, CreateUserStep
│   └── Order.cs          — OrderResponse, AddOrderItem,
│                            CreateOrderRequest, CreateOrderStep, GetOrderStep
├── StepWiseTestBase.cs
└── WorkflowTests/
    ├── OrderWorkflowTests.cs          — C# tests
    └── Json/
        ├── JsonOrderWorkflowTests.cs  — JSON tests (xUnit runner)
        ├── targets.json
        ├── Requests/
        │   ├── auth.requests.json
        │   └── order.requests.json
        └── *.workflow.json
```

---

## C# style

### Request file layout

Response type, request record, and step class live in one file per API concept:

```csharp
// Order.cs
public record OrderResponse(string Id, string UserId, List<OrderItemResponse> Items, string Status);

public record CreateOrderRequest() : WorkflowRequest<OrderResponse>("createOrder", "sample-api")
{
    public IFieldValue<string>                             UserId { get; init; } = From(ctx => ctx.Get<UserResponse>("createUser").Id);
    public IFieldValue<List<Dictionary<string, object?>>> Items  { get; init; } = From(ctx => ctx.GetAccumulated<AddOrderItem>());
}

public class CreateOrderStep : HttpStep<CreateOrderRequest, OrderResponse>
{
    public override HttpMethod    Method => HttpMethod.Post;
    public override string        Path   => "/orders";
    public override IAuthProvider Auth   => BearerTokenAuth.From(ctx => ctx.Get<LoginResponse>("login").Token);
}
```

### Test class

xUnit creates a new instance per class — each test gets a fresh `WorkflowContext` with no shared state:

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

## JSON style

### Workflow file

```json
{
  "name": "PlacedOrder_CanBeRetrieved",
  "steps": [
    { "step": "login" },
    { "step": "createUser" },
    { "build": "addOrderItem" },
    { "step": "createOrder" },
    { "step": "getOrder" }
  ],
  "assertions": [
    { "equal": ["createOrder.id", "getOrder.id"] },
    { "equal": ["getOrder.status", "pending"] }
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
    }
  }
}
```

### Field value types

```json
{ "static": "some value" }     // literal — any JSON primitive or array/object
{ "generated": "guid" }        // built-in generators: guid, email, int, decimal
{ "from": "login.token" }      // capture path — see Path reference syntax below
```

### Auth types

```json
{ "type": "none" }
{ "type": "bearer", "from": "login.token" }
{ "type": "bearer", "token": "static-token" }
{ "type": "apikey", "header": "X-Api-Key", "key": { "static": "my-key" } }
{ "type": "apikey", "queryParam": "api_key", "key": { "from": "login.apiKey" } }
```

### Targets file

```json
{ "sample-api": "http://localhost:4200" }
```

### `captureAs` — running the same step twice

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

Without `captureAs`, each step is captured under its definition name. Use `captureAs` when the same step appears more than once and you need to distinguish the results.

### xUnit runner

```csharp
public class JsonOrderWorkflowTests : JsonWorkflowTestBase
{
    protected override IReadOnlyList<string> RequestPaths =>
    [
        "Requests/auth.requests.json",
        "Requests/order.requests.json"
    ];

    protected override string TargetsPath => "WorkflowTests/Json/targets.json";

    [Fact]
    public Task PlacedOrder_CanBeRetrieved() =>
        RunWorkflowAsync("WorkflowTests/Json/retrieve-order.workflow.json");
}
```

`RequestPaths` are resolved relative to the project working directory.

---

## Path reference syntax

`From` references and assertion expressions use: `captureKey.property.nested[index].property`

- First segment is always the capture key (step name or `captureAs` value)
- Supports arbitrary nesting: `step.a.b.c`
- Supports array indexing: `step.items[0]`
- Supports combinations: `step.items[1].meta.label`

Assertion expressions containing `.` or `[` are resolved as paths. Bare strings with neither are treated as literals if not found as a capture key.

---

## Capture runtime model

| Source | Type stored in captures |
|--------|------------------------|
| HTTP step response | `Dictionary<string, JsonElement>` |
| Build step accumulation | `List<Dictionary<string, object?>>` |
| JSON array after `JsonElementToObject` | `List<object?>` |
| JSON object after `JsonElementToObject` | `Dictionary<string, object?>` |

**Only the response is captured from HTTP steps.** The request payload is resolved, sent, and discarded. If a downstream step needs a value that was in the request, the server must echo it back, or re-derive it via `static`/`generated`/`from`.

---

## Testing strategy

Prefer testing through the public surface:

- **Path resolution / `From` references** — construct a `Dictionary<string, object?>` captures dict and call `new FromJsonValue("path").Resolve(captures)`. No need for `InternalsVisibleTo`.
- **Assertions end-to-end** — use `JsonWorkflowRunner.RunAsync(workflow, stepDefs, targets)` with `Build` steps (no HTTP required). Check `WorkflowResult.Passed` and `AssertionErrors`.
- **Full JSON workflow tests** — create a `.workflow.json` file and add a `[Fact]` to `JsonOrderWorkflowTests` (or a new `JsonWorkflowTestBase` subclass). These hit the live API.

Only reach for lower-level testing if the above is genuinely insufficient.
