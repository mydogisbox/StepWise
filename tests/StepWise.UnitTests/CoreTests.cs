using System.Text.Json;
using StepWise.Core;
using StepWise.Json;
using static StepWise.Core.FieldValues;
using Xunit;
using System.Collections.Generic;

namespace StepWise.UnitTests;

public class FieldValueTests
{
    private readonly WorkflowContext _context = new();

    [Fact]
    public void Static_AlwaysReturnsGivenValue()
    {
        var field = Static("hello");
        Assert.Equal("hello", field.Resolve(_context));
        Assert.Equal("hello", field.Resolve(_context));
    }

    [Fact]
    public void Generated_InvokesLambdaEachTime()
    {
        var counter = 0;
        var field = Generated(() => ++counter);

        Assert.Equal(1, field.Resolve(_context));
        Assert.Equal(2, field.Resolve(_context));
    }
}

public class WorkflowContextTests
{
    private record FakeResponse();
    private record FakeRequest() : WorkflowRequest<FakeResponse>("login", "test-api");
    private class FakeTarget : ITarget
    {
        public Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request, WorkflowContext context)
            => Task.FromResult((TResponse)(object)new FakeResponse());
    }

    [Fact]
    public async Task Get_ThrowsDescriptiveException_WhenStepNotFound()
    {
        var context = new WorkflowContext().WithTarget("test-api", new FakeTarget());
        await context.ExecuteAsync(new FakeRequest());

        var ex = Assert.Throws<WorkflowContextException>(
            () => context.Get<object>("missingStep"));

        Assert.Contains("missingStep", ex.Message);
        Assert.Contains("login", ex.Message);
    }

    [Fact]
    public void HasCapture_ReturnsFalse_WhenStepNotExecuted()
    {
        var context = new WorkflowContext();
        Assert.False(context.HasCapture("login"));
    }
}

public class FieldValueResolverTests
{
    private record TestResponse;
    private class TestProtocol;

    private record TestRequest<TProtocol>() : WorkflowRequest<TestResponse>("test", "test-api")
    {
        public IFieldValue<string> Name  { get; init; } = Static("Alice");
        public IFieldValue<int>    Count { get; init; } = Static(42);
    }

    [Fact]
    public void Resolve_ResolvesAllFieldValues()
    {
        var context = new WorkflowContext();
        var request = new TestRequest<TestProtocol>();

        var resolved = FieldValueResolver.Resolve<TestResponse>(request, context);

        Assert.Equal("Alice", resolved["Name"]);
        Assert.Equal(42, resolved["Count"]);
        Assert.False(resolved.ContainsKey("StepName"));
        Assert.False(resolved.ContainsKey("TargetKey"));
    }

    [Fact]
    public void Resolve_RespectsWithOverride()
    {
        var context = new WorkflowContext();
        var request = new TestRequest<TestProtocol>() with { Name = Static("Bob") };

        var resolved = FieldValueResolver.Resolve<TestResponse>(request, context);

        Assert.Equal("Bob", resolved["Name"]);
        Assert.Equal(42, resolved["Count"]);
    }
}

public class RecursiveFieldValueTests
{
    private record Inner
    {
        public IFieldValue<string> Value { get; init; } = Static("default");
    }

    private record Outer() : WorkflowRequest<object>("test", "test-api")
    {
        public IFieldValue<Inner> Nested { get; init; } = Static(new Inner());
    }

    private record DeepInner
    {
        public IFieldValue<string> Leaf { get; init; } = Static("deep");
    }

    private record MiddleLayer
    {
        public IFieldValue<DeepInner> Inner { get; init; } = Static(new DeepInner());
    }

    private record DeepOuter() : WorkflowRequest<object>("test", "test-api")
    {
        public IFieldValue<MiddleLayer> Middle { get; init; } = Static(new MiddleLayer());
    }

    [Fact]
    public void NestedRecord_FieldValuesResolvedRecursively()
    {
        var context = new WorkflowContext();
        var resolved = FieldValueResolver.Resolve<object>(new Outer(), context);

        var nested = Assert.IsType<Dictionary<string, object?>>(resolved["Nested"]);
        Assert.Equal("default", nested["Value"]);
    }

    [Fact]
    public void NestedRecord_WithOverride_ResolvesCorrectly()
    {
        var context = new WorkflowContext();
        var resolved = FieldValueResolver.Resolve<object>(
            new Outer() with { Nested = Static(new Inner() with { Value = Static("overridden") }) },
            context);

        var nested = Assert.IsType<Dictionary<string, object?>>(resolved["Nested"]);
        Assert.Equal("overridden", nested["Value"]);
    }

    [Fact]
    public void DeeplyNestedRecord_AllLevelsResolved()
    {
        var context = new WorkflowContext();
        var resolved = FieldValueResolver.Resolve<object>(new DeepOuter(), context);

        var middle = Assert.IsType<Dictionary<string, object?>>(resolved["Middle"]);
        var inner  = Assert.IsType<Dictionary<string, object?>>(middle["Inner"]);
        Assert.Equal("deep", inner["Leaf"]);
    }
}

public class FromDefaultTests
{
    private static FieldValueDefinition StaticField(object value) =>
        new() { Static = JsonSerializer.SerializeToElement(value) };

    private static Dictionary<string, StepDefinition> StepDefs => new()
    {
        ["setUser"] = new() { AccumulateAs = "users", Defaults = new() { ["id"] = StaticField("user-123") } },
        ["addItem"] = new()
        {
            AccumulateAs = "items",
            Defaults = new()
            {
                ["ownerId"] = new FieldValueDefinition { From = "setUser.id", Default = StaticField("guest") }
            }
        }
    };

    [Fact]
    public async Task From_UsesDefault_WhenCaptureNotPresent()
    {
        var workflow = new WorkflowDefinition("test",
            [new StepInvocation { Build = "addItem" }],
            [new AssertionDefinition { Equal = ["addItem.ownerId", "guest"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, StepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task From_ResolvesValue_WhenCapturePresent()
    {
        var workflow = new WorkflowDefinition("test",
            [
                new StepInvocation { Build = "setUser" },
                new StepInvocation { Build = "addItem" }
            ],
            [new AssertionDefinition { Equal = ["addItem.ownerId", "user-123"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, StepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task From_Throws_WhenRootPresentButFieldAbsent()
    {
        // setUser ran but has no "nonexistent" field — this is a bug, not a missing step
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["setUser"] = new() { AccumulateAs = "users", Defaults = new() { ["id"] = StaticField("user-123") } },
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new()
                {
                    ["ownerId"] = new FieldValueDefinition { From = "setUser.nonexistent", Default = StaticField("guest") }
                }
            }
        };

        var workflow = new WorkflowDefinition("test",
            [
                new StepInvocation { Build = "setUser" },
                new StepInvocation { Build = "addItem" }
            ],
            null);

        await Assert.ThrowsAsync<JsonWorkflowException>(() =>
            JsonWorkflowRunner.RunAsync(workflow, stepDefs, []));
    }
}

public class BuildableRequestAccumulationTests
{
    private record FakeResponse;
    private record LineItemResponse(string Name, int Count);

    private record LineItem() : BuildableRequest<LineItemResponse>
    {
        public IFieldValue<string> Name  { get; init; } = Static("Widget");
        public IFieldValue<int>    Count { get; init; } = Static(1);
    }

    private record OrderRequest() : WorkflowRequest<FakeResponse>("createOrder", "test-api")
    {
        public IFieldValue<List<Dictionary<string, object?>>> Items { get; init; } = From(ctx => ctx.GetAccumulated<LineItem>());
    }

    [Fact]
    public async Task BuildAsync_ReturnsResolvedResponse()
    {
        var context = new WorkflowContext();
        var result = await context.BuildAsync(new LineItem() with { Name = Static("Gadget"), Count = Static(3) });

        Assert.Equal("Gadget", result.Name);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetAccumulated_ReturnsResolvedDicts()
    {
        var context = new WorkflowContext();
        await context.BuildAsync(new LineItem());
        await context.BuildAsync(new LineItem() with { Name = Static("Gadget"), Count = Static(3) });

        var items = context.GetAccumulated<LineItem>();

        Assert.Equal(2, items.Count);
        Assert.Equal("Widget", items[0]["Name"]);
        Assert.Equal(1, items[0]["Count"]);
        Assert.Equal("Gadget", items[1]["Name"]);
        Assert.Equal(3, items[1]["Count"]);
    }

    [Fact]
    public async Task BuildableItems_AvailableInResolvedRequest()
    {
        var context = new WorkflowContext();
        await context.BuildAsync(new LineItem() with { Name = Static("Widget"), Count = Static(2) });
        await context.BuildAsync(new LineItem() with { Name = Static("Gadget"), Count = Static(5) });

        var resolved = FieldValueResolver.Resolve(new OrderRequest(), context);

        var items = Assert.IsType<List<object?>>(resolved["Items"]);
        Assert.Equal(2, items.Count);
        var first  = Assert.IsType<Dictionary<string, object?>>(items[0]);
        var second = Assert.IsType<Dictionary<string, object?>>(items[1]);
        Assert.Equal("Widget", first["Name"]);
        Assert.Equal(2, first["Count"]);
        Assert.Equal("Gadget", second["Name"]);
        Assert.Equal(5, second["Count"]);
    }
}

public class TemplateFieldValueTests
{
    private static FieldValueDefinition TemplateDef(string template) =>
        new() { Template = template };

    private static Dictionary<string, object?> LoginCapture(string token) => new()
    {
        ["login"] = new Dictionary<string, object?> { ["token"] = token }
    };

    [Fact]
    public void Template_SubstitutesCapturePath()
    {
        var result = JsonValueResolver.Resolve(TemplateDef("Bearer {login.token}"))
            .Resolve(LoginCapture("abc123"));

        Assert.Equal("Bearer abc123", result);
    }

    [Fact]
    public void Template_SubstitutesMultiplePlaceholders()
    {
        var captures = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?> { ["first"] = "Alice", ["last"] = "Smith" }
        };
        var result = JsonValueResolver.Resolve(TemplateDef("{user.first} {user.last}"))
            .Resolve(captures);

        Assert.Equal("Alice Smith", result);
    }

    [Fact]
    public void Template_NoPlaceholders_ReturnsLiteral()
    {
        var result = JsonValueResolver.Resolve(TemplateDef("plain text")).Resolve([]);

        Assert.Equal("plain text", result);
    }

    [Fact]
    public void Template_EscapedBraces_ArePreserved()
    {
        var result = JsonValueResolver.Resolve(TemplateDef("{{literal}}")).Resolve([]);

        Assert.Equal("{literal}", result);
    }

    [Fact]
    public void Template_MissingCapture_Throws()
    {
        Assert.Throws<JsonWorkflowException>(() =>
            JsonValueResolver.Resolve(TemplateDef("Bearer {login.token}")).Resolve([]));
    }

    [Fact]
    public async Task Template_UsedAsHeader_InBuildWorkflow()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["setToken"] = new() { AccumulateAs = "tokens", Defaults = new() { ["token"] = StaticField("my-token") } },
            ["addItem"]  = new()
            {
                AccumulateAs = "items",
                Defaults = new()
                {
                    ["authHeader"] = new FieldValueDefinition { Template = "Bearer {setToken.token}" }
                }
            }
        };

        var workflow = new WorkflowDefinition("test",
            [
                new StepInvocation { Build = "setToken" },
                new StepInvocation { Build = "addItem" }
            ],
            [new AssertionDefinition { Equal = ["addItem.authHeader", "Bearer my-token"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    private static FieldValueDefinition StaticField(object value) =>
        new() { Static = JsonSerializer.SerializeToElement(value) };
}
