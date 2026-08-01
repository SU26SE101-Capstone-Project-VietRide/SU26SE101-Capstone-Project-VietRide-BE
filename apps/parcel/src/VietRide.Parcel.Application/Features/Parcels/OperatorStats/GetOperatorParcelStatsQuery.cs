using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorStats;

public sealed record GetOperatorParcelStatsQuery(
    Guid OperatorId,
    DateOnly? From,
    DateOnly? To,
    string? GroupBy,
    int? Limit) : IQuery<OperatorParcelStatsResponse>;
