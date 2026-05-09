using Walkthrough.Core;
using Walkthrough.Http;
using static Walkthrough.Core.FieldValues;

namespace Walkthrough.SampleWorkflows;

public abstract class WalkthroughTestBase
{
    private const string SampleApiUrl = "http://localhost:4200";
    private readonly WorkflowRunner _runner;

    protected WalkthroughTestBase()
    {
        var context = new WorkflowContext();

        var loginTarget = new HttpTarget(SampleApiUrl)
            .Register(new LoginStep());

        var apiTarget = new HttpTarget(SampleApiUrl)
            .Register(new CreateUserStep())
            .Register(new UpdateUserAddressStep())
            .Register(new GetUsersByRoleStep())
            .Register(new CreateOrderStep())
            .Register(new GetOrderStep())
            .Register(new EchoHeadersStep())
            .Register(new EchoHeadersWithStepHeaderStep())
            .WithHeaders(new Dictionary<string, IFieldValue<string>>
            {
                ["Authorization"] = From(ctx => ctx.HasCapture("login")
                    ? $"Bearer {ctx.Get<LoginResponse>("login").Token}"
                    : "")
            });

        _runner = new WorkflowRunner(context,
            stepName => stepName == "login" ? (ITarget)loginTarget : apiTarget);
    }

    protected Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request)
        => _runner.ExecuteAsync(request);

    protected Task<object> ExecuteRawAsync<TResponse>(WorkflowRequest<TResponse> request)
        => _runner.ExecuteRawAsync(request);

    protected Task<TResponse> BuildAsync<TResponse>(BuildableRequest<TResponse> item)
        => _runner.BuildAsync(item);
}
