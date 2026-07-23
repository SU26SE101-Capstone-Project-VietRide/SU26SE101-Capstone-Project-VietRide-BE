namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IIdempotentTripServiceClient
{
    Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> RemeasureCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);
}
