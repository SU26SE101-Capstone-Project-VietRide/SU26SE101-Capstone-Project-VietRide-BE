using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IIncidentRepository : IRepository<Incident, Guid>
{
    Task<PagedResult<OperatorIncidentReadRow>> ListOperatorIncidentsAsync(
        Guid operatorId,
        Guid? tripId,
        IncidentCategory? category,
        bool? resolved,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator Incident listing is not implemented by this repository.");

    Task<PagedResult<OperatorIncidentReadRow>> ListOperatorIncidentsFilteredAsync(
        Guid operatorId, Guid? tripId, IncidentCategory? category, bool? resolved,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtcExclusive, string? search,
        IReadOnlyCollection<Guid> reporterUserIds, Guid? reportedByUserId,
        string sortBy, string sortDir, int page, int pageSize,
        CancellationToken cancellationToken = default)
        => ListOperatorIncidentsAsync(operatorId, tripId, category, resolved, fromUtc,
            toUtcExclusive, page, pageSize, cancellationToken);

    Task<OperatorIncidentReadRow?> GetOperatorIncidentAsync(
        Guid operatorId,
        Guid incidentId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator Incident detail is not implemented by this repository.");

    Task<Incident?> AcquireOperatorIncidentAsync(
        Guid operatorId,
        Guid incidentId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator Incident locking is not implemented by this repository.");
}
