namespace Walkthrough.Core;

/// <summary>
/// Marker base for buildable request items. Extend BuildableRequest&lt;TResponse&gt; instead.
/// </summary>
public abstract record BuildableRequest
{
    /// <summary>
    /// The type used as the accumulation key in WorkflowContext.
    /// Defaults to the runtime type. Override on a polymorphic base to accumulate all subtypes together:
    /// <c>public override Type AccumulationKey =&gt; typeof(MyBaseItem);</c>
    /// </summary>
    public virtual Type AccumulationKey => GetType();

    /// <summary>
    /// The key used to capture the resolved snapshot in WorkflowContext after BuildAsync.
    /// Defaults to the runtime type name. Override when the type name is not a suitable key.
    /// </summary>
    public virtual string BuildableName => GetType().Name;
}

/// <summary>
/// A pure data record that accumulates into the WorkflowContext without making an API call.
/// TResponse is the resolved snapshot type returned by BuildAsync — define it as a plain record
/// with the same properties. Use runner.BuildAsync() to add instances and reference the resolved
/// result directly; use context.GetAccumulated&lt;TItem&gt;() in a From(...) lookup to pass the
/// accumulated list to a request.
/// </summary>
public abstract record BuildableRequest<TResponse> : BuildableRequest;
