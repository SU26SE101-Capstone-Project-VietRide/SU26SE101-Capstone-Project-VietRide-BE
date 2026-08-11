using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

namespace VietRide.Trip.Application.Features.Incidents.ResolveIncident;

public sealed class ResolveIncidentCommandHandler
    : IRequestHandler<ResolveIncidentCommand, OperatorIncidentDto>
{
    private readonly IIncidentRepository incidents;
    private readonly IIdentityInternalClient identity;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public ResolveIncidentCommandHandler(
        IIncidentRepository incidents,
        IIdentityInternalClient identity,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.incidents = incidents;
        this.identity = identity;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<OperatorIncidentDto> Handle(
        ResolveIncidentCommand request,
        CancellationToken cancellationToken)
    {
        var row = await incidents.GetOperatorIncidentAsync(
            request.OperatorId,
            request.IncidentId,
            cancellationToken)
            ?? throw IncidentNotFound();
        if (row.ResolvedAt.HasValue)
        {
            throw AlreadyResolved();
        }

        var profiles = await identity.GetUsersAsync([row.ReportedByUserId], cancellationToken);
        var resolution = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidents.AcquireOperatorIncidentAsync(
                request.OperatorId,
                request.IncidentId,
                cancellationToken)
                ?? throw IncidentNotFound();
            if (incident.ResolvedAt.HasValue)
            {
                throw AlreadyResolved();
            }

            var resolvedAt = clock.UtcNow;
            incident.Resolve(request.ActorUserId, request.ResolutionNote, resolvedAt);
            return new Resolution(resolvedAt, request.ActorUserId, incident.ResolutionNote!);
        }, cancellationToken);

        return OperatorIncidentMapper.ToDto(
            row with
            {
                ResolvedAt = resolution.ResolvedAt,
                ResolvedByUserId = resolution.ResolvedByUserId,
                ResolutionNote = resolution.ResolutionNote,
            },
            profiles);
    }

    private static CodedNotFoundException IncidentNotFound()
        => new("INCIDENT_NOT_FOUND", "Incident was not found.");

    private static CodedConflictException AlreadyResolved()
        => new("INCIDENT_ALREADY_RESOLVED", "Incident is already resolved.");

    private sealed record Resolution(
        DateTimeOffset ResolvedAt,
        Guid ResolvedByUserId,
        string ResolutionNote);
}
