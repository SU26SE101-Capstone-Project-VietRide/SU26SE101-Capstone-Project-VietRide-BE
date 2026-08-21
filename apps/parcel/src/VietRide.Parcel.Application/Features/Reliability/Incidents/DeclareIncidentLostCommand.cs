using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record DeclareIncidentLostCommand(
    Guid IncidentId,
    Guid OperatorId,
    Guid ActorUserId,
    string? Note) : IRequest<ParcelIncidentListItem>;
