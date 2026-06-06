namespace VietRide.Identity.Application.Abstractions.ExternalClients;

/// <summary>
/// Abstraction for transactional email delivery.
/// Signature per v7 lines 234-239.
///
/// Day 3: implemented by <c>LoggingEmailService</c> (Serilog log, no real send).
/// Day 10: replaced by <c>OutboxBackedEmailService</c> that inserts
///         <c>identity.otp.requested</c> into the Outbox; Notification Service
///         consumes and calls SendGrid (BSOT §1.2 line 93 + §3.5 line 419).
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an OTP email to the recipient.
    /// </summary>
    Task SendOtpAsync(
        string to,
        string code,
        EmailOtpPurpose purpose,
        int ttlMinutes,
        CancellationToken ct = default);

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
