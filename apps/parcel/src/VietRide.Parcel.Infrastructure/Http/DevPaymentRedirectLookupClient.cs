using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class DevPaymentRedirectLookupClient : IPaymentRedirectLookupClient
{
    public Task<IReadOnlyList<PaymentRedirectLookupItem>> LookupAsync(
        Guid userId,
        IReadOnlyCollection<PaymentRedirectLookupReference> references,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PaymentRedirectLookupItem>>([]);
}
