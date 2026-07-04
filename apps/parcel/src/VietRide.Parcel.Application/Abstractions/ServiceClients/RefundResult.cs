namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record RefundResult(
    Guid WalletTransactionId,
    long BalanceAfter);
