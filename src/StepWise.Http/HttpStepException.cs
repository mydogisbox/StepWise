namespace StepWise.Http;

/// <summary>
/// Thrown when an HTTP workflow step fails, such as a non-success status code
/// or a missing base URL configuration.
/// </summary>
public class HttpStepException(string message) : Exception(message);
