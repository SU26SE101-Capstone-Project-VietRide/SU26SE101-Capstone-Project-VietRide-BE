namespace VietRide.Shared.Application.Exceptions;

/// Mapped to HTTP 422 by ApiResponseExceptionFilter. Carries field-level errors.
public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(string message, IReadOnlyList<ValidationError>? errors = null)
        : base(message)
    {
        Errors = errors ?? Array.Empty<ValidationError>();
    }
}

public sealed record ValidationError(string Field, string Message);

/// Mapped to HTTP 404.
public sealed class NotFoundException : Exception
{
    public string EntityName { get; }
    public object Id { get; }

    public NotFoundException(string entityName, object id)
        : base($"{entityName} with id '{id}' not found")
    {
        EntityName = entityName;
        Id = id;
    }
}

/// Mapped to HTTP 409.
public sealed class ConflictException : Exception
{
    public string ErrorCode { get; }

    public ConflictException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

/// Mapped to HTTP 403.
public sealed class ForbiddenException : Exception
{
    public string ErrorCode { get; }

    public ForbiddenException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

/// Mapped to HTTP 401 with a specific error code (e.g. AUTH_INVALID_CREDENTIALS, AUTH_TOKEN_INVALID).
public sealed class UnauthorizedException : Exception
{
    public string ErrorCode { get; }

    public UnauthorizedException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

/// Mapped to HTTP 400 with a specific error code (e.g. AUTH_OTP_INVALID, AUTH_OTP_EXPIRED).
public sealed class BadRequestException : Exception
{
    public string ErrorCode { get; }

    public BadRequestException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

/// Mapped to HTTP 429 with a specific error code (e.g. AUTH_OTP_RATE_LIMIT_EXCEEDED).
public sealed class TooManyRequestsException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
