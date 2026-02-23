namespace StepWise.Core;

/// <summary>
/// Carries shared state across all steps in a workflow execution.
/// Holds named targets (URL + protocol) and captures step responses
/// for use by subsequent steps via From(...).
/// </summary>
public class WorkflowContext
{
    private readonly Dictionary<string, ITarget> _targets = new();
    private readonly Dictionary<string, object> _captures = new();

    /// <summary>
    /// Registers a named target — a combination of location and protocol.
    /// </summary>
    public WorkflowContext WithTarget(string key, ITarget target)
    {
        _targets[key] = target;
        return this;
    }

    /// <summary>
    /// Executes a request against its registered target, captures the response,
    /// and returns it as a typed result.
    /// </summary>
    public async Task<TResponse> ExecuteAsync<TResponse>(WorkflowRequest<TResponse> request)
    {
        if (!_targets.TryGetValue(request.TargetKey, out var target))
            throw new WorkflowContextException(
                $"No target registered for key '{request.TargetKey}'. " +
                $"Available targets: [{string.Join(", ", _targets.Keys)}]");

        var response = await target.ExecuteAsync(request, this);
        _captures[request.StepName] = response!;
        return response;
    }

    /// <summary>
    /// Retrieves the captured response of a previous step by step name.
    /// </summary>
    public T Get<T>(string stepName)
    {
        if (!_captures.TryGetValue(stepName, out var value))
            throw new WorkflowContextException(
                $"No captured response found for step '{stepName}'. " +
                $"Ensure the step has been executed before referencing its output. " +
                $"Available steps: [{string.Join(", ", _captures.Keys)}]");

        if (value is not T typed)
            throw new WorkflowContextException(
                $"Captured response for step '{stepName}' is of type '{value.GetType().Name}', " +
                $"not '{typeof(T).Name}'.");

        return typed;
    }

    /// <summary>
    /// Returns true if a response has been captured for the given step name.
    /// </summary>
    public bool HasCapture(string stepName) => _captures.ContainsKey(stepName);
}
