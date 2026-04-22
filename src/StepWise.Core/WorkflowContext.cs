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
    private readonly Dictionary<Type, List<object>> _accumulated = new();

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
    /// Repeatedly executes a request until <paramref name="until"/> returns true
    /// or <paramref name="timeoutMs"/> elapses, with <paramref name="intervalMs"/> between attempts.
    /// The final successful response is captured under <see cref="WorkflowRequest{TResponse}.StepName"/>.
    /// </summary>
    public async Task<TResponse> PollAsync<TResponse>(
        WorkflowRequest<TResponse> request,
        Func<TResponse, bool> until,
        int intervalMs = 500,
        int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            var response = await ExecuteAsync(request);
            if (until(response)) return response;

            var remaining = timeoutMs - sw.ElapsedMilliseconds;

            if (remaining <= 0)
                throw new WorkflowContextException(
                    $"PollAsync timed out after {timeoutMs}ms waiting for step '{request.StepName}'.");

            var delayMs = Math.Min(intervalMs, remaining);

            await Task.Delay((int)delayMs);
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Resolves the item's field values immediately, stores the resolved dictionary in the
    /// accumulated list for this item type, and returns a typed snapshot of the resolved values.
    /// Resolution happens once at build time — no deferred re-resolution when the request is sent.
    /// </summary>
    public Task<TResponse> BuildAsync<TResponse>(BuildableRequest<TResponse> item)
    {
        var runtimeType = item.GetType();
        if (!_accumulated.TryGetValue(runtimeType, out var list))
        {
            list = new List<object>();
            _accumulated[runtimeType] = list;
        }

        var resolved = FieldValueResolver.ResolveObject(item, this);
        list.Add(resolved);

        var json = System.Text.Json.JsonSerializer.Serialize(resolved);
        var response = System.Text.Json.JsonSerializer.Deserialize<TResponse>(json, _jsonOptions)!;
        return Task.FromResult(response);
    }

    /// <summary>
    /// Returns all accumulated resolved dictionaries for the given item type,
    /// then clears the accumulation. Returns an empty list if nothing has been accumulated.
    /// </summary>
    public List<Dictionary<string, object?>> GetAccumulated<TItem>() where TItem : BuildableRequest
    {
        if (!_accumulated.TryGetValue(typeof(TItem), out var list))
            return [];

        _accumulated.Remove(typeof(TItem));
        return list.Cast<Dictionary<string, object?>>().ToList();
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

    /// <summary>
    /// Returns the raw captured object for a step without type casting.
    /// Used by JSON workflow runners where the response type is not known at compile time.
    /// </summary>
    public object? GetRaw(string stepName)
    {
        if (!_captures.TryGetValue(stepName, out var value))
            throw new WorkflowContextException(
                $"No captured response found for step '{stepName}'. " +
                $"Available steps: [{string.Join(", ", _captures.Keys)}]");
        return value;
    }

    /// <summary>
    /// Captures a raw response under the given step name.
    /// Used by JSON workflow runners where the response type is not known at compile time.
    /// </summary>
    public void CaptureRaw(string stepName, object response)
    {
        _captures[stepName] = response;
    }
}
