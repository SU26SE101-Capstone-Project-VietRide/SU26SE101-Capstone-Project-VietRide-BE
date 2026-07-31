namespace VietRide.Parcel.Application.Features.Parcels.OperatorStats;

public sealed record OperatorParcelStatsResponse(
    IReadOnlyList<OperatorParcelStatsItemResponse> Items,
    long TotalParcels);
