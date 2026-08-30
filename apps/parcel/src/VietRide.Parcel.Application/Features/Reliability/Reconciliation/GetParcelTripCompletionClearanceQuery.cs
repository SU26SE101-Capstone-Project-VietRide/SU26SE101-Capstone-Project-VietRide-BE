using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed record GetParcelTripCompletionClearanceQuery(
    Guid TripId,
    Guid OperatorId) : IRequest<ParcelTripCompletionClearanceResponse>;

public sealed record ParcelTripCompletionClearanceResponse(
    Guid TripId,
    Guid OperatorId,
    string Status,
    IReadOnlyList<Guid> UnresolvedParcelIds,
    IReadOnlyList<Guid> IncidentIds);
