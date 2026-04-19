# StepWise — Claude Guidance

StepWise is a C# workflow testing library for APIs. It lets you write integration tests that express multi-step API workflows — login, create a user, place an order — with minimal noise. Each test only specifies what matters; everything else flows through sensible defaults.

---

## Running tests

Always use `./test.sh`. It starts the sample API, runs all test projects, and tears the API down. Do not run `dotnet test` directly — integration tests depend on the API being up.

Run tests after every change to verify nothing is broken.

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
        │   ├── order.requests.json
        │   └── user.requests.json
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

### URL parameters and query parameters

Override `PathParams` for `{placeholder}` substitution and `Query` for query string parameters. Both are excluded from the request body:

```csharp
public record GetOrderRequest() : WorkflowRequest<OrderResponse>("getOrder", "sample-api")
{
    public override IReadOnlyDictionary<string, IFieldValue<string>> PathParams { get; init; } = new Dictionary<string, IFieldValue<string>>
    {
        ["orderId"] = From(ctx => ctx.Get<OrderResponse>("createOrder").Id)
    };
}

public record SearchOrdersRequest() : WorkflowRequest<List<OrderResponse>>("searchOrders", "sample-api")
{
    public override IReadOnlyDictionary<string, IFieldValue<string>> Query { get; init; } = new Dictionary<string, IFieldValue<string>>
    {
        ["status"] = Static("pending"),
        ["userId"] = From(ctx => ctx.Get<UserResponse>("createUser").Id)
    };
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

### URL parameters (`pathParams`)

Values to substitute into `{placeholder}` segments of the path. Never sent in the request body:

```json
"getOrder": {
  "target": "sample-api",
  "method": "GET",
  "path": "/orders/{orderId}",
  "auth": { "type": "bearer", "from": "login.token" },
  "pathParams": {
    "orderId": { "from": "createOrder.id" }
  }
}
```

Multiple path parameters are supported. The key must match the placeholder name exactly.

### Query parameters (`query`)

Key-value pairs appended to the URL as a query string. Resolved independently of the request body:

```json
"searchOrders": {
  "target": "sample-api",
  "method": "GET",
  "path": "/orders",
  "auth": { "type": "bearer", "from": "login.token" },
  "query": {
    "status": { "static": "pending" },
    "userId": { "from": "createUser.id" }
  }
}
```

Produces: `GET /orders?status=pending&userId=abc-123`

`query` can be combined with `pathParams` and `defaults` on the same step.

### Per-invocation overrides for `pathParams` and `query`

Workflow invocations can override `pathParams` and `query` on a per-call basis, independent of the step definition's defaults. Override values are merged over the step-level values (invocation wins for matching keys).

```json
{
  "steps": [
    { "step": "createOrder", "captureAs": "firstOrder" },
    { "step": "createOrder", "captureAs": "secondOrder" },
    { "step": "getOrder", "pathParams": { "orderId": { "from": "firstOrder.id" } }, "captureAs": "retrieved" }
  ],
  "assertions": [
    { "equal":    ["retrieved.id", "firstOrder.id"] },
    { "notEqual": ["retrieved.id", "secondOrder.id"] }
  ]
}
```

```json
{
  "steps": [
    { "step": "getUsers", "captureAs": "regularUsers" },
    { "step": "getUsers", "query": { "role": { "static": "admin" } }, "captureAs": "admins" }
  ]
}
```

`with` (body field overrides), `pathParams`, and `query` are all independent and can be combined on the same invocation.

### Assertion types

| Type | Example | Meaning |
|------|---------|---------|
| `equal` | `"equal": ["createOrder.status", "pending"]` | two expressions are equal |
| `notEqual` | `"notEqual": ["firstOrder.id", "secondOrder.id"]` | two expressions differ |
| `single` | `"single": "createOrder.items"` | collection has exactly 1 item |
| `empty` | `"empty": "createOrder.items"` | collection is empty |
| `notEmpty` | `"notEmpty": "createOrder.id"` | value is present / collection is non-empty |
| `count` | `"count": ["createOrder.items", "2"]` | collection has exactly N items |

### Field value types

```json
{ "static": "some value" }     // literal — any JSON primitive or array/object
{ "generated": "guid" }        // built-in generators: guid, email, int, decimal
{ "from": "login.token" }      // capture path — see Path reference syntax below
```

`from` accepts an optional `default` that is used when the referenced step has not run:

```json
{ "from": "createUser.id", "default": { "static": "guest" } }
```

- If `createUser` was never executed, the default resolves and is used.
- If `createUser` ran but `id` is missing or null, a `JsonWorkflowException` is thrown — the step ran, so a missing field is a bug, not an absent step.
- The `default` is itself a field value definition and supports `static`, `generated`, or `from`.

When `static` contains a JSON object, each property value is resolved recursively as a `FieldValueDefinition`. An object with a `static`, `from`, or `generated` key is treated as a field value definition; all other objects are structural nodes whose children are resolved the same way. This allows `from` and `generated` at any depth:

```json
"contact": { "static": {
  "primary": { "static": {
    "address": { "static": {
      "street": { "static": "123 Main St" },
      "city":   { "from": "user.city" }
    }}
  }}
}}
```

### Nested object defaults and partial overrides

Step definitions should include full default values for all fields, including nested objects. When a `with` block specifies a nested object, it is **deep-merged** with the default: override wins for matching keys at every level, and unspecified keys are filled from the default.

This means a workflow only needs to specify the properties that differ:

```json
// requests file — full defaults
"updateUserAddress": {
  "defaults": {
    "contact": { "static": {
      "primary": { "static": {
        "address": { "static": {
          "street":  { "static": "123 Main St" },
          "city":    { "static": "Springfield" },
          "region":  { "static": {
            "state":   { "static": "IL" },
            "country": { "static": "US" }
          }}
        }}
      }}
    }}
  }
}

// workflow file — override only what matters
{
  "step": "updateUserAddress",
  "with": {
    "contact": { "static": {
      "primary": { "static": {
        "address": { "static": {
          "city":   { "static": "Boston" },
          "region": { "static": {
            "state": { "static": "MA" }
          }}
        }}
      }}
    }}
  }
}
```

`street` and `country` are not specified in `with`, so they come from the step defaults.

### Limitations

- **Array merge** — deep merge only applies to objects. If both default and override resolve to an array, the override replaces the default entirely. To vary array contents across invocations, use `build` steps and reference the accumulation via `from`.

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

### `poll` — polling a step until a condition is met

Re-executes a step definition on an interval until an `until` assertion passes or the timeout is reached. Useful for async operations where the result isn't immediately available.

```json
{
  "steps": [
    { "step": "createOrder" },
    {
      "poll": "getOrder",
      "until": { "equal": ["getOrder.status", "shipped"] },
      "intervalMs": 500,
      "timeoutMs": 10000
    }
  ],
  "assertions": [
    { "equal": ["getOrder.status", "shipped"] }
  ]
}
```

- `poll` — the step definition name to re-execute (same as `step`)
- `until` — a single assertion evaluated after each attempt; polling stops when it passes
- `intervalMs` — delay between attempts (default: 500)
- `timeoutMs` — max wait before throwing (default: 10000)
- `captureAs` — works the same as on `step` invocations
- If `until` is omitted the step executes once and succeeds immediately

### `workflow` — nesting workflows

A step can embed another workflow by name. Its steps execute against the same captures and targets as the parent; its assertions are skipped. All captures produced by the nested workflow are available to subsequent parent steps.

```json
{
  "name": "PlaceOrder",
  "steps": [
    { "workflow": "SetupUser" },
    { "build": "addOrderItem" },
    { "step": "createOrder" }
  ],
  "assertions": [
    { "equal":    ["createOrder.status", "pending"] },
    { "notEmpty": "createUser.id" }
  ]
}
```

Named workflows are pre-loaded by the test class via `SharedWorkflowPaths`. The `workflow` value is matched against each file's `name` field:

```csharp
protected override IReadOnlyList<string> SharedWorkflowPaths =>
[
    "WorkflowTests/Json/setup-user.workflow.json"
];
```

If the value doesn't match any loaded name, it is treated as a file path (fallback). Nesting is recursive — a named workflow can itself contain `workflow` steps.

### `captureAs` — naming captures explicitly

`captureAs` works on both `step` and `build` invocations. Without it, captures are stored under the step's definition name.

Use `captureAs` on HTTP steps when the same step runs more than once and you need to distinguish the results:

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

Use `captureAs` on build steps to pin a specific item's fields for later reference, independent of the accumulation:

```json
{
  "steps": [
    { "build": "addOrderItem", "with": { "productName": { "static": "Widget A" } } },
    { "build": "addOrderItem", "with": { "productName": { "static": "Widget B" } }, "captureAs": "lastItem" }
  ],
  "assertions": [
    { "equal": ["addOrderItem.productName", "Widget A"] },
    { "equal": ["lastItem.productName",     "Widget B"] }
  ]
}
```

Without `captureAs`, each build overwrites the previous individual capture for that step name — so `addOrderItem` above holds "Widget A" because the second build used `captureAs` instead.

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
- Supports numeric index: `step.items[0]`
- Supports field lookup: `step.items[?id=abc-123]` — finds the first element where `id` equals `abc-123` (case-insensitive)
- Supports dynamic field lookup values: `step.items[?id=other.id]` — the lookup value is resolved as a capture path when it contains `.` or `[`
- Supports combinations: `step.items[?productName=Widget B].quantity`

Assertion expressions containing `.` or `[` are resolved as paths. Bare strings with neither are treated as literals if not found as a capture key.

---

## Capture runtime model

| Source | Key | Type stored in captures |
|--------|-----|------------------------|
| HTTP step response | step name or `captureAs` | `Dictionary<string, JsonElement>` |
| HTTP step request payload | `captureRequestAs` value | `Dictionary<string, object?>` |
| Build step — individual result | step name or `captureAs` | `Dictionary<string, object?>` |
| Build step — accumulation | `accumulateAs` value | `List<Dictionary<string, object?>>` |
| JSON array after `JsonElementToObject` | — | `List<object?>` |
| JSON object after `JsonElementToObject` | — | `Dictionary<string, object?>` |

Each build step writes to **two** capture keys: the accumulation list (always, under `accumulateAs`) and the individual result (under the step name or `captureAs`). When the same step name is built multiple times without `captureAs`, the individual result capture is overwritten each time — only the last one is accessible by step name.

By default, only the response is captured from HTTP steps. Use `captureRequestAs` to also capture the resolved request payload under a named key, making it available to subsequent steps and assertions:

```json
{ "step": "createUser", "captureRequestAs": "userRequest", "with": { "firstName": { "static": "Jane" } } }
```

After this step, `userRequest.firstName` resolves to `"Jane"` and the response is still captured under `createUser` as usual. The request capture type is `Dictionary<string, object?>`.

---

## Choosing a capture strategy

| Situation | What to use |
|-----------|-------------|
| Share common setup steps across multiple workflows | `{ "workflow": "SetupUser" }` |
| Wait for an async result to reach a desired state | `{ "poll": "getOrder", "until": { "equal": ["getOrder.status", "shipped"] } }` |
| Reference a response field from a step that runs once | Step name: `createUser.id` |
| Same HTTP step runs multiple times; need to tell results apart | `captureAs` on each invocation: `"firstOrder"`, `"secondOrder"` |
| Need a more readable alias for a response | `captureAs: "token"` instead of `login.token` |
| Server doesn't echo a request field back; need it downstream | `captureRequestAs: "userRequest"` then `userRequest.email` |
| Inspect a specific item in a built collection | Accumulation index: `orderItems[0].productName` |
| Reference the fields of the most recently built item | Build step name: `addOrderItem.productName` (overwritten each time the step builds without `captureAs`) |
| Pin a specific built item independently of the accumulation | `captureAs` on the build invocation: `"specialItem"` then `specialItem.productName` |

**Key rules:**
- `captureAs` and the step name are mutually exclusive for a given invocation — `captureAs` wins if set.
- `captureRequestAs` is additive — it doesn't replace the response capture; both are available.
- Build steps always write to the `accumulateAs` list regardless of `captureAs`.
- Without `captureAs`, repeated builds of the same step overwrite the individual result capture; the accumulation still grows.

---

## Testing strategy

Prefer testing through the public surface:

- **Path resolution / `From` references** — construct a `Dictionary<string, object?>` captures dict and call `new FromJsonValue("path").Resolve(captures)`. No need for `InternalsVisibleTo`.
- **Assertions end-to-end** — use `JsonWorkflowRunner.RunAsync(workflow, stepDefs, targets)` with `Build` steps (no HTTP required). Check `WorkflowResult.Passed` and `AssertionErrors`.
- **Full JSON workflow tests** — create a `.workflow.json` file and add a `[Fact]` to `JsonOrderWorkflowTests` (or a new `JsonWorkflowTestBase` subclass). These hit the live API.

Only reach for lower-level testing if the above is genuinely insufficient.
