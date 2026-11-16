namespace Orchestra.Api.Common;

public sealed class ApiException(
    int statusCode,
    string code,
    string message,
    IReadOnlyDictionary<string, string[]>? errors = null,
    bool retryable = false) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
    public bool Retryable { get; } = retryable;
}