namespace VietRide.Shared.Kernel.Primitives;

/// Functional result wrapper. Replace `throw` for expected errors; reserve exceptions for truly exceptional.
public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static Error Validation(string field, string msg) => new("VALIDATION_ERROR", $"{field}: {msg}");
    public static Error NotFound(string what) => new("NOT_FOUND", $"{what} not found");
    public static Error Conflict(string msg) => new("CONFLICT", msg);
}

public readonly record struct Result<T>
{
    public T? Value { get; }
    public Error Error { get; }
    public bool IsSuccess => Error == Error.None;
    public bool IsFailure => !IsSuccess;

    private Result(T? value, Error error) { Value = value; Error = error; }

    public static Result<T> Success(T value) => new(value, Error.None);
    public static Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
