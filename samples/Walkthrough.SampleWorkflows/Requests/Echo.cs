using Walkthrough.Http;
using static Walkthrough.Core.FieldValues;

namespace Walkthrough.SampleWorkflows;

public record EchoHeadersRequest() : HttpWorkflowRequest<Dictionary<string, string>>("echoHeaders");

public class EchoHeadersStep : HttpStep<EchoHeadersRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string     Path   => "/echo/headers";
}

public record EchoHeadersWithStepHeaderRequest() : HttpWorkflowRequest<Dictionary<string, string>>("echoHeadersWithStepHeader");

public class EchoHeadersWithStepHeaderStep : HttpStep<EchoHeadersWithStepHeaderRequest, Dictionary<string, string>>
{
    public override HttpMethod Method => HttpMethod.Get;
    public override string     Path   => "/echo/headers";

    public override Dictionary<string, string> MapHeaders(Dictionary<string, object?> resolvedFields)
        => new() { ["x-step-header"] = "from-step" };
}
