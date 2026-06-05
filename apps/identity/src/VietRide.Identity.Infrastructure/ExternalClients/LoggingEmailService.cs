using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.ExternalClients;

namespace VietRide.Identity.Infrastructure.ExternalClients;

/// <summary>
/// Day-3 stub implementation of <see cref="IEmailService"/>.
/// Logs the OTP via Serilog; makes no real HTTP call and has no SendGrid SDK dependency.
///
/// Day 10 will replace this with <c>OutboxBackedEmailService</c> which inserts
/// <c>identity.otp.requested</c> into the Outbox. The Notification Service consumer
/// then delivers the email via SendGrid (BSOT §1.2 line 93 + §3.5 lines 419/471).
/// </summary>
public sealed class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendOtpAsync(
        string to,
        string code,
        EmailOtpPurpose purpose,
        int ttlMinutes,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DEV] OTP email — to: {Email}, purpose: {Purpose}, code: {Code}, ttlMinutes: {Ttl}",
            to,
            purpose,
            code,
            ttlMinutes);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAccountCreatedLinkAsync(
        string to,
        AccountCreatedEmailDto accountInfo,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DEV] Account-created email — to: {Email}, userId: {UserId}, link: {Link}, expiresAt: {ExpiresAt}",
            to,
            accountInfo.UserId,
            accountInfo.SetInitialPasswordUrl,
            accountInfo.ExpiresAt);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendParcelDeliveryLinkAsync(
        string to,
        string deliveryToken,
        ParcelDeliveryEmailDto parcelInfo,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("Parcel delivery email — Day 26+");
    }
}
