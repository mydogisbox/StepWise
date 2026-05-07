using Walkthrough.Core;

namespace Walkthrough.Http;

/// <summary>
/// Base record for requests that execute over HTTP.
/// URL shape (path, query, headers) is declared on the step; this record carries only body fields.
/// </summary>
public abstract record HttpWorkflowRequest<TResponse>(string StepName)
    : WorkflowRequest<TResponse>(StepName);
