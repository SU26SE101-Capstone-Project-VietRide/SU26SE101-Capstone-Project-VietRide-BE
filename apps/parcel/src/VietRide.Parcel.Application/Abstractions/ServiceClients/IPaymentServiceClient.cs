namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IPaymentServiceClient
{
    Task<ChargeOutcome> ChargeParcelPaymentAsync(
        string referenceType,
        Guid referenceId,
        Guid userId,
        long amount,
        string method,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<RefundOutcome> RefundParcelPaymentAsync(
        Guid userId,
        long amount,
        string referenceType,
        Guid referenceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
