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
