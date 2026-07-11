using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record GetAssistantTripParcelsQuery(
    Guid TripId,
    Guid UserId,
    Guid OperatorId,
    int Page,
    int PageSize) : IQuery<PagedResult<AssistantTripParcelResponse>>;
