# Walkthrough — Claude Guidance

Walkthrough is a C# workflow testing library for APIs. It lets you write integration tests that express multi-step API workflows — login, create a user, place an order — with minimal noise. Each test only specifies what matters; everything else flows through sensible defaults.

---

## Running tests

Always use `./test.sh`. It starts the sample API, runs all test projects, and tears the API down. Do not run `dotnet test` directly — integration tests depend on the API being up.

Run tests after every change to verify nothing is broken.

---

## Architecture

```
Walkthrough.Core
├── WorkflowRequest<TResponse>     — base record for all requests
├── BuildableRequest               — non-generic marker base for array item builders
├── BuildableRequest<TResponse>    — generic base; TResponse is the resolved snapshot type returned by BuildAsync
├── WorkflowContext                — holds target resolver, captures, accumulations
├── IFieldValue<T>                 — interface for resolvable field values
├── FieldValues                    — Static(), Generated(), From() factories
└── FieldValueResolver             — reflection-based resolver

Walkthrough.Http
├── HttpTarget                     — sends requests over HTTP; steps registered explicitly via Register()
├── HttpExecutor                   — shared HTTP send/deserialize logic
└── HttpStep<TRequest, TResponse>  — declares Method, Path, Query, Headers for one request type

Walkthrough.Json
├── JsonWorkflowRunner             — pure engine: step execution, path resolution, assertion evaluation
├── JsonWorkflowTestBase           — thin xUnit wrapper over the runner
├── WorkflowDefinition             — all JSON model types
├── WorkflowResult / StepResult    — execution results
└── JsonValueResolver              — FromJsonValue, JsonElementToObject, field value types
```

Sample structure:

```
samples/Walkthrough.SampleWorkflows/
├── Requests/
│   ├── Login.cs          — LoginResponse, LoginRequest, LoginStep
│   ├── User.cs           — UserResponse, CreateUserRequest, CreateUserStep,
│   │                        UpdateUserAddressResponse (+ nested response types),
│   │                        ContactFields/PrimaryFields/AddressFields/RegionFields,
│   │                        UpdateUserAddressRequest, UpdateUserAddressStep,
│   │                        GetUsersByRoleRequest, GetUsersByRoleStep
│   └── Order.cs          — OrderResponse, AddOrderItemResponse, AddOrderItem,
│                            CreateOrderRequest, CreateOrderStep, GetOrderStep
├── WalkthroughTestBase.cs
└── WorkflowTests/
    ├── OrderWorkflowTests.cs          — C# tests
    └── Json/
        ├── JsonOrderWorkflowTests.cs  — JSON tests (xUnit runner)
        ├── sample-api.target.json     — base URL + per-step HTTP execution details
        ├── Contracts/
        │   ├── auth.contracts.json    — body defaults for auth steps
        │   ├── order.contracts.json   — body defaults for order steps
        │   └── user.contracts.json    — body defaults for user steps
        └── *.workflow.json
```

---

## C# style

### Request file layout

Response type, request record, and step class live in one file per API concept:

```csharp
// Order.cs
public record OrderResponse(string Id, string UserId, List<OrderItemResponse> Items, string Status);

public record AddOrderItemResponse(string ProductName, int Quantity, decimal UnitPrice);

public record AddOrderItem() : BuildableRequest<AddOrderItemResponse>
{
    public IFieldValue<string>  ProductName { get; init; } = Static("Widget");
    public IFieldValue<int>     Quantity    { get; init; } = Static(1);
    public IFieldValue<decimal> UnitPrice   { get; init; } = Static(9.99m);
}

public record CreateOrderRequest() : WorkflowRequest<OrderResponse>("createOrder")
{
    public IFieldValue<string>                            UserId { get; init; } = From(ctx => ctx.Get<UserResponse>("createUser").Id);
    public IFieldValue<List<Dictionary<string, object?>>> Items  { get; init; } = From(ctx => ctx.GetAccumulated<AddOrderItem>());
}

public class CreateOrderStep : HttpStep<CreateOrderRequest, OrderResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string     Path   => "/orders";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["Authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
}
```

### URL parameters and query parameters

Override `PathParams` for `{placeholder}` substitution and `Query` for query string parameters. Both are excluded from the request body:

```csharp
public record GetOrderRequest() : WorkflowRequest<OrderResponse>("getOrder")
{
    public override IReadOnlyDictionary<string, IFieldValue<string>> PathParams { get; init; } = new Dictionary<string, IFieldValue<string>>
    {
        ["orderId"] = From(ctx => ctx.Get<OrderResponse>("createOrder").Id)
    };
}

public record SearchOrdersRequest() : WorkflowRequest<List<OrderResponse>>("searchOrders")
{
    public override IReadOnlyDictionary<string, IFieldValue<string>> Query { get; init; } = new Dictionary<string, IFieldValue<string>>
    {
        ["status"] = Static("pending"),
        ["userId"] = From(ctx => ctx.Get<UserResponse>("createUser").Id)
    };
}
```

### Per-invocation overrides in C#

`PathParams`, `Query`, and `Headers` can all be overridden per-call using the `with` expression, or via convenience parameters on `WalkthroughTestBase.ExecuteAsync` (which accept `Dictionary<string, string>` and wrap values in `Static`):

```csharp
// Convenience parameters — static strings only
var admins = await ExecuteAsync(new GetUsersByRoleRequest(), query: new() { ["role"] = "admin" });
var order  = await ExecuteAsync(new GetOrderRequest(), pathParams: new() { ["orderId"] = firstId });
var echo   = await ExecuteAsync(new EchoRequest(), headers: new() { ["X-Request-Id"] = "abc" });

// with expression — full IFieldValue<string> support
var retrieved = await ExecuteAsync(new GetOrderRequest() with
{
    PathParams = new Dictionary<string, IFieldValue<string>>
    {
        ["orderId"] = From(ctx => ctx.Get<OrderResponse>("firstOrder").Id)
    }
});
```

All three can be combined on the same invocation. Override values are merged over the step/request defaults — invocation wins for matching keys.

### Test class

xUnit creates a new instance per class — each test gets a fresh `WorkflowContext` with no shared state:

```csharp
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
```

### Custom targets and multi-target routing

`WithTargetResolver(Func<string, ITarget>)` configures how `WorkflowContext` dispatches each step. The resolver receives the step name and returns any `ITarget` — `HttpTarget`, or a custom class implementing `ITarget`.

Use this to route different steps to different services, or to swap in a hand-rolled implementation for a specific step:

```csharp
// Route "login" to a custom HttpClient wrapper; all other steps go to HttpTarget
var httpTarget = new HttpTarget(SampleApiUrl)
    .Register(new CreateUserStep())
    .Register(new CreateOrderStep());

var context = new WorkflowContext()
    .WithTargetResolver(stepName => stepName == "login"
        ? (ITarget)new DirectLoginTarget(SampleApiUrl)
        : httpTarget);
```

Any class that implements `ITarget` is a valid target:

```csharp
private class DirectLoginTarget(string baseUrl) : ITarget
{
    private static readonly HttpClient _http = new();

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
```

All captures are shared through the same `WorkflowContext` regardless of which target produced them. A `From` lambda on a request for target B can freely reference captures produced by target A.

### Field value resolution

`FieldValueResolver` resolves `IFieldValue<T>` properties on any request or build item. Resolution is recursive: after resolving `IFieldValue<T>` → `T`, if `T` is itself a record with `IFieldValue<U>` properties, those are resolved too — producing a nested `Dictionary<string, object?>`. This continues to arbitrary depth. List elements are also recursed into.

This means nested records with `IFieldValue<T>` fields work naturally in requests:

```csharp
public record RegionFields
{
    public IFieldValue<string> State   { get; init; } = Static("IL");
    public IFieldValue<string> Country { get; init; } = Static("US");
}

public record AddressFields
{
    public IFieldValue<string>       Street { get; init; } = Static("123 Main St");
    public IFieldValue<string>       City   { get; init; } = Static("Springfield");
    public IFieldValue<RegionFields> Region { get; init; } = Static(new RegionFields());
}

public record UpdateUserAddressRequest() : WorkflowRequest<UpdateUserAddressResponse>("updateUserAddress")
{
    public override IReadOnlyDictionary<string, IFieldValue<string>> PathParams { get; init; } = new Dictionary<string, IFieldValue<string>>
    {
        ["userId"] = From(ctx => ctx.Get<UserResponse>("createUser").Id)
    };
    public IFieldValue<AddressFields> Address { get; init; } = Static(new AddressFields());
}
```

Overriding only what differs at the invocation site — unspecified fields keep their defaults:

```csharp
var result = await ExecuteAsync(
    new UpdateUserAddressRequest() with
    {
        Address = Static(new AddressFields
        {
            City   = Static("Boston"),
            Region = Static(new RegionFields { State = Static("MA") })
        })
    });

Assert.Equal("Boston",      result.Address.City);
Assert.Equal("123 Main St", result.Address.Street);  // default preserved
Assert.Equal("MA",          result.Address.Region.State);
Assert.Equal("US",          result.Address.Region.Country);  // default preserved
```

### Building an array

`BuildAsync` resolves all `IFieldValue<T>` properties immediately, appends the resolved dictionary to the accumulation, and returns a typed `TResponse` snapshot. `GetAccumulated<TItem>()` returns the accumulated `List<Dictionary<string, object?>>` of already-resolved values. Resolution happens once at build time — not again when the request is sent.

`BuildAsync` returns `TResponse`, so callers can reference resolved values (including generated ones) directly:

```csharp
var widget = await BuildAsync(new AddOrderItem() with { ProductName = Static("Deluxe Widget"), Quantity = Static(3) });
await BuildAsync(new AddOrderItem() with { ProductName = Static("Basic Widget") });
var order = await ExecuteAsync(new CreateOrderRequest());

Assert.Equal("Deluxe Widget", widget.ProductName);  // TResponse — plain values, no IFieldValue
Assert.Equal(3, widget.Quantity);
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
    { "equal": ["$createOrder.id", "$getOrder.id"] },
    { "equal": ["$getOrder.status", "pending"] }
  ]
}
```

### Contracts file

Defines the transport-agnostic shape of each step — default body fields and (for build steps) the accumulation key. No HTTP execution details.

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
      "defaults": {
        "userId": { "from": "createUser.id" },
        "items":  { "from": "orderItems" }
      }
    }
  }
}
```

Steps with no body defaults (e.g. `getOrder`) need no entry in the contracts file.

### Target file

Defines how to execute steps — base URL, per-step HTTP method/path/pathParams/query/headers, and optional target-level headers. Each file is a single target. The runner finds the right target for a step by scanning which target declares that step name.

```json
{
  "baseUrl": "http://localhost:4200",
  "steps": {
    "login": {
      "method": "POST",
      "path": "/auth/login"
    },
    "createOrder": {
      "method": "POST",
      "path": "/orders",
      "headers": {
        "Authorization": { "template": "Bearer {login.token}" }
      }
    },
    "getOrder": {
      "method": "GET",
      "path": "/orders/{orderId}",
      "pathParams": {
        "orderId": { "from": "createOrder.id" }
      },
      "headers": {
        "Authorization": { "template": "Bearer {login.token}" }
      }
    }
  }
}
```

Build steps (`build:`) only use contracts — they never need a target entry.

### URL parameters (`pathParams`)

Defined in the target file under the step's entry. Values substitute into `{placeholder}` segments of the path and are never sent in the request body:

```json
"getOrder": {
  "method": "GET",
  "path": "/orders/{orderId}",
  "pathParams": {
    "orderId": { "from": "createOrder.id" }
  },
  "headers": {
    "Authorization": { "template": "Bearer {login.token}" }
  }
}
```

Multiple path parameters are supported. The key must match the placeholder name exactly.

### Query parameters (`query`)

Defined in the target file under the step's entry. Key-value pairs appended to the URL as a query string:

```json
"searchOrders": {
  "method": "GET",
  "path": "/orders",
  "query": {
    "status": { "static": "pending" },
    "userId": { "from": "createUser.id" }
  },
  "headers": {
    "Authorization": { "template": "Bearer {login.token}" }
  }
}
```

Produces: `GET /orders?status=pending&userId=abc-123`

`query` can be combined with `pathParams` in the same target step entry.

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
    { "equal":    ["$retrieved.id", "$firstOrder.id"] },
    { "notEqual": ["$retrieved.id", "$secondOrder.id"] }
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
| `equal` | `"equal": ["$createOrder.status", "pending"]` | two expressions are equal |
| `notEqual` | `"notEqual": ["$firstOrder.id", "$secondOrder.id"]` | two expressions differ |
| `single` | `"single": "$createOrder.items"` | collection has exactly 1 item |
| `empty` | `"empty": "$createOrder.items"` | collection is empty |
| `notEmpty` | `"notEmpty": "$createOrder.id"` | value is present / collection is non-empty |
| `count` | `"count": ["$createOrder.items", "2"]` | collection has exactly N items |

### Field value types

```json
{ "static": "some value" }                   // literal — any JSON primitive or array/object
{ "generated": "guid" }                      // built-in generators: guid, email, int, decimal
{ "from": "login.token" }                    // capture path — see Path reference syntax below
{ "template": "Bearer {login.token}" }       // string with {capture.path} placeholders
```

`template` substitutes `{capture.path}` placeholders with resolved values. Use `{{` and `}}` for literal braces. Throws if any placeholder cannot be resolved.

`from` accepts an optional `default` that is used when the referenced step has not run:

```json
{ "from": "createUser.id", "default": { "static": "guest" } }
```

- If `createUser` was never executed, the default resolves and is used.
- If `createUser` ran but `id` is missing or null, a `JsonWorkflowException` is thrown — the step ran, so a missing field is a bug, not an absent step.
- The `default` is itself a field value definition and supports `static`, `generated`, `from`, or `template`.

When `static` contains a JSON object, each property value is resolved recursively as a `FieldValueDefinition`. An object with a `static`, `from`, `generated`, or `template` key is treated as a field value definition; all other objects are structural nodes whose children are resolved the same way. This applies to **arrays** too — each element is recursed into, so `from`, `template`, and `generated` objects inside a `static` array are also resolved. This allows `from` and `generated` at any depth, in both objects and arrays:

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

```json
"tags": { "static": [{ "from": "step.tag" }, { "static": "fixed-tag" }] }
```

```json
"pair": { "static": [{ "template": "{step.id}.field" }, "literal"] }
```

### Nested object defaults and partial overrides

Step definitions should include full default values for all fields, including nested objects. When a `with` block specifies a nested object, it is **deep-merged** with the default: override wins for matching keys at every level, and unspecified keys are filled from the default.

This means a workflow only needs to specify the properties that differ:

```json
// contracts file — full defaults
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

### Headers

Three levels of headers are supported, merged in order (later wins for matching keys):

1. **Target-level** — defined at the root of the target file under `headers`; sent with every request to that target.
2. **Step-level** — defined in the target file under `steps.<name>.headers`; sent with every invocation of that step.
3. **Invocation-level** — defined in the workflow file under `headers` on a `step` or `poll` invocation; applies to that call only.

#### In the target file (step-level)

```json
"createItem": {
  "method": "POST",
  "path": "/items",
  "headers": {
    "X-Api-Version": { "static": "2" }
  }
}
```

#### In the workflow file (per-invocation)

```json
{ "step": "createItem", "headers": { "X-Request-Id": { "from": "session.requestId" } } }
```

#### In C# (step-level)

Override `Headers` on `HttpStep`:

```csharp
public class CreateItemStep : HttpStep<CreateItemRequest, ItemResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/items";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["X-Api-Version"] = Static("2")
        };
}
```

#### In C# (per-request)

Pass `headers:` to `ExecuteAsync` or override `Headers` on the request with `with`:

```csharp
await ExecuteAsync(new CreateItemRequest(), headers: new() { ["X-Request-Id"] = myId });
```

Or supply a target with headers via `WithTargetResolver`:

```csharp
new WorkflowContext().WithTargetResolver(_ =>
    new HttpTarget(SampleApiUrl)
        .Register(new LoginStep())
        // ... other steps
        .WithHeaders(new Dictionary<string, IFieldValue<string>>
        {
            ["X-Tenant-Id"] = Static("acme")
        }))
```

### `poll` — polling a step until a condition is met

Re-executes a step definition on an interval until an `until` assertion passes or the timeout is reached. Useful for async operations where the result isn't immediately available.

```json
{
  "steps": [
    { "step": "createOrder" },
    {
      "poll": "getOrder",
      "until": { "equal": ["$getOrder.status", "shipped"] },
      "intervalMs": 500,
      "timeoutMs": 10000
    }
  ],
  "assertions": [
    { "equal": ["$getOrder.status", "shipped"] }
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
    { "equal":    ["$createOrder.status", "pending"] },
    { "notEmpty": "$createUser.id" }
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
    { "notEqual": ["$firstOrder.id", "$secondOrder.id"] }
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
    { "equal": ["$addOrderItem.productName", "Widget A"] },
    { "equal": ["$lastItem.productName",     "Widget B"] }
  ]
}
```

Without `captureAs`, each build overwrites the previous individual capture for that step name — so `addOrderItem` above holds "Widget A" because the second build used `captureAs` instead.

### xUnit runner

```csharp
public class JsonOrderWorkflowTests : JsonWorkflowTestBase
{
    protected override IReadOnlyList<string> ContractPaths =>
    [
        "WorkflowTests/Json/Contracts/auth.contracts.json",
        "WorkflowTests/Json/Contracts/order.contracts.json"
    ];

    protected override IReadOnlyList<string> TargetPaths =>
    [
        "WorkflowTests/Json/sample-api.target.json"
    ];

    [Fact]
    public Task PlacedOrder_CanBeRetrieved() =>
        RunWorkflowAsync("WorkflowTests/Json/retrieve-order.workflow.json");
}
```

`ContractPaths` and `TargetPaths` are resolved relative to the project working directory. A class with only build steps needs no `TargetPaths`.

---

## Path reference syntax

`From` references and assertion expressions use: `captureKey.property.nested[index].property`

- First segment is always the capture key (step name or `captureAs` value)
- Supports arbitrary nesting: `step.a.b.c`
- Supports numeric index: `step.items[0]`
- Supports field lookup: `step.items[?id=abc-123]` — finds the first element where `id` equals `abc-123` (case-insensitive)
- Supports dynamic field lookup values: `step.items[?id=other.id]` — the lookup value is resolved as a capture path when it contains `.` or `[`
- Supports combinations: `step.items[?productName=Widget B].quantity`

Assertion expressions must be prefixed with `$` to be resolved as a capture path (e.g., `"$createOrder.id"`). Strings without `$` are treated as literals.

---

## Capture runtime model

| Source | Key | Type stored in captures |
|--------|-----|------------------------|
| HTTP step response | step name or `captureAs` | `Dictionary<string, JsonElement>` |
| HTTP step full response (status + body) | `captureFullResponseAs` value | `Dictionary<string, object?>` with keys `"status"` (int) and `"body"` |
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

Use `captureFullResponseAs` to capture the raw HTTP response — including the status code — under a named key instead of throwing on non-2xx. The captured value is `{ "status": <int>, "body": <parsed body> }`. If the body is valid JSON it is parsed (object → `Dictionary<string, JsonElement>`, array → `List<object?>`); otherwise the raw string is stored. Normal 2xx responses can also use `captureFullResponseAs` — it simply adds the status field alongside the body:

```json
{ "step": "deleteUser", "captureFullResponseAs": "deleteResult" }
```

After this step, `deleteResult.status` is the HTTP status code (e.g. `204`) and `deleteResult.body` is the parsed response body. The response is **not** additionally captured under the step name — `captureFullResponseAs` replaces the normal capture for that invocation.

---

## Choosing a capture strategy

| Situation | What to use |
|-----------|-------------|
| Share common setup steps across multiple workflows | `{ "workflow": "SetupUser" }` |
| Wait for an async result to reach a desired state | `{ "poll": "getOrder", "until": { "equal": ["$getOrder.status", "shipped"] } }` |
| Reference a response field from a step that runs once | Step name: `createUser.id` |
| Same HTTP step runs multiple times; need to tell results apart | `captureAs` on each invocation: `"firstOrder"`, `"secondOrder"` |
| Need a more readable alias for a response | `captureAs: "token"` instead of `login.token` |
| Server doesn't echo a request field back; need it downstream | `captureRequestAs: "userRequest"` then `userRequest.email` |
| Need the HTTP status code, or step may return non-2xx without throwing | `captureFullResponseAs: "result"` then `result.status` / `result.body.field` |
| Inspect a specific item in a built collection | Accumulation index: `orderItems[0].productName` |
| Reference the fields of the most recently built item | Build step name: `addOrderItem.productName` (overwritten each time the step builds without `captureAs`) |
| Pin a specific built item independently of the accumulation | `captureAs` on the build invocation: `"specialItem"` then `specialItem.productName` |

**Key rules:**
- `captureAs` and the step name are mutually exclusive for a given invocation — `captureAs` wins if set.
- `captureRequestAs` is additive — it doesn't replace the response capture; both are available.
- `captureFullResponseAs` replaces the normal response capture for that invocation — the step name is not populated.
- Build steps always write to the `accumulateAs` list regardless of `captureAs`.
- Without `captureAs`, repeated builds of the same step overwrite the individual result capture; the accumulation still grows.

---

## Testing strategy

Prefer testing through the public surface:

- **Path resolution / `From` references** — construct a `Dictionary<string, object?>` captures dict and call `new FromJsonValue("path").Resolve(captures)`. No need for `InternalsVisibleTo`.
- **Assertions end-to-end** — use `JsonWorkflowRunner.RunAsync(workflow, contracts, targets)` where `contracts` is `Dictionary<string, StepContractDefinition>` and `targets` is `List<TargetDefinition>`. Pass `[]` for targets when using only build steps (no HTTP required). Check `WorkflowResult.Passed` and `AssertionErrors`.
- **Full JSON workflow tests** — create a `.workflow.json` file and add a `[Fact]` to `JsonOrderWorkflowTests` (or a new `JsonWorkflowTestBase` subclass). These hit the live API.

Only reach for lower-level testing if the above is genuinely insufficient.
