using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using StepWise.Core;
using StepWise.Http;
using StepWise.Json;
using static StepWise.Core.FieldValues;
using Xunit;

namespace StepWise.UnitTests;

// ── JSON workflow runner headers ─────────────────────────────────────────────

public class JsonWorkflowRunnerHeaderTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly List<NameValueCollection> _receivedHeaders = [];

    public JsonWorkflowRunnerHeaderTests()
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

                _receivedHeaders.Add(ctx.Request.Headers);

                var bytes = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"1\"}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
    }

    public void Dispose() => _listener.Stop();

    private static FieldValueDefinition F(string value) =>
        new() { Static = JsonSerializer.SerializeToElement(value) };

    private Dictionary<string, StepDefinition> StepDef(
        Dictionary<string, FieldValueDefinition>? headers = null) => new()
    {
        ["doThing"] = new StepDefinition
        {
            Target = "api", Method = "POST", Path = "/thing",
            Headers = headers
        }
    };

    private Dictionary<string, TargetDefinition> Target(
        Dictionary<string, FieldValueDefinition>? headers = null) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["api"] = new TargetDefinition { BaseUrl = $"http://127.0.0.1:{_port}", Headers = headers }
        };

    private static WorkflowDefinition Workflow(
        Dictionary<string, FieldValueDefinition>? invocationHeaders = null) =>
        new("test", [new StepInvocation { Step = "doThing", Headers = invocationHeaders }]);

    [Fact]
    public async Task TargetHeaders_SentWithRequest()
    {
        await JsonWorkflowRunner.RunAsync(Workflow(), StepDef(),
            Target(new() { ["X-Tenant-Id"] = F("acme") }));

        Assert.Equal("acme", _receivedHeaders[0]["X-Tenant-Id"]);
    }

    [Fact]
    public async Task StepHeaders_SentWithRequest()
    {
        await JsonWorkflowRunner.RunAsync(Workflow(),
            StepDef(new() { ["X-Api-Version"] = F("2") }),
            Target());

        Assert.Equal("2", _receivedHeaders[0]["X-Api-Version"]);
    }

    [Fact]
    public async Task InvocationHeaders_SentWithRequest()
    {
        await JsonWorkflowRunner.RunAsync(
            Workflow(new() { ["X-Request-Id"] = F("req-123") }),
            StepDef(), Target());

        Assert.Equal("req-123", _receivedHeaders[0]["X-Request-Id"]);
    }

    [Fact]
    public async Task StepWinsOverTarget_ForSameKey()
    {
        await JsonWorkflowRunner.RunAsync(Workflow(),
            StepDef(new() { ["X-Level"] = F("step") }),
            Target(new() { ["X-Level"] = F("target") }));

        Assert.Equal("step", _receivedHeaders[0]["X-Level"]);
    }

    [Fact]
    public async Task InvocationWinsOverStep_ForSameKey()
    {
        await JsonWorkflowRunner.RunAsync(
            Workflow(new() { ["X-Level"] = F("invocation") }),
            StepDef(new() { ["X-Level"] = F("step") }),
            Target());

        Assert.Equal("invocation", _receivedHeaders[0]["X-Level"]);
    }

    [Fact]
    public async Task AllLevels_MergedTogether()
    {
        await JsonWorkflowRunner.RunAsync(
            Workflow(new() { ["X-Invocation"] = F("yes") }),
            StepDef(new() { ["X-Step"] = F("yes") }),
            Target(new() { ["X-Target"] = F("yes") }));

        Assert.Equal("yes", _receivedHeaders[0]["X-Target"]);
        Assert.Equal("yes", _receivedHeaders[0]["X-Step"]);
        Assert.Equal("yes", _receivedHeaders[0]["X-Invocation"]);
    }
}

// ── C# HttpTarget headers ────────────────────────────────────────────────────

public class HttpTargetHeaderTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly List<NameValueCollection> _receivedHeaders = [];

    // One request/step pair per scenario to avoid assembly-scan ambiguity.
    private record FakeResponse(string Id);

    private record WithHeadersRequest() : WorkflowRequest<FakeResponse>("doThing", "api");
    private class WithHeadersStep : HttpStep<WithHeadersRequest, FakeResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string Path => "/thing";
    }

    private record StepLevelHeaderRequest() : WorkflowRequest<FakeResponse>("doThing", "api");
    private class StepLevelHeaderStep : HttpStep<StepLevelHeaderRequest, FakeResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string Path => "/thing";
        public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
            new Dictionary<string, IFieldValue<string>> { ["X-Api-Version"] = Static("2") };
    }

    private record PerRequestHeaderRequest() : WorkflowRequest<FakeResponse>("doThing", "api");
    private class PerRequestHeaderStep : HttpStep<PerRequestHeaderRequest, FakeResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string Path => "/thing";
    }

    private record StepVsTargetRequest() : WorkflowRequest<FakeResponse>("doThing", "api");
    private class StepVsTargetStep : HttpStep<StepVsTargetRequest, FakeResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string Path => "/thing";
        public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
            new Dictionary<string, IFieldValue<string>> { ["X-Level"] = Static("step") };
    }

    private record RequestVsStepRequest() : WorkflowRequest<FakeResponse>("doThing", "api");
    private class RequestVsStepStep : HttpStep<RequestVsStepRequest, FakeResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string Path => "/thing";
        public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
            new Dictionary<string, IFieldValue<string>> { ["X-Level"] = Static("step") };
    }

    public HttpTargetHeaderTests()
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

                _receivedHeaders.Add(ctx.Request.Headers);

                var bytes = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"1\"}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
    }

    public void Dispose() => _listener.Stop();

    private HttpTarget Target() =>
        new($"http://127.0.0.1:{_port}", GetType().Assembly);

    private WorkflowContext Context(HttpTarget target) =>
        new WorkflowContext().WithTarget("api", target);

    [Fact]
    public async Task WithHeaders_SentWithRequest()
    {
        var target = Target().WithHeaders(new Dictionary<string, IFieldValue<string>>
        {
            ["X-Tenant-Id"] = Static("acme")
        });
        await Context(target).ExecuteAsync(new WithHeadersRequest());

        Assert.Equal("acme", _receivedHeaders[0]["X-Tenant-Id"]);
    }

    [Fact]
    public async Task StepHeaders_SentWithRequest()
    {
        await Context(Target()).ExecuteAsync(new StepLevelHeaderRequest());

        Assert.Equal("2", _receivedHeaders[0]["X-Api-Version"]);
    }

    [Fact]
    public async Task PerRequestHeaders_SentWithRequest()
    {
        var request = new PerRequestHeaderRequest() with
        {
            Headers = new Dictionary<string, IFieldValue<string>> { ["X-Request-Id"] = Static("req-123") }
        };
        await Context(Target()).ExecuteAsync(request);

        Assert.Equal("req-123", _receivedHeaders[0]["X-Request-Id"]);
    }

    [Fact]
    public async Task StepWinsOverTarget_ForSameKey()
    {
        var target = Target().WithHeaders(new Dictionary<string, IFieldValue<string>>
        {
            ["X-Level"] = Static("target")
        });
        await Context(target).ExecuteAsync(new StepVsTargetRequest());

        Assert.Equal("step", _receivedHeaders[0]["X-Level"]);
    }

    [Fact]
    public async Task RequestWinsOverStep_ForSameKey()
    {
        var request = new RequestVsStepRequest() with
        {
            Headers = new Dictionary<string, IFieldValue<string>> { ["X-Level"] = Static("request") }
        };
        await Context(Target()).ExecuteAsync(request);

        Assert.Equal("request", _receivedHeaders[0]["X-Level"]);
    }

    // Auth expressed as a header using From — equivalent to auth: bearer.
    // Uses a separate in-memory target for login so only one HTTP request hits the listener.
    private record FakeTokenResponse(string Token);
    private record FromAuthRequest() : WorkflowRequest<FakeResponse>("doThing", "api");
    private class FromAuthStep : HttpStep<FromAuthRequest, FakeResponse>
    {
        public override HttpMethod Method => HttpMethod.Post;
        public override string Path => "/thing";
        public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
            new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => $"Bearer {ctx.Get<FakeTokenResponse>("login").Token}")
            };
    }

    private record FakeLoginRequest() : WorkflowRequest<FakeTokenResponse>("login", "login-api");
    private class FakeLoginTarget(FakeTokenResponse response) : ITarget
    {
        public Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request, WorkflowContext context)
            => Task.FromResult((TResponse)(object)response);
    }

    [Fact]
    public async Task FromInHeader_ConstructsBearerToken()
    {
        // Login is handled in-memory; only the doThing request hits the HTTP listener.
        var context = new WorkflowContext()
            .WithTarget("login-api", new FakeLoginTarget(new FakeTokenResponse("my-token")))
            .WithTarget("api", Target());

        await context.ExecuteAsync(new FakeLoginRequest());
        await context.ExecuteAsync(new FromAuthRequest());

        Assert.Equal("Bearer my-token", _receivedHeaders[0]["Authorization"]);
    }
}
