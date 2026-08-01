namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IPaymentRedirectLookupClient
{
    Task<IReadOnlyList<PaymentRedirectLookupItem>> LookupAsync(
        Guid userId,
        IReadOnlyCollection<PaymentRedirectLookupReference> references,
        CancellationToken cancellationToken = default);
}
