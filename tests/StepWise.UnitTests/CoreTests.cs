using StepWise.Core;
using static StepWise.Core.FieldValues;
using Xunit;

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
    [Fact]
    public void Get_ThrowsDescriptiveException_WhenStepNotFound()
    {
        var context = new WorkflowContext();
        // Manually poke a capture in via reflection for test purposes
        var captures = typeof(WorkflowContext)
            .GetField("_captures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(context) as Dictionary<string, object>;
        captures!["login"] = new { Token = "tok" };

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
