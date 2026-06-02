namespace VietRide.Shared.Kernel.Exceptions;

/// Base for all domain rule violations. Subclasses live in each service's Domain layer.
/// Maps to HTTP 422 / 409 / 403 via ApiResponseExceptionFilter (VietRide.Shared.Web).
public abstract class DomainException : Exception
{
    public string ErrorCode { get; }

    protected DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
