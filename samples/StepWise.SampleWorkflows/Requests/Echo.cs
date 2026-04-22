using StepWise.Core;
using StepWise.Http;
using StepWise.Http.Auth;
using static StepWise.Core.FieldValues;

namespace StepWise.SampleWorkflows;

public record EchoHeadersRequest() : WorkflowRequest<Dictionary<string, string>>("echoHeaders", "sample-api");

public class EchoHeadersStep : HttpStep<EchoHeadersRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/echo/headers";
    public override IAuthProvider Auth => NoAuth.Instance;
}

public record EchoHeadersWithStepHeaderRequest() : WorkflowRequest<Dictionary<string, string>>("echoHeadersWithStepHeader", "sample-api");

public class EchoHeadersWithStepHeaderStep : HttpStep<EchoHeadersWithStepHeaderRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/echo/headers";
    public override IAuthProvider Auth => NoAuth.Instance;
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>> { ["x-step-header"] = Static("from-step") };
}

public record EchoHeadersWithFromAuthRequest() : WorkflowRequest<Dictionary<string, string>>("echoHeadersWithFromAuth", "sample-api");

public class EchoHeadersWithFromAuthStep : HttpStep<EchoHeadersWithFromAuthRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/echo/headers";
    public override IAuthProvider Auth => NoAuth.Instance;
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
}
