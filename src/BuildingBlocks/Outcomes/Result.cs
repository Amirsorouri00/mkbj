namespace EwaysVNext.Outcomes;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null && error != Error.None)
            throw new InvalidOperationException(ResultMessages.SuccessWithError);
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException(ResultMessages.FailureWithoutError);
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result Failure(string code, string message) => new(false, new Error(code, message));
    public static Result<T> Success<T>(T value) => new(value, true, null!);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

public class Result<T> : Result
{
    public T? Value { get; }
    internal Result(T? value, bool isSuccess, Error error) : base(isSuccess, error) => Value = value;
}

/// <summary>
/// <paramref name="Retryable"/> tells the caller whether trying the same request again could
/// succeed. It is true only for failures that are genuinely temporary — a rate limit, a timeout, a
/// transient database fault — because a client that retries a permanent failure just multiplies
/// load on something already broken.
/// </summary>
public sealed record Error(string Code, string Message, int HttpStatus, bool Retryable = false)
{
    public Error(string code, string message)
        : this(code, message, SuggestHttpStatus(code)) { }

    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NotFound = new("NOT_FOUND", ErrorMessages.NotFound, 404);
    public static readonly Error Conflict = new("CONFLICT", ErrorMessages.Conflict, 409);
    public static readonly Error Validation = new("VALIDATION_FAILED", ErrorMessages.Validation);
    public static readonly Error RateLimited = new("RATE_LIMITED", ErrorMessages.RateLimited, 429, true);
    public static readonly Error ServiceUnavailable = new("SERVICE_UNAVAILABLE", ErrorMessages.ServiceUnavailable, 503, true);
    public static readonly Error Unauthorized = new("UNAUTHORIZED", ErrorMessages.Unauthorized, 401);
    public static readonly Error Forbidden = new("FORBIDDEN", ErrorMessages.Forbidden, 403);
    public static readonly Error Internal = new("INTERNAL", ErrorMessages.Internal, 500);
    public static readonly Error ConcurrencyConflict = new("CONCURRENCY_CONFLICT", ErrorMessages.ConcurrencyConflict, 409);
    public static readonly Error PersistenceConflict = new("PERSISTENCE_CONFLICT", ErrorMessages.PersistenceConflict, 409);

    public static int SuggestHttpStatus(string code) => code switch
    {
        var c when c.EndsWith("NOT_FOUND", StringComparison.Ordinal) => 404,
        var c when c.EndsWith("CONFLICT", StringComparison.Ordinal)
                || c.EndsWith("TAKEN", StringComparison.Ordinal)
                || c.Contains("DUPLICATE", StringComparison.Ordinal) => 409,
        var c when c.EndsWith("SUSPENDED", StringComparison.Ordinal)
                || c.Contains("FORBIDDEN", StringComparison.Ordinal) => 403,
        var c when c.StartsWith("INTERNAL", StringComparison.Ordinal) => 500,
        _ => 400,
    };
}
