using MediatR;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed record GetOperatorIncidentQuery(Guid OperatorId, Guid IncidentId) : IRequest<OperatorIncidentDto>;
