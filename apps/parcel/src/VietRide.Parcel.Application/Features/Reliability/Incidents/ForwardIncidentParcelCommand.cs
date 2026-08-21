using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record ForwardIncidentParcelCommand(
    Guid IncidentId,
    Guid OperatorId,
    Guid ActorUserId,
    Guid TargetTripId) : IRequest<ParcelIncidentListItem>;
