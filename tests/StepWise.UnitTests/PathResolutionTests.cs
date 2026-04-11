using System.Text.Json;
using StepWise.Json;
using Xunit;

namespace StepWise.UnitTests;

/// <summary>
/// Tests for array indexing and deep-nesting support in From references and assertions.
/// Uses FromJsonValue (public) to exercise ResolveCapturePath, and Build steps (no HTTP)
/// to exercise assertion evaluation end-to-end.
/// </summary>
public class FromValuePathResolutionTests
{
    // Simulates captures as stored after an HTTP step: Dictionary<string, JsonElement>
    private static Dictionary<string, object?> ApiCaptures(string stepName, string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [stepName] = dict };
    }

    [Fact]
    public void SimpleProperty_Resolves()
    {
        var captures = ApiCaptures("login", """{"token":"abc123"}""");
        Assert.Equal("abc123", new FromJsonValue("login.token").Resolve(captures));
    }

    [Fact]
    public void DeepNestedProperty_Resolves()
    {
        var captures = ApiCaptures("createUser", """{"user":{"profile":{"name":"Alice"}}}""");
        Assert.Equal("Alice", new FromJsonValue("createUser.user.profile.name").Resolve(captures));
    }

    [Fact]
    public void ArrayIndex_Resolves()
    {
        var captures = ApiCaptures("getOrders", """{"orders":[{"id":10},{"id":20}]}""");
        Assert.Equal(10, new FromJsonValue("getOrders.orders[0].id").Resolve(captures));
    }

    [Fact]
    public void NonZeroArrayIndex_Resolves()
    {
        var captures = ApiCaptures("getOrders", """{"orders":[{"id":10},{"id":20},{"id":30}]}""");
        Assert.Equal(30, new FromJsonValue("getOrders.orders[2].id").Resolve(captures));
    }

    [Fact]
    public void ArrayOfScalars_IndexResolves()
    {
        var captures = ApiCaptures("getTags", """{"tags":["alpha","beta","gamma"]}""");
        Assert.Equal("beta", new FromJsonValue("getTags.tags[1]").Resolve(captures));
    }

    [Fact]
    public void ArrayIndexWithDeepNesting_Resolves()
    {
        var captures = ApiCaptures("getData", """{"items":[{"meta":{"label":"first"}},{"meta":{"label":"second"}}]}""");
        Assert.Equal("second", new FromJsonValue("getData.items[1].meta.label").Resolve(captures));
    }

    [Fact]
    public void MissingStep_ReturnsNull()
    {
        var captures = ApiCaptures("login", """{"token":"abc"}""");
        Assert.Null(new FromJsonValue("missing.token").Resolve(captures));
    }

    [Fact]
    public void MissingProperty_ReturnsNull()
    {
        var captures = ApiCaptures("login", """{"token":"abc"}""");
        Assert.Null(new FromJsonValue("login.nothere").Resolve(captures));
    }

    [Fact]
    public void OutOfBoundsIndex_ReturnsNull()
    {
        var captures = ApiCaptures("getItems", """{"items":[1,2]}""");
        Assert.Null(new FromJsonValue("getItems.items[5]").Resolve(captures));
    }
}

/// <summary>
/// Tests that Build step results are captured under the step name (and captureAs)
/// in addition to being appended to the accumulation list.
/// </summary>
public class BuildStepCaptureTests
{
    private static FieldValueDefinition StaticField(object value) =>
        new() { Static = JsonSerializer.SerializeToElement(value) };

    [Fact]
    public async Task BuildResult_IsAvailable_UnderStepName()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new() { ["name"] = StaticField("Widget") }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addItem" }],
            [new AssertionDefinition { Equal = ["addItem.name", "Widget"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task BuildResult_CaptureAs_OverridesDefaultKey()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new() { ["name"] = StaticField("Widget") }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addItem", CaptureAs = "lastItem" }],
            [new AssertionDefinition { Equal = ["lastItem.name", "Widget"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task BuildResult_StillAccumulates()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new() { ["name"] = StaticField("Widget") }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [
                new StepInvocation { Build = "addItem" },
                new StepInvocation { Build = "addItem" },
            ],
            [new AssertionDefinition { NotEmpty = "items" }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
        Assert.Equal(2, ((System.Collections.IList)result.Captures["items"]!).Count);
    }
}

/// <summary>
/// Tests assertion evaluation end-to-end using Build steps (no HTTP required).
/// Exercises array indexing and deep nesting via WorkflowResult.
/// </summary>
public class AssertionPathTests
{
    private static FieldValueDefinition StaticField(object value) =>
        new() { Static = JsonSerializer.SerializeToElement(value) };

    [Fact]
    public async Task Assertion_Equal_ArrayIndex_Passes()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addOrder"] = new()
            {
                AccumulateAs = "orders",
                Defaults = new() { ["status"] = StaticField("shipped") }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addOrder" }],
            [new AssertionDefinition { Equal = ["orders[0].status", "shipped"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task Assertion_Equal_ArrayIndex_Fails_WhenValueWrong()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addOrder"] = new()
            {
                AccumulateAs = "orders",
                Defaults = new() { ["status"] = StaticField("pending") }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addOrder" }],
            [new AssertionDefinition { Equal = ["orders[0].status", "shipped"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.False(result.Passed);
        Assert.Single(result.AssertionErrors);
    }

    [Fact]
    public async Task Assertion_NotEmpty_ArrayIndex_Passes()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new() { ["id"] = StaticField("abc-123") }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addItem" }],
            [new AssertionDefinition { NotEmpty = "items[0].id" }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task Assertion_Equal_SecondItemInArray_Passes()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addOrder"] = new()
            {
                AccumulateAs = "orders",
                Defaults = new() { ["status"] = StaticField("pending") }
            }
        };

        // Build two items; override the second one's status
        var workflow = new WorkflowDefinition(
            "test",
            [
                new StepInvocation { Build = "addOrder" },
                new StepInvocation { Build = "addOrder", With = new() { ["status"] = StaticField("shipped") } }
            ],
            [new AssertionDefinition { Equal = ["orders[1].status", "shipped"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }
}

public class CountAssertionTests
{
    private static FieldValueDefinition StaticField(object value) =>
        new() { Static = JsonSerializer.SerializeToElement(value) };

    private static Dictionary<string, StepDefinition> ItemStepDefs => new()
    {
        ["addItem"] = new() { AccumulateAs = "items", Defaults = new() { ["name"] = StaticField("Widget") } }
    };

    [Fact]
    public async Task Count_Passes_WhenExactMatch()
    {
        var workflow = new WorkflowDefinition(
            "test",
            [
                new StepInvocation { Build = "addItem" },
                new StepInvocation { Build = "addItem" },
                new StepInvocation { Build = "addItem" },
            ],
            [new AssertionDefinition { Count = ["items", "3"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, ItemStepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task Count_Fails_WhenCountMismatch()
    {
        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addItem" }],
            [new AssertionDefinition { Count = ["items", "3"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, ItemStepDefs, []);
        Assert.False(result.Passed);
        Assert.Single(result.AssertionErrors);
        Assert.Contains("1", result.AssertionErrors[0]);
        Assert.Contains("3", result.AssertionErrors[0]);
    }

    [Fact]
    public async Task Count_Fails_WithDescriptiveMessage_WhenInvalidNumber()
    {
        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addItem" }],
            [new AssertionDefinition { Count = ["items", "notanumber"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, ItemStepDefs, []);
        Assert.False(result.Passed);
        Assert.Contains("notanumber", result.AssertionErrors[0]);
    }
}
