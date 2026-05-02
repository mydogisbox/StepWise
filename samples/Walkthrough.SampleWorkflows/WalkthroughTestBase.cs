using Walkthrough.Core;
using Walkthrough.Http;
using static Walkthrough.Core.FieldValues;

namespace Walkthrough.SampleWorkflows;

public abstract class WalkthroughTestBase
{
    private const string SampleApiUrl = "http://localhost:4200";

    private readonly WorkflowContext _context;

    protected WalkthroughTestBase()
    {
        _context = new WorkflowContext()
            .WithTargetResolver(_ => new HttpTarget(SampleApiUrl)
                .Register(new LoginStep())
                .Register(new CreateUserStep())
                .Register(new UpdateUserAddressStep())
                .Register(new GetUsersByRoleStep())
                .Register(new CreateOrderStep())
                .Register(new GetOrderStep())
                .Register(new EchoHeadersStep())
                .Register(new EchoHeadersWithStepHeaderStep())
                .Register(new EchoHeadersWithFromAuthStep()));
    }

    protected Task<TResponse> ExecuteAsync<TResponse>(
        WorkflowRequest<TResponse> request,
        Dictionary<string, string>? query = null,
        Dictionary<string, string>? pathParams = null,
        Dictionary<string, string>? headers = null)
    {
        if (query is not null)
            request = request with
            {
                Query = query.ToDictionary(kv => kv.Key, kv => (IFieldValue<string>)Static(kv.Value))
            };
        if (pathParams is not null)
            request = request with
            {
                PathParams = pathParams.ToDictionary(kv => kv.Key, kv => (IFieldValue<string>)Static(kv.Value))
            };
        if (headers is not null)
            request = request with
            {
                Headers = headers.ToDictionary(kv => kv.Key, kv => (IFieldValue<string>)Static(kv.Value))
            };
        return _context.ExecuteAsync(request);
    }

    protected Task<TResponse> BuildAsync<TResponse>(BuildableRequest<TResponse> item)
        => _context.BuildAsync(item);
}
