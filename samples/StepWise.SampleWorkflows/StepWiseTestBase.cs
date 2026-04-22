using System.Reflection;
using StepWise.Core;
using StepWise.Http;
using static StepWise.Core.FieldValues;

namespace StepWise.SampleWorkflows;

public abstract class StepWiseTestBase
{
    private const string SampleApiUrl = "http://localhost:4200";

    private readonly WorkflowContext _context;

    protected StepWiseTestBase()
    {
        _context = new WorkflowContext()
            .WithTarget("sample-api", new HttpTarget(SampleApiUrl, Assembly.GetExecutingAssembly()));
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

    protected Task BuildAsync<TItem>(TItem item) where TItem : BuildableRequest
        => _context.BuildAsync(item);
}
