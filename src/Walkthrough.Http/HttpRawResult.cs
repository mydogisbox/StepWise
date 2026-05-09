namespace Walkthrough.Http;

/// <summary>
/// Returned by ExecuteRawAsync. Carries the HTTP status code and the response body.
/// Body is the response deserialized as TResponse when the body matches that shape;
/// otherwise the raw JSON string.
/// </summary>
public record HttpRawResult(int StatusCode, object? Body);
