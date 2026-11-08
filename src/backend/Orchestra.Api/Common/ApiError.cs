namespace Orchestra.Api.Common;

public sealed record ApiError(string Code, string Message, string RequestId, bool Retryable = false);
