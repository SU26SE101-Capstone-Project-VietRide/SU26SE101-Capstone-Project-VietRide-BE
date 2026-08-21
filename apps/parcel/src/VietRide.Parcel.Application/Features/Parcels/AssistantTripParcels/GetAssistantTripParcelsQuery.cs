using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record GetAssistantTripParcelsQuery(
    Guid TripId,
    Guid UserId,
    Guid OperatorId,
    int Page,
    int PageSize,
    Guid? StopId = null,
    string? Status = null,
    bool? HasException = null,
    string? Search = null) : IQuery<AssistantTripParcelManifestResponse>;
