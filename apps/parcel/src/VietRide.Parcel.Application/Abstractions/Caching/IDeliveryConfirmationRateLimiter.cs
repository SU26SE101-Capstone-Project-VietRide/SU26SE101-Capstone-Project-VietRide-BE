namespace VietRide.Parcel.Application.Abstractions.Caching;

public interface IDeliveryConfirmationRateLimiter
{
    Task<bool> TryAcquireAsync(
        string tokenHash,
        CancellationToken cancellationToken);
}
