using VietRide.Shared.Kernel.Exceptions;

namespace VietRide.Identity.Domain.Exceptions;

/// Base for Identity service domain rule violations.
public class IdentityDomainException : DomainException
{
    public IdentityDomainException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
