using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed class GetOperatorIncidentHandler : IRequestHandler<GetOperatorIncidentQuery, OperatorIncidentDto>
{
    private readonly IIncidentRepository incidents;
    private readonly IIdentityInternalClient identity;

    public GetOperatorIncidentHandler(IIncidentRepository incidents, IIdentityInternalClient identity)
    {
        this.incidents = incidents;
        this.identity = identity;
    }

    public async Task<OperatorIncidentDto> Handle(
        GetOperatorIncidentQuery request,
        CancellationToken cancellationToken)
    {
        var row = await incidents.GetOperatorIncidentAsync(
            request.OperatorId,
            request.IncidentId,
            cancellationToken)
            ?? throw new CodedNotFoundException("INCIDENT_NOT_FOUND", "Incident was not found.");
        var profiles = await identity.GetUsersAsync([row.ReportedByUserId], cancellationToken);
        return OperatorIncidentMapper.ToDto(row, profiles);
    }
}
