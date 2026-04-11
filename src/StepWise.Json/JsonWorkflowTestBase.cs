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

    /// <summary>
    /// Paths to .workflow.json files to pre-load as named sub-workflows.
    /// Referenced in workflow files by their <c>name</c> field rather than a file path.
    /// </summary>
    protected virtual IReadOnlyList<string> SharedWorkflowPaths => [];

    protected async Task RunWorkflowAsync(string workflowPath)
    {
        var result = await JsonWorkflowRunner.RunAsync(workflowPath, RequestPaths, TargetsPath, SharedWorkflowPaths);
        result.ThrowIfFailed();
    }
}
