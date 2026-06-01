namespace VietRide.Identity.Domain.Enums;

public enum UserStatus
{
    PENDING_EMAIL_VERIFICATION,
    PENDING_INITIAL_PASSWORD,
    ACTIVE,
    LOCKED,
    DELETED,
}
