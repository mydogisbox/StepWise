# Walkthrough — Claude Guidance

Walkthrough is a C# workflow testing library for APIs. It lets you write integration tests that express multi-step API workflows — login, create a user, place an order — with minimal noise. Each test only specifies what matters; everything else flows through sensible defaults.

---

## Philosophy

**Tests should express consumer capabilities, not API mechanics.**

A test named `NewUser_CanPlaceOrder` describes an interaction — something a consumer of this system is able to do. The test structure reflects that: set up the actor, perform the interaction, assert the outcome. The HTTP calls, request bodies, and response shapes are implementation details of the interaction, not the point of the test.

**Tests highlight what they are testing and nothing else.**

A test that verifies an order is created with two specific items should say exactly that — and nothing about the email address used to log in, the user's role, or the product's default price. Every field specified in a test is a claim that this field matters to this test. Defaults exist so that claim stays true. The same applies to values that flow between steps — if an order needs the user ID from a prior step, reference it rather than hardcoding it. A hardcoded value is a silent claim that the specific value matters, when usually only the dependency does.

---

## Running tests

Always use `./test.sh`. It starts the sample API, runs all test projects, and tears the API down. Do not run `dotnet test` directly — integration tests depend on the API being up.

Run tests after every change to verify nothing is broken.

---

## Architecture

```
Walkthrough.Core
├── WorkflowRequest<TResponse>     — transport-agnostic base record; carries only StepName
├── BuildableRequest               — non-generic marker base for array item builders
├── BuildableRequest<TResponse>    — generic base; TResponse is the resolved snapshot type returned by BuildAsync
├── WorkflowContext                — pure state bag: captures and accumulations only; no execution logic
├── ITarget                        — execute a request against a target; implemented by HttpTarget or any custom class
├── WorkflowRunner                 — orchestrates execution: ExecuteAsync, PollAsync, BuildAsync
├── IFieldValue<T>                 — interface for resolvable field values
├── FieldValues                    — Static(), Generated(), From() factories
└── FieldValueResolver             — reflection-based resolver

Walkthrough.Http
├── HttpWorkflowRequest<TResponse> — marker base for HTTP requests; carries only body fields (StepName only)
├── HttpTarget : ITarget           — sends requests over HTTP; steps registered explicitly via Register()
├── HttpExecutor                   — shared HTTP send/deserialize logic
└── HttpStep<TRequest, TResponse>  — declares Method, Path, MapBody, MapQuery, and MapHeaders for one request type

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
│   ├── Login.cs
│   ├── User.cs
│   └── Order.cs
├── WalkthroughTestBase.cs
└── WorkflowTests/
    ├── OrderWorkflowTests.cs
    └── Json/
        ├── JsonOrderWorkflowTests.cs
        ├── sample-api.target.json
        ├── Contracts/
        │   ├── auth.contracts.json
        │   ├── order.contracts.json
        │   └── user.contracts.json
        └── *.workflow.json
```

---

## Testing strategy

Prefer testing through the public surface:

- **Path resolution / `From` references** — construct a `Dictionary<string, object?>` captures dict and call `new FromJsonValue("path").Resolve(captures)`. No need for `InternalsVisibleTo`.
- **Assertions end-to-end** — use `JsonWorkflowRunner.RunAsync(workflow, contracts, targets)` where `contracts` is `Dictionary<string, StepContractDefinition>` and `targets` is `List<TargetDefinition>`. Pass `[]` for targets when using only build steps (no HTTP required). Check `WorkflowResult.Passed` and `AssertionErrors`.
- **Full JSON workflow tests** — create a `.workflow.json` file and add a `[Fact]` to `JsonOrderWorkflowTests` (or a new `JsonWorkflowTestBase` subclass). These hit the live API.

Only reach for lower-level testing if the above is genuinely insufficient.

---

## Style guides

- C#: `docs/claude/csharp-style.md`
- JSON: `docs/claude/json-style.md`
