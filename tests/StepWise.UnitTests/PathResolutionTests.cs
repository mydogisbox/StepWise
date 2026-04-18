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

    [Fact]
    public void FieldLookup_ResolvesMatchingItem()
    {
        var captures = ApiCaptures("getOrders", """{"orders":[{"id":"a","status":"pending"},{"id":"b","status":"shipped"}]}""");
        Assert.Equal("shipped", new FromJsonValue("getOrders.orders[?id=b].status").Resolve(captures));
    }

    [Fact]
    public void FieldLookup_IsCaseInsensitive()
    {
        var captures = ApiCaptures("getOrders", """{"orders":[{"id":"A","status":"pending"},{"id":"B","status":"shipped"}]}""");
        Assert.Equal("shipped", new FromJsonValue("getOrders.orders[?id=b].status").Resolve(captures));
    }

    [Fact]
    public void FieldLookup_ReturnsNull_WhenNoMatch()
    {
        var captures = ApiCaptures("getOrders", """{"orders":[{"id":"a","status":"pending"}]}""");
        Assert.Null(new FromJsonValue("getOrders.orders[?id=z].status").Resolve(captures));
    }

    [Fact]
    public void FieldLookup_ReturnsFirstMatch_WhenMultipleMatch()
    {
        var captures = ApiCaptures("getData", """{"items":[{"type":"widget","name":"First"},{"type":"widget","name":"Second"}]}""");
        Assert.Equal("First", new FromJsonValue("getData.items[?type=widget].name").Resolve(captures));
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

public class NestedStaticResolutionTests
{
    private static FieldValueDefinition StaticField(object value) =>
        new() { Static = JsonSerializer.SerializeToElement(value) };

    [Fact]
    public async Task Static_ObjectWithNestedFieldValueDefs_ResolvesRecursively()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new()
                {
                    ["address"] = StaticField(new Dictionary<string, object>
                    {
                        ["city"]  = new Dictionary<string, string> { ["static"] = "Boston" },
                        ["state"] = new Dictionary<string, string> { ["static"] = "MA" }
                    })
                }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addItem" }],
            [
                new AssertionDefinition { Equal = ["addItem.address.city",  "Boston"] },
                new AssertionDefinition { Equal = ["addItem.address.state", "MA"] }
            ]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task Static_DeeplyNestedObjects_ResolveAllLevels()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new()
                {
                    ["contact"] = StaticField(new Dictionary<string, object>
                    {
                        ["primary"] = new Dictionary<string, object>
                        {
                            ["static"] = new Dictionary<string, object>
                            {
                                ["address"] = new Dictionary<string, object>
                                {
                                    ["static"] = new Dictionary<string, object>
                                    {
                                        ["city"]   = new Dictionary<string, string> { ["static"] = "Boston" },
                                        ["region"] = new Dictionary<string, object>
                                        {
                                            ["static"] = new Dictionary<string, object>
                                            {
                                                ["state"] = new Dictionary<string, string> { ["static"] = "MA" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    })
                }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Build = "addItem" }],
            [
                new AssertionDefinition { Equal = ["addItem.contact.primary.address.city",         "Boston"] },
                new AssertionDefinition { Equal = ["addItem.contact.primary.address.region.state", "MA"] }
            ]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task Static_NestedWith_DeepMergesWithDefaults()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new()
                {
                    ["address"] = StaticField(new Dictionary<string, object>
                    {
                        ["street"] = new Dictionary<string, string> { ["static"] = "123 Main St" },
                        ["city"]   = new Dictionary<string, string> { ["static"] = "Springfield" },
                        ["state"]  = new Dictionary<string, string> { ["static"] = "IL" }
                    })
                }
            }
        };

        // Override only "city"; "street" and "state" should come from defaults.
        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation
            {
                Build = "addItem",
                With = new()
                {
                    ["address"] = StaticField(new Dictionary<string, object>
                    {
                        ["city"] = new Dictionary<string, string> { ["static"] = "Boston" }
                    })
                }
            }],
            [
                new AssertionDefinition { Equal = ["addItem.address.city",   "Boston"] },
                new AssertionDefinition { Equal = ["addItem.address.state",  "IL"] },
                new AssertionDefinition { NotEmpty = "addItem.address.street" }
            ]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, []);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task Static_NestedFrom_ResolvesAgainstCaptures()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["setSource"] = new()
            {
                AccumulateAs = "sources",
                Defaults = new() { ["value"] = new FieldValueDefinition { Static = JsonSerializer.SerializeToElement("dynamic-value") } }
            },
            ["addItem"] = new()
            {
                AccumulateAs = "items",
                Defaults = new()
                {
                    ["wrapper"] = StaticField(new Dictionary<string, object>
                    {
                        ["inner"] = new Dictionary<string, string> { ["from"] = "setSource.value" }
                    })
                }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [
                new StepInvocation { Build = "setSource" },
                new StepInvocation { Build = "addItem" }
            ],
            [new AssertionDefinition { Equal = ["addItem.wrapper.inner", "dynamic-value"] }]);

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
