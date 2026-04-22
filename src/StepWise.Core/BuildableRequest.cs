namespace StepWise.Core;

/// <summary>
/// Marker base for buildable request items. Extend BuildableRequest&lt;TResponse&gt; instead.
/// </summary>
public abstract record BuildableRequest;

/// <summary>
/// A pure data record that accumulates into the WorkflowContext without making an API call.
/// TResponse is the resolved snapshot type returned by BuildAsync — define it as a plain record
/// with the same properties. Use context.BuildAsync() to add instances and reference the resolved
/// result directly; use context.GetAccumulated&lt;TItem&gt;() in a From(...) lookup to pass the
/// accumulated list to a request.
/// </summary>
public abstract record BuildableRequest<TResponse> : BuildableRequest;
