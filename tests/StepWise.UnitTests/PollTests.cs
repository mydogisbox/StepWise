using System.Net;
using System.Text.Json;
using StepWise.Core;
using StepWise.Json;
using Xunit;

namespace StepWise.UnitTests;

// ── WorkflowContext.PollAsync ────────────────────────────────────────────────

public class WorkflowContextPollTests
{
    private record StatusResponse(string Status);
    private record GetStatusRequest() : WorkflowRequest<StatusResponse>("getStatus", "test-api");

    private class FakeTarget : ITarget
    {
        private readonly Queue<object> _responses = new();
        public int CallCount { get; private set; }

        public void Enqueue<T>(T response) => _responses.Enqueue(response!);

        public Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request, WorkflowContext context)
        {
            CallCount++;
            return Task.FromResult((TResponse)_responses.Dequeue());
        }
    }

    [Fact]
    public async Task ReturnsOnFirstAttempt_WhenConditionImmediatelyMet()
    {
        var fake = new FakeTarget();
        fake.Enqueue(new StatusResponse("Completed"));

        var context = new WorkflowContext().WithTarget("test-api", fake);
        var result = await context.PollAsync(new GetStatusRequest(), r => r.Status == "Completed");

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Retries_UntilConditionMet()
    {
        var fake = new FakeTarget();
        fake.Enqueue(new StatusResponse("Pending"));
        fake.Enqueue(new StatusResponse("Pending"));
        fake.Enqueue(new StatusResponse("Completed"));

        var context = new WorkflowContext().WithTarget("test-api", fake);
        var result = await context.PollAsync(
            new GetStatusRequest(), r => r.Status == "Completed", intervalMs: 1);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(3, fake.CallCount);
    }

    [Fact]
    public async Task ThrowsWorkflowContextException_OnTimeout()
    {
        var fake = new FakeTarget();
        for (var i = 0; i < 10; i++) fake.Enqueue(new StatusResponse("Pending"));

        var context = new WorkflowContext().WithTarget("test-api", fake);
        var ex = await Assert.ThrowsAsync<WorkflowContextException>(() =>
            context.PollAsync(new GetStatusRequest(), r => r.Status == "Completed",
                intervalMs: 10, timeoutMs: 50));

        Assert.Contains("getStatus", ex.Message);
    }

    [Fact]
    public async Task CapturesFinalResponse_UnderStepName()
    {
        var fake = new FakeTarget();
        fake.Enqueue(new StatusResponse("Pending"));
        fake.Enqueue(new StatusResponse("Completed"));

        var context = new WorkflowContext().WithTarget("test-api", fake);
        await context.PollAsync(new GetStatusRequest(), r => r.Status == "Completed", intervalMs: 1);

        Assert.Equal("Completed", context.Get<StatusResponse>("getStatus").Status);
    }
}

// ── JsonWorkflowRunner poll step ─────────────────────────────────────────────

public class JsonWorkflowRunnerPollTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly int[] _callCount = new int[1];

    public JsonWorkflowRunnerPollTests()
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        _port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
    }

    public void Dispose() => _listener.Stop();

    private void ServeSequence(params string[] responses)
    {
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }

                var idx = Math.Min(_callCount[0]++, responses.Length - 1);
                var bytes = System.Text.Encoding.UTF8.GetBytes(responses[idx]);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
    }

    private (WorkflowDefinition, Dictionary<string, StepDefinition>) BuildWorkflow(StepInvocation step)
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["getStatus"] = new StepDefinition { Target = "api", Method = "GET", Path = "/status" }
        };
        var workflow = new WorkflowDefinition(
            Name: "PollTest",
            Steps: [step]);
        return (workflow, stepDefs);
    }

    private Dictionary<string, TargetDefinition> TestTargets =>
        new(StringComparer.OrdinalIgnoreCase) { ["api"] = $"http://127.0.0.1:{_port}" };

    [Fact]
    public async Task Poll_ResolvesWhenConditionEventuallyMet()
    {
        ServeSequence(
            "{\"status\":\"Pending\"}",
            "{\"status\":\"Pending\"}",
            "{\"status\":\"Completed\"}");

        var (workflow, stepDefs) = BuildWorkflow(new StepInvocation
        {
            Poll = "getStatus",
            Until = new AssertionDefinition { Equal = ["$getStatus.status", "Completed"] },
            IntervalMs = 1,
            TimeoutMs = 5000
        });

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, TestTargets);

        Assert.True(result.Passed);
        Assert.Equal(3, _callCount[0]);
    }

    [Fact]
    public async Task Poll_WithNoUntil_ExecutesOnceAndSucceeds()
    {
        ServeSequence("{\"status\":\"Completed\"}");

        var (workflow, stepDefs) = BuildWorkflow(new StepInvocation
        {
            Poll = "getStatus",
            IntervalMs = 1,
            TimeoutMs = 5000
        });

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, TestTargets);

        Assert.True(result.Passed);
        Assert.Equal(1, _callCount[0]);
    }

    [Fact]
    public async Task Poll_ThrowsJsonWorkflowException_OnTimeout()
    {
        ServeSequence("{\"status\":\"Pending\"}");

        var (workflow, stepDefs) = BuildWorkflow(new StepInvocation
        {
            Poll = "getStatus",
            Until = new AssertionDefinition { Equal = ["$getStatus.status", "Completed"] },
            IntervalMs = 1,
            TimeoutMs = 50
        });

        var ex = await Assert.ThrowsAsync<JsonWorkflowException>(() =>
            JsonWorkflowRunner.RunAsync(workflow, stepDefs, TestTargets));

        Assert.Contains("getStatus", ex.Message);
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task Poll_CapturesFinalResponse_ForSubsequentAssertions()
    {
        ServeSequence(
            "{\"status\":\"Pending\"}",
            "{\"status\":\"Completed\"}");

        var (workflow, stepDefs) = BuildWorkflow(new StepInvocation
        {
            Poll = "getStatus",
            Until = new AssertionDefinition { Equal = ["$getStatus.status", "Completed"] },
            IntervalMs = 1,
            TimeoutMs = 5000
        });

        var withAssertion = workflow with
        {
            Assertions = [new AssertionDefinition { Equal = ["$getStatus.status", "Completed"] }]
        };

        var result = await JsonWorkflowRunner.RunAsync(withAssertion, stepDefs, TestTargets);

        Assert.True(result.Passed);
        Assert.Empty(result.AssertionErrors);
    }
}

public class CaptureRequestAsTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;

    public CaptureRequestAsTests()
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        _port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();

        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }

                var bytes = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"resp-1\"}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
    }

    public void Dispose() => _listener.Stop();

    private Dictionary<string, TargetDefinition> TestTargets =>
        new(StringComparer.OrdinalIgnoreCase) { ["api"] = $"http://127.0.0.1:{_port}" };

    [Fact]
    public async Task CaptureRequestAs_StoresResolvedFields()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["createItem"] = new()
            {
                Target = "api", Method = "POST", Path = "/items",
                Defaults = new()
                {
                    ["name"]  = new FieldValueDefinition { Static = JsonSerializer.SerializeToElement("Widget") },
                    ["price"] = new FieldValueDefinition { Static = JsonSerializer.SerializeToElement(9.99) }
                }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Step = "createItem", CaptureRequestAs = "itemRequest" }],
            [
                new AssertionDefinition { Equal = ["$itemRequest.name", "Widget"] },
                new AssertionDefinition { NotEmpty = "$itemRequest.price" }
            ]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, TestTargets);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task CaptureRequestAs_IsAvailableToSubsequentSteps()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["createItem"] = new()
            {
                Target = "api", Method = "POST", Path = "/items",
                Defaults = new()
                {
                    ["name"] = new FieldValueDefinition { Static = JsonSerializer.SerializeToElement("Widget") }
                }
            },
            ["createOrder"] = new()
            {
                Target = "api", Method = "POST", Path = "/orders",
                Defaults = new()
                {
                    ["itemName"] = new FieldValueDefinition { From = "itemRequest.name" }
                }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [
                new StepInvocation { Step = "createItem",  CaptureRequestAs = "itemRequest" },
                new StepInvocation { Step = "createOrder" }
            ],
            [new AssertionDefinition { Equal = ["$itemRequest.name", "Widget"] }]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, TestTargets);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }

    [Fact]
    public async Task CaptureRequestAs_DoesNotAffectResponseCapture()
    {
        var stepDefs = new Dictionary<string, StepDefinition>
        {
            ["createItem"] = new()
            {
                Target = "api", Method = "POST", Path = "/items",
                Defaults = new()
                {
                    ["name"] = new FieldValueDefinition { Static = JsonSerializer.SerializeToElement("Widget") }
                }
            }
        };

        var workflow = new WorkflowDefinition(
            "test",
            [new StepInvocation { Step = "createItem", CaptureRequestAs = "itemRequest" }],
            [
                new AssertionDefinition { Equal = ["$createItem.id", "resp-1"] },
                new AssertionDefinition { Equal = ["$itemRequest.name", "Widget"] }
            ]);

        var result = await JsonWorkflowRunner.RunAsync(workflow, stepDefs, TestTargets);
        Assert.True(result.Passed, string.Join(", ", result.AssertionErrors));
    }
}
