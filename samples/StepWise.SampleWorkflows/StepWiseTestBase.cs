using System.Reflection;
using StepWise.Core;
using StepWise.Http;

namespace StepWise.SampleWorkflows;

/// <summary>
/// Base class for workflow tests. Exposes ExecuteAsync and BuildAsync directly,
/// delegating to an internal WorkflowContext so tests stay uncluttered.
/// </summary>
public abstract class StepWiseTestBase
{
    private const string SampleApiUrl = "http://localhost:5000";

    private readonly WorkflowContext _context;

    protected StepWiseTestBase()
    {
        _context = new WorkflowContext()
            .WithTarget("sample-api", new HttpTarget(SampleApiUrl, Assembly.GetExecutingAssembly()));
    }

    protected Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request)
        => _context.ExecuteAsync(request);

    protected Task BuildAsync<TItem>(TItem item) where TItem : BuildableRequest
        => _context.BuildAsync(item);
}
