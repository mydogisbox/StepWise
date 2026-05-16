namespace Walkthrough.Core;

public abstract record WorkflowRequest<TResponse>;

public abstract record WorkflowRequest<TResponse, TSelf> : WorkflowRequest<TResponse>
    where TSelf : WorkflowRequest<TResponse, TSelf>, IWorkflowRequest;
