namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public static class TripServiceClientIdempotencyExtensions
{
    public static Task<TripCargoOutcome> ReserveCargoAsync(
        this ITripServiceClient client,
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => client is IIdempotentTripServiceClient idempotent
            ? idempotent.ReserveCargoAsync(
                tripId, parcelId, weightKg, volumeM3, idempotencyKey, cancellationToken)
            : client.ReserveCargoAsync(tripId, parcelId, weightKg, volumeM3, cancellationToken);

    public static Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(
        this ITripServiceClient client,
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => client is IIdempotentTripServiceClient idempotent
            ? idempotent.ReserveCargoWithOverrideAsync(
                tripId, parcelId, weightKg, volumeM3, idempotencyKey, cancellationToken)
            : client.ReserveCargoWithOverrideAsync(
                tripId, parcelId, weightKg, volumeM3, cancellationToken);

    public static Task<TripCargoOutcome> RemeasureCargoAsync(
        this ITripServiceClient client,
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => client is IIdempotentTripServiceClient idempotent
            ? idempotent.RemeasureCargoAsync(
                tripId,
                parcelId,
                weightKg,
                volumeM3,
                allowCapacityOverflow,
                idempotencyKey,
                cancellationToken)
            : client.RemeasureCargoAsync(
                tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow, cancellationToken);

    public static Task<TripCargoOutcome> LoadCargoAsync(
        this ITripServiceClient client,
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => client is IIdempotentTripServiceClient idempotent
            ? idempotent.LoadCargoAsync(
                tripId, parcelId, weightKg, volumeM3, idempotencyKey, cancellationToken)
            : client.LoadCargoAsync(tripId, parcelId, weightKg, volumeM3, cancellationToken);

    public static Task<TripCargoOutcome> ReleaseCargoAsync(
        this ITripServiceClient client,
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => client is IIdempotentTripServiceClient idempotent
            ? idempotent.ReleaseCargoAsync(
                tripId, parcelId, weightKg, volumeM3, idempotencyKey, cancellationToken)
            : client.ReleaseCargoAsync(tripId, parcelId, weightKg, volumeM3, cancellationToken);
}
