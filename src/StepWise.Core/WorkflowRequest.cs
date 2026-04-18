namespace StepWise.Core;

/// <summary>
/// Base record for all workflow requests.
/// TResponse is the type returned by executing this request.
/// The StepName is used to capture the response into WorkflowContext.
/// The TargetKey identifies which target in the context to execute against.
/// </summary>
public abstract record WorkflowRequest<TResponse>(string StepName, string TargetKey)
{
    /// <summary>
    /// Values substituted into <c>{placeholder}</c> segments of the step's path.
    /// Never sent in the request body.
    /// </summary>
    public virtual IReadOnlyDictionary<string, IFieldValue<string>> PathParams { get; init; }
        = new Dictionary<string, IFieldValue<string>>();

    /// <summary>
    /// Key-value pairs appended to the URL as a query string (<c>?key=value&amp;…</c>).
    /// Overrides matching keys from the step's <c>Query</c> defaults.
    /// </summary>
    public virtual IReadOnlyDictionary<string, IFieldValue<string>> Query { get; init; }
        = new Dictionary<string, IFieldValue<string>>();
}
