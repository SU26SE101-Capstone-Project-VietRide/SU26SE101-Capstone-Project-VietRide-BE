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
}
