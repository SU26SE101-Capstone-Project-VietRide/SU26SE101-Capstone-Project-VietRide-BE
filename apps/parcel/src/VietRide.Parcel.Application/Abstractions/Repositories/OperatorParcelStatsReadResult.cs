namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record OperatorParcelStatsReadResult(
    long TotalParcels,
    IReadOnlyList<OperatorParcelStatsBucketReadModel> Buckets);
