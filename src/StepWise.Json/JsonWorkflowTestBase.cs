namespace StepWise.Json;

/// <summary>
/// Base class for xUnit tests that run .workflow.json files.
/// Thin wrapper over JsonWorkflowRunner — no assembly scanning, no C# type resolution.
/// </summary>
public abstract class JsonWorkflowTestBase
{
    /// <summary>
    /// Paths to .requests.json files, resolved relative to each workflow file's directory.
    /// </summary>
    protected virtual IReadOnlyList<string> RequestPaths => [];

    protected virtual string? TargetsPath => null;

    protected async Task RunWorkflowAsync(string workflowPath)
    {
        var result = await JsonWorkflowRunner.RunAsync(workflowPath, RequestPaths, TargetsPath);
        result.ThrowIfFailed();
    }
}
