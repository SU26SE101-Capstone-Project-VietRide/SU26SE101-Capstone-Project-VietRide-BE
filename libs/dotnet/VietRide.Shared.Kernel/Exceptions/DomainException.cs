namespace VietRide.Shared.Kernel.Exceptions;

/// Base for all domain rule violations. Subclasses live in each service's Domain layer.
/// Maps to HTTP 422 by default via ApiResponseExceptionFilter (VietRide.Shared.Web);
/// selected BSOT error codes can map to their registered status codes.
public abstract class DomainException : Exception
{
    public string ErrorCode { get; }

    protected DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
