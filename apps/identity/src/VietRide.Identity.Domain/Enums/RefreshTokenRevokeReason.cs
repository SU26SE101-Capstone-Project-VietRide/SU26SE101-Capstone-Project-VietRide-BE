namespace VietRide.Identity.Domain.Enums;

public enum RefreshTokenRevokeReason
{
    NORMAL_ROTATION,
    REUSE_DETECTED,
    USER_LOGOUT,
    ADMIN_REVOKE,
    PASSWORD_RESET,
}
