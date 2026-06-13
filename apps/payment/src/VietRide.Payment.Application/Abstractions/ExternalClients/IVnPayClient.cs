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

    bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
        => throw new NotSupportedException("This VNPay client does not support signature verification.");

    Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        => throw new NotSupportedException("This VNPay client does not support IPN dedupe reservation.");

    Task ReleaseIpnReservationAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        => throw new NotSupportedException("This VNPay client does not support IPN dedupe reservation release.");
}
