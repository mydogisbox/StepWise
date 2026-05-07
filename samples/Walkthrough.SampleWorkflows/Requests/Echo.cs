using Walkthrough.Core;
using Walkthrough.Http;
using static Walkthrough.Core.FieldValues;

namespace Walkthrough.SampleWorkflows;

public record EchoHeadersRequest() : HttpWorkflowRequest<Dictionary<string, string>>("echoHeaders");

public class EchoHeadersStep : HttpStep<EchoHeadersRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/echo/headers";
}

public record EchoHeadersWithStepHeaderRequest() : HttpWorkflowRequest<Dictionary<string, string>>("echoHeadersWithStepHeader");

public class EchoHeadersWithStepHeaderStep : HttpStep<EchoHeadersWithStepHeaderRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/echo/headers";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>> { ["x-step-header"] = Static("from-step") };
}

public record EchoHeadersWithFromAuthRequest() : HttpWorkflowRequest<Dictionary<string, string>>("echoHeadersWithFromAuth");

public class EchoHeadersWithFromAuthStep : HttpStep<EchoHeadersWithFromAuthRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string Path => "/echo/headers";
    public override IReadOnlyDictionary<string, IFieldValue<string>> Headers { get; } =
        new Dictionary<string, IFieldValue<string>>
        {
            ["authorization"] = From(ctx => $"Bearer {ctx.Get<LoginResponse>("login").Token}")
        };
}
