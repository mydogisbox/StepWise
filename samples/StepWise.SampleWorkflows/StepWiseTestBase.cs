using StepWise.Core;

namespace StepWise.SampleWorkflows;

/// <summary>
/// Base class for workflow tests. Provides a convenience method for
/// creating a WorkflowContext with named targets.
/// </summary>
public abstract class StepWiseTestBase
{
    protected static WorkflowContext NewContext(Action<WorkflowContext> configure)
    {
        var context = new WorkflowContext();
        configure(context);
        return context;
    }
}
