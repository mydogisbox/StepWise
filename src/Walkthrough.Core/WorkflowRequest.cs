namespace Walkthrough.Core;

/// <summary>
/// Base record for all workflow requests.
/// TResponse is the type returned by executing this request.
/// The StepName is used to capture the response into WorkflowContext.
/// </summary>
public abstract record WorkflowRequest<TResponse>(string StepName);
