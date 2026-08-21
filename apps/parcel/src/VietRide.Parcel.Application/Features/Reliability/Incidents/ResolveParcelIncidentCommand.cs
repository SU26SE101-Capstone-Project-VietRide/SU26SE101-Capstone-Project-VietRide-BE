using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record ResolveParcelIncidentCommand(
    Guid IncidentId,
    Guid OperatorId,
    Guid ActorUserId,
    string ResolutionCode,
    string? Note) : IRequest<ParcelIncidentListItem>;
