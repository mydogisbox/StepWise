namespace StepWise.Core;

/// <summary>
/// A pure data record that accumulates into the WorkflowContext
/// without making an API call. Use context.BuildAsync() to add instances,
/// then reference them via context.GetAccumulated&lt;T&gt;() in a From(...) lookup.
/// </summary>
public abstract record BuildableRequest;
