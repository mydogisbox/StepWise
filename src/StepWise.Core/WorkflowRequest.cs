namespace StepWise.Core;

/// <summary>
/// Base record for all workflow requests.
/// TResponse is the type returned by executing this request.
/// The StepName is used to capture the response into WorkflowContext.
/// The TargetKey identifies which target in the context to execute against.
/// </summary>
public abstract record WorkflowRequest<TResponse>(string StepName, string TargetKey);
