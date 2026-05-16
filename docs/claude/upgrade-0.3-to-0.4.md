# Upgrade guide: 0.3.0 → 0.4.0

Two breaking changes. Both are mechanical find-and-replace with no logic changes required.

---

## 1. Request declaration

### What changed

`WorkflowRequest<TResponse>` no longer takes a constructor argument for the step name. The step name is now a static property on the concrete request type, and the base class is now `WorkflowRequest<TResponse, TSelf>` (CRTP).

### Before

```csharp
public record CreateOrderRequest() : WorkflowRequest<OrderResponse>("createOrder")
{
    public IFieldValue<string> UserId { get; init; } = ...;
}
```

### After

```csharp
public record CreateOrderRequest() : WorkflowRequest<OrderResponse, CreateOrderRequest>, IWorkflowRequest
{
    public static string StepName => "createOrder";
    public IFieldValue<string> UserId { get; init; } = ...;
}
```

### Migration rule

For every `WorkflowRequest` subclass:

1. Change `: WorkflowRequest<TResponse>("stepName")` → `: WorkflowRequest<TResponse, ClassName>, IWorkflowRequest`
2. Add `public static string StepName => "stepName";` as the first property in the body
3. If the record had no body (single-line declaration), add braces

For requests with no fields (previously a one-liner):

```csharp
// Before
public record EchoRequest() : WorkflowRequest<EchoResponse>("echo");

// After
public record EchoRequest() : WorkflowRequest<EchoResponse, EchoRequest>, IWorkflowRequest
{
    public static string StepName => "echo";
}
```

### Why

`StepName` as a constructor parameter was declared as a positional record property, which reserved the name `StepName` in the field namespace. Any API field named `StepName` could not be represented. Moving it to a static property removes it from the instance field space entirely.

---

## 2. WalkthroughTestBase / WorkflowRunner method signatures

### What changed

`ExecuteAsync`, `ExecuteRawAsync`, and `PollAsync` now have a `TSelf` type parameter to match the new CRTP base. Call sites are **unchanged** — C# infers both type parameters from the argument. Only wrapper methods that explicitly typed `WorkflowRequest<TResponse>` need updating.

### Call sites (no change needed)

```csharp
// These work exactly as before — inference handles both type params
await ExecuteAsync(new CreateOrderRequest());
await ExecuteRawAsync(new CreateOrderRequest());
await runner.PollAsync(new GetOrderRequest(), r => r.Status == "shipped");
```

### Wrapper method signatures (update if you have them)

If you wrote your own base class or helper that wraps `WorkflowRunner.ExecuteAsync`:

```csharp
// Before
protected Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request)
    => _runner.ExecuteAsync(request);

// After
protected Task<TResponse> ExecuteAsync<TResponse, TSelf>(WorkflowRequest<TResponse, TSelf> request)
    where TSelf : WorkflowRequest<TResponse, TSelf>, IWorkflowRequest
    => _runner.ExecuteAsync(request);
```

Apply the same pattern to `ExecuteRawAsync` and any `PollAsync` wrappers.

### Custom ITarget implementations (no change needed)

`ITarget.ExecuteAsync` signature is unchanged:

```csharp
Task<TResponse> ExecuteAsync<TResponse>(
    WorkflowRequest<TResponse> request,
    Dictionary<string, object?> resolvedFields,
    WorkflowContext context);
```

`WorkflowRequest<TResponse, TSelf>` is a subtype of `WorkflowRequest<TResponse>`, so existing custom targets compile without modification.
