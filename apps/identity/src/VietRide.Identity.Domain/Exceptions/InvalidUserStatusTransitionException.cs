namespace VietRide.Identity.Domain.Exceptions;

/// Thrown when a User entity status transition is invalid.
public sealed class InvalidUserStatusTransitionException : IdentityDomainException
{
    public InvalidUserStatusTransitionException(string from, string to)
        : base("USER_INVALID_STATUS_TRANSITION",
               $"Cannot transition user status from {from} to {to}.")
    {
    }
}
