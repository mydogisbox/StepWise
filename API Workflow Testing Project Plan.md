# Project Plan: C# API Workflow Testing Library

## Vision

A testing library that lets developers write API workflow tests focused on **what matters** — not on boilerplate.
You describe the steps a user would take; the library fills in sensible defaults for everything else.

---

## Core Concept

### The Problem It Solves

```csharp
// Without the library — noisy, hard to see what's being tested
var user = new CreateUserRequest {
    Email = "test@example.com",
    FirstName = "Test",
    LastName = "User",
    DateOfBirth = new DateTime(1990, 1, 1),
    PhoneNumber = "555-0000",
    AcceptsMarketing = false,
    // ... 10 more fields you don't care about
};

// With the library — only specify what matters for THIS test
var login = await Login.ExecuteAsync(LoginRequest.Default, context);
var user  = await CreateUser.ExecuteAsync(
    CreateUserRequest.Default with { Email = Static("buyer@test.com") },
    context
);
var order = await PlaceOrder.ExecuteAsync(
    PlaceOrderRequest.Default with { UserId = Static(user.Id) },
    context
);
Assert.Equal("pending", order.Status);
```

---

## Library Name (suggestion)

**`ApiFlow`** — open to alternatives.

---

## Architecture

### Layer 1 — Transport Core
Thin wrappers per transport:
- `HttpClient` with base URL + auth configuration, JSON serialization, response unwrapping
- Future: gRPC channel, GraphQL client

### Layer 2 — Request Records
Pure data. Each request record carries its step name and field values — no transport concerns.

```csharp
public record CreateUserRequest(
    IFieldValue<string> Email,
    IFieldValue<string> FirstName,
    IFieldValue<string> LastName,
    IFieldValue<string> Role
) : WorkflowRequest("createUser")
{
    public static CreateUserRequest Default => new(
        Email:     Generated(() => new Faker().Internet.Email()),
        FirstName: Generated(() => new Faker().Name.FirstName()),
        LastName:  Generated(() => new Faker().Name.LastName()),
        Role:      Static("user")
    );
}
```

### Layer 3 — Step Classes
Each step class owns the execution details for a specific transport. The same request record can be executed by different step implementations — enabling swappable transports per environment.

```csharp
// HTTP
public class CreateUserHttpStep : HttpWorkflowStep<CreateUserRequest, UserResponse>
{
    public override HttpMethod Method => HttpMethod.Post;
    public override string Path => "/users";
}

// gRPC (future)
public class CreateUserGrpcStep : GrpcWorkflowStep<CreateUserRequest, UserResponse>
{
    public override ServiceMethod Method => UserService.CreateUser;
}
```

### Layer 4 — Value Resolution
Before a step executes, the `WorkflowContext` resolves all `IFieldValue<T>` fields:
- `Static<T>` — returns the value as-is
- `Generated<T>` — invokes the lambda
- `From<T>` — invokes the typed context lookup lambda

### Layer 5 — Workflow Runner
Executes steps in sequence, resolving field values and capturing responses into context by step name.

### Layer 6 — Declarative Workflows (JSON)
Optional: define workflow templates in JSON for data-driven or non-developer tests.

---

## Key Design Principles

| Principle | Detail |
|---|---|
| **Defaults everywhere** | Every field has a default — tests only specify what's meaningful |
| **Stateful context** | Steps pass outputs to subsequent steps via typed `From(...)` lookups |
| **Composable** | Individual steps usable standalone or in sequence |
| **Transport-agnostic core** | HTTP first, but `IWorkflowStep` works for gRPC, GraphQL, etc. |
| **Auth per step** | Each step declares its own `IAuthProvider` — supports mixed-auth workflows |
| **Named base URLs** | Context holds a named map of base URLs; steps declare which key they use |
| **Test framework agnostic** | Works with xUnit, NUnit, MSTest |

---

## Proposed Project Structure

```
ApiFlow/
├── src/
│   ├── ApiFlow.Core/                   # Abstractions, interfaces, base classes
│   │   ├── IFieldValue.cs              # IFieldValue<T> interface
│   │   ├── FieldValues.cs              # Static<T>, Generated<T>, From<T> + factory methods
│   │   ├── WorkflowRequest.cs          # Base record carrying step name
│   │   ├── WorkflowContext.cs          # Carries state and named base URLs between steps
│   │   └── IWorkflowStep.cs            # IWorkflowStep<TRequest, TResponse>
│   │
│   ├── ApiFlow.Http/                   # HTTP-specific step implementation
│   │   ├── HttpWorkflowStep.cs         # Base class for HTTP steps
│   │   ├── HttpApiClient.cs            # Thin HttpClient wrapper
│   │   └── Auth/
│   │       ├── IAuthProvider.cs
│   │       ├── NoAuth.cs
│   │       ├── BearerTokenAuth.cs
│   │       └── ApiKeyAuth.cs
│   │
│   └── ApiFlow.Json/                   # JSON declarative workflow runner (optional)
│       ├── WorkflowDefinition.cs
│       └── JsonWorkflowRunner.cs
│
├── tests/
│   ├── ApiFlow.UnitTests/              # Tests for core abstractions
│   └── ApiFlow.IntegrationTests/       # Tests against a real sample API
│
├── samples/
│   ├── ApiFlow.SampleApi/              # Minimal ASP.NET Core API (runs locally)
│   │   ├── Program.cs                  # All endpoints defined here
│   │   ├── Models/                     # Request/response models
│   │   └── ApiFlow.SampleApi.csproj
│   │
│   └── ApiFlow.SampleWorkflows/        # Example tests written against the sample API
│       ├── Steps/                      # Step class definitions
│       ├── Requests/                   # Request records with Defaults
│       └── WorkflowTests/              # Actual test classes
│
└── ApiFlow.sln
```

---

## NuGet Package Plan

| Package | Purpose |
|---|---|
| `ApiFlow.Core` | Required. `IFieldValue<T>`, `Static`, `Generated`, `From`, `WorkflowRequest`, `WorkflowContext`, `IWorkflowStep`. |
| `ApiFlow.Http` | HTTP step base class and client. |
| `ApiFlow.Json` | JSON declarative workflow runner (optional). |

---

## Key Abstractions to Define First

### `IFieldValue<T>` — The Core Value Resolution Type

Every field on a request record is `IFieldValue<T>`, resolved at execution time by the workflow context.

```csharp
public interface IFieldValue<T>
{
    T Resolve(WorkflowContext context);
}
```

Three built-in implementations:

```csharp
// 1. Static — a hardcoded value
Static("buyer@test.com")
Static(42)

// 2. Generated — a lambda, evaluated fresh each time
Generated(() => Guid.NewGuid().ToString())
Generated(() => new Faker().Internet.Email())

// 3. From — typed lookup from a previous step's captured response
From(ctx => ctx.Get<UserResponse>("createUser").Id)
```

### `WorkflowRequest` — Base Record
Carries the step name used to capture the response into context.

```csharp
public abstract record WorkflowRequest(string StepName);
```

### Request Records
Pure data — no transport or HTTP concerns. Own their own generation defaults.

```csharp
public record CreateUserRequest(
    IFieldValue<string> Email,
    IFieldValue<string> FirstName,
    IFieldValue<string> LastName,
    IFieldValue<string> Role
) : WorkflowRequest("createUser")
{
    public static CreateUserRequest Default => new(
        Email:     Generated(() => new Faker().Internet.Email()),
        FirstName: Generated(() => new Faker().Name.FirstName()),
        LastName:  Generated(() => new Faker().Name.LastName()),
        Role:      Static("user")
    );
}
```

### `IWorkflowStep<TRequest, TResponse>`
```csharp
public interface IWorkflowStep<TRequest, TResponse>
    where TRequest : WorkflowRequest
{
    Task<TResponse> ExecuteAsync(TRequest request, WorkflowContext context);
}
```

### `HttpWorkflowStep<TRequest, TResponse>`
Base class for HTTP steps. Subclasses declare the verb, path, base URL key, and auth provider. The base class handles value resolution, serialization, and response capture.

```csharp
public abstract class HttpWorkflowStep<TRequest, TResponse>
    : IWorkflowStep<TRequest, TResponse>
    where TRequest : WorkflowRequest
{
    public abstract HttpMethod Method { get; }
    public abstract string Path { get; }
    public abstract string BaseUrlKey { get; }
    public virtual IAuthProvider Auth => NoAuth.Instance;

    public async Task<TResponse> ExecuteAsync(TRequest request, WorkflowContext context)
    {
        // 1. Resolve all IFieldValue<T> fields on the request
        // 2. Resolve auth from context
        // 3. Build full URL: context.BaseUrls[BaseUrlKey] + Path
        // 4. Serialize and send HTTP request
        // 5. Deserialize response
        // 6. Capture into context under request.StepName
        // 7. Return typed response
    }
}
```

### `WorkflowContext`
```csharp
public class WorkflowContext
{
    public Dictionary<string, object> Captures { get; } = new();
    public Dictionary<string, string> BaseUrls { get; set; } = new();
    public HttpClient HttpClient { get; set; }

    public void Capture<T>(string stepName, T response) =>
        Captures[stepName] = response;

    public T Get<T>(string stepName) =>
        (T)Captures[stepName];
}
```

---

## Milestones

### M1 — Foundation ✅ Start here
- [ ] `ApiFlow.Core` — `IFieldValue<T>`, `Static`, `Generated`, `From`
- [ ] `WorkflowRequest` base record
- [ ] `WorkflowContext` with named `BaseUrls` and typed `Captures`
- [ ] `IWorkflowStep<TRequest, TResponse>`

### M2 — HTTP Transport
- [ ] `HttpWorkflowStep<TRequest, TResponse>` base class
- [ ] Field value resolution on request records
- [ ] `IAuthProvider` + `NoAuth`, `BearerTokenAuth`, `ApiKeyAuth`
- [ ] Full URL assembly from `context.BaseUrls[BaseUrlKey] + Path`

### M3 — Sample API + First End-to-End Test
- [ ] `ApiFlow.SampleApi` — minimal ASP.NET Core API with login, users, orders (in-memory)
- [ ] `ApiFlow.SampleWorkflows` — step classes, request records, and workflow tests against sample API
- [ ] `ApiFlowTestBase` helper with `NewContext(baseUrls)`
- [ ] Integration test covering login → create user → place order workflow

### M4 — Developer Experience
- [ ] Clear error messages when step capture is missing from context
- [ ] Request/response logging per step
- [ ] Unit tests for core value resolution logic

### M5 — JSON Workflows (optional)
- [ ] `WorkflowDefinition` schema
- [ ] `JsonWorkflowRunner`
- [ ] Variable substitution

### M6 — Packaging
- [ ] `ApiFlow.Core` NuGet package
- [ ] `ApiFlow.Http` NuGet package
- [ ] README and sample project

---

## Auth Providers (v1)

Three built-in implementations of `IAuthProvider`:

```csharp
public interface IAuthProvider
{
    Task ApplyAsync(HttpRequestMessage request, WorkflowContext context);
}

// 1. No auth
NoAuth.Instance

// 2. Bearer token — static or resolved from context
BearerTokenAuth.Static("my-token")
BearerTokenAuth.From(ctx => ctx.Get<LoginResponse>("login").Token)

// 3. API key — configurable header name or query param
ApiKeyAuth.Header("X-Api-Key", Static("my-key"))
ApiKeyAuth.Header("X-Api-Key", From(ctx => ctx.Get<LoginResponse>("login").ApiKey))
ApiKeyAuth.QueryParam("api_key", Static("my-key"))
```

---

## Sample Test (Target API)

```csharp
public class OrderWorkflowTests : ApiFlowTestBase
{
    private static readonly LoginHttpStep Login = new();
    private static readonly CreateUserHttpStep CreateUser = new();
    private static readonly PlaceOrderHttpStep PlaceOrder = new();

    [Fact]
    public async Task NewUser_CanPlaceOrder_AndSeeItAsPending()
    {
        var context = NewContext(baseUrls: new()
        {
            ["identity-api"] = "https://localhost:5001",
            ["users-api"]    = "https://localhost:5001",
            ["orders-api"]   = "https://localhost:5001",
        });

        var login = await Login.ExecuteAsync(
            LoginRequest.Default with {
                Username = Static("admin@test.com"),
                Password = Static("password123")
            },
            context
        );

        var user = await CreateUser.ExecuteAsync(
            CreateUserRequest.Default with { Email = Static("buyer@test.com") },
            context
        );

        var order = await PlaceOrder.ExecuteAsync(
            PlaceOrderRequest.Default with { UserId = Static(user.Id) },
            context
        );

        Assert.Equal("pending", order.Status);
        Assert.Equal(user.Id, order.BuyerId);
    }
}
```

### Note on `From(...)`

`From(...)` is not used directly in test bodies. Tests get values from prior step return values directly.
`From(...)` belongs in `Default` definitions, encoding the "happy path" assumption for reusable workflow composition.

```csharp
// In a Default — From(...) makes sense here
public static PlaceOrderRequest Default => new(
    UserId: From(ctx => ctx.Get<UserResponse>("createUser").Id),
    // ...
);
```