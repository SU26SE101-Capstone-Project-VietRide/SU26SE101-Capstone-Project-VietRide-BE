using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.ExternalClients;

namespace VietRide.Identity.Infrastructure.ExternalClients;

/// <summary>
/// Stub implementation of <see cref="IEmailService"/> for non-OTP transactional emails.
/// OTP delivery is now fully async via Outbox → RabbitMQ → Notification Service
/// (BSOT §1.2 line 93 + §3.5 lines 419/471).
/// </summary>
public sealed class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        _logger = logger;
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
