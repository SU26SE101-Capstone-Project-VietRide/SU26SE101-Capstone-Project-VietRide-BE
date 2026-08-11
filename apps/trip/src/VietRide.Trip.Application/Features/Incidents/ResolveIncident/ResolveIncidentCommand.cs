using MediatR;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

namespace VietRide.Trip.Application.Features.Incidents.ResolveIncident;

public sealed record ResolveIncidentCommand(
    Guid OperatorId,
    Guid ActorUserId,
    Guid IncidentId,
    string ResolutionNote) : IRequest<OperatorIncidentDto>;
