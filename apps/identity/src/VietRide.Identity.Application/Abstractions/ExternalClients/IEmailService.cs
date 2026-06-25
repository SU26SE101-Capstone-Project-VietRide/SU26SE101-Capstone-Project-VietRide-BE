namespace VietRide.Identity.Application.Abstractions.ExternalClients;

/// <summary>
/// Abstraction for transactional email delivery (non-OTP emails only).
/// OTP emails are now delivered asynchronously via the Outbox:
/// handlers enqueue <c>identity.otp.requested</c>; Notification Service
/// consumes and calls SendGrid (BSOT §1.2 line 93 + §3.5 line 419).
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends the account-created email with the initial-password setup link.
    /// </summary>
    Task SendAccountCreatedLinkAsync(
        string to,
        AccountCreatedEmailDto accountInfo,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a parcel delivery link email.
    /// Day 26+ — not implemented in Day 3 (throws <see cref="NotImplementedException"/>).
    /// </summary>
    Task SendParcelDeliveryLinkAsync(
        string to,
        string deliveryToken,
        ParcelDeliveryEmailDto parcelInfo,
        CancellationToken ct = default);
}
