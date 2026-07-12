namespace Binexus.SharedKernel.Results;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Transient,
    Permanent,
}

public sealed record DomainError(string Code, string Message, ErrorKind Kind = ErrorKind.Validation)
{
    public static DomainError Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static DomainError NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static DomainError Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static DomainError Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static DomainError Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
}

public readonly struct Result
{
    private Result(bool isSuccess, DomainError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(DomainError error) => new(false, error);
}

public readonly struct Result<T>
{
    internal Result(T? value, bool isSuccess, DomainError? error)
    {
        Value = value;
        IsSuccess = isSuccess;
        Error = error;
    }

    public T? Value { get; }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }
}

/// <summary>Factory methods for <see cref="Result{T}"/> without static members on generic types.</summary>
public static class ResultFactory
{
    public static Result<T> Ok<T>(T value) => new(value, true, null);

    public static Result<T> Fail<T>(DomainError error) => new(default, false, error);
}
