namespace StepWise.Core;

/// <summary>
/// Thrown when a workflow context operation fails, such as referencing
/// a step that has not yet been executed.
/// </summary>
public class WorkflowContextException(string message) : Exception(message);
