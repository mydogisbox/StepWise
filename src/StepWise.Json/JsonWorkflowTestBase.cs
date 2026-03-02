namespace StepWise.Json;

/// <summary>
/// Base class for xUnit tests that run .workflow.json files.
/// Thin wrapper over JsonWorkflowRunner — no assembly scanning, no C# type resolution.
/// </summary>
public abstract class JsonWorkflowTestBase
{
    protected abstract string TargetsPath { get; }

    protected async Task RunWorkflowAsync(string workflowPath)
    {
        var result = await JsonWorkflowRunner.RunAsync(workflowPath, TargetsPath);
        result.ThrowIfFailed();
    }
}
