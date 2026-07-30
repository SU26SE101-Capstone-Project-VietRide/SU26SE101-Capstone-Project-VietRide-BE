namespace VietRide.Trip.Application.Abstractions.Repositories;

public sealed record TripCargoTransferRepositoryResult(
    TripCargoTransferStatus Status,
    Guid ParcelId,
    Guid SourceTripId,
    Guid TargetTripId,
    string TargetState,
    decimal WeightKg,
    decimal VolumeM3,
    bool NearFullCrossed,
    Guid TargetOperatorId,
    decimal TargetLoadedWeightKg,
    decimal TargetMaxCargoWeightKg,
    decimal TargetPercentFull)
{
    public static TripCargoTransferRepositoryResult Failed(TripCargoTransferStatus status) =>
        new(
            status,
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            0m,
            0m,
            false,
            Guid.Empty,
            0m,
            0m,
            0m);
}
