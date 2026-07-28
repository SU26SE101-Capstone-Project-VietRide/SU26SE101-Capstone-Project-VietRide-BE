using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public interface IVnPayClient
{
    string CreateTopUpRedirectUrl(
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt);

    string CreateBookingPaymentRedirectUrl(
        Guid bookingId,
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt)
        => throw new NotSupportedException("This VNPay client does not support booking-payment redirect URLs.");

    string CreateBookingPaymentRedirectUrl(
        Guid bookingId,
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
        => CreateBookingPaymentRedirectUrl(
            bookingId,
            userId,
            amount,
            vnPayTxnRef,
            clientIpAddress,
            createdAt);

    string CreateSubscriptionPaymentRedirectUrl(
        Guid upgradeAttemptId,
        Guid operatorId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt)
        => throw new NotSupportedException("This VNPay client does not support subscription-payment redirect URLs.");

    string CreateSubscriptionPaymentRedirectUrl(
        Guid upgradeAttemptId,
        Guid operatorId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
        => CreateSubscriptionPaymentRedirectUrl(
            upgradeAttemptId,
            operatorId,
            amount,
            vnPayTxnRef,
            clientIpAddress,
            createdAt);

    bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
        => throw new NotSupportedException("This VNPay client does not support signature verification.");

    bool IsExpectedMerchant(IReadOnlyDictionary<string, string> parameters) => true;

    Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        => Task.FromResult(true);

    Task ReleaseIpnReservationAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
