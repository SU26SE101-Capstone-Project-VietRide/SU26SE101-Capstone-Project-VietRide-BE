namespace VietRide.Identity.Application.Abstractions.ExternalClients;

/// <summary>
/// Purpose enum for OTP emails — contract-level enum, distinct from
/// <see cref="VietRide.Identity.Domain.Enums.EmailVerificationPurpose"/>.
/// Per v7 lines 238-239.
/// </summary>
public enum EmailOtpPurpose
{
    REGISTRATION,
    PASSWORD_RESET,
}
