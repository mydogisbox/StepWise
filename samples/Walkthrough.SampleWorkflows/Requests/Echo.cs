using Walkthrough.Core;
using Walkthrough.Http;

namespace Walkthrough.SampleWorkflows;

public record EchoHeadersRequest() : WorkflowRequest<Dictionary<string, string>, EchoHeadersRequest>, IWorkflowRequest
{
    public static string StepName => "echoHeaders";
}

public class EchoHeadersStep : HttpStep<EchoHeadersRequest, Dictionary<string, string>, EchoHeadersStep>, IHttpStep
{
    public static HttpMethod Method => HttpMethod.Get;
    public static string     Path   => "/echo/headers";
}

public record EchoHeadersWithStepHeaderRequest() : WorkflowRequest<Dictionary<string, string>, EchoHeadersWithStepHeaderRequest>, IWorkflowRequest
{
    public static string StepName => "echoHeadersWithStepHeader";
}

public class EchoHeadersWithStepHeaderStep : HttpStep<EchoHeadersWithStepHeaderRequest, Dictionary<string, string>, EchoHeadersWithStepHeaderStep>, IHttpStep
{
    public static HttpMethod Method => HttpMethod.Get;
    public static string     Path   => "/echo/headers";

    public override Dictionary<string, string> MapHeaders(Dictionary<string, object?> resolvedFields)
        => new() { ["x-step-header"] = "from-step" };
}
