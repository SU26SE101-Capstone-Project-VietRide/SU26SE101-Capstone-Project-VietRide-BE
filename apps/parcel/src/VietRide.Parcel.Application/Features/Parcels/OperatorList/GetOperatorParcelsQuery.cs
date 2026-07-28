using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed record GetOperatorParcelsQuery(
    Guid OperatorId,
    string? Status,
    Guid? TripId,
    string? PendingActionType,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<OperatorParcelListItemResponse>>;
