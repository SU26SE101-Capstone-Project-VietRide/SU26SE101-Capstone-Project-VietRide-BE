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

/// Mapped to HTTP 422 by ApiResponseExceptionFilter. Carries a caller-supplied UPPER_SNAKE_CASE error code and field-level errors.
public sealed class CodedValidationException : Exception
{
    public string ErrorCode { get; }
    public IReadOnlyList<ValidationError> Errors { get; }

    public CodedValidationException(string errorCode, string message, IReadOnlyList<ValidationError>? errors = null)
        : base(message)
    {
        if (!IsUpperSnakeCase(errorCode))
        {
            throw new ArgumentException("Error code must be non-empty UPPER_SNAKE_CASE.", nameof(errorCode));
        }

        ErrorCode = errorCode;
        Errors = errors ?? Array.Empty<ValidationError>();
    }

    private static bool IsUpperSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var previousWasUnderscore = true;
        foreach (var c in value)
        {
            if (c == '_')
            {
                if (previousWasUnderscore)
                {
                    return false;
                }

                previousWasUnderscore = true;
                continue;
            }

            if (c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9'))
            {
                return false;
            }

            previousWasUnderscore = false;
        }

        return !previousWasUnderscore;
    }
}

/// Mapped to HTTP 404 with the backwards-compatible RESOURCE_NOT_FOUND error code.
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

/// Mapped to HTTP 404 with a caller-supplied UPPER_SNAKE_CASE error code.
public sealed class CodedNotFoundException : Exception
{
    public string ErrorCode { get; }

    public CodedNotFoundException(string errorCode, string message) : base(message)
    {
        if (!IsUpperSnakeCase(errorCode))
        {
            throw new ArgumentException("Error code must be non-empty UPPER_SNAKE_CASE.", nameof(errorCode));
        }

        ErrorCode = errorCode;
    }

    private static bool IsUpperSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var previousWasUnderscore = true;
        foreach (var c in value)
        {
            if (c == '_')
            {
                if (previousWasUnderscore)
                {
                    return false;
                }

                previousWasUnderscore = true;
                continue;
            }

            if (c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9'))
            {
                return false;
            }

            previousWasUnderscore = false;
        }

        return !previousWasUnderscore;
    }
}

/// Mapped to HTTP 409.
public sealed class ConflictException : Exception
{
    public string ErrorCode { get; }
    public IReadOnlyList<ValidationError> Errors { get; }

    public ConflictException(string errorCode, string message, IReadOnlyList<ValidationError>? errors = null) : base(message)
    {
        ErrorCode = errorCode;
        Errors = errors ?? Array.Empty<ValidationError>();
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
