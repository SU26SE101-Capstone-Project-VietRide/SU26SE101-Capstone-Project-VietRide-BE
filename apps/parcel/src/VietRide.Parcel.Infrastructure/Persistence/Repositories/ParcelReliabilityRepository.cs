using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelReliabilityRepository : IParcelReliabilityRepository
{
    private readonly ParcelDbContext _db;

    public ParcelReliabilityRepository(ParcelDbContext db)
    {
        _db = db;
    }

    public Task<ParcelTransitLeg?> GetActiveLegAsync(Guid parcelId, CancellationToken ct = default)
        => _db.ParcelTransitLegs
            .Where(x => x.ParcelId == parcelId
                && (x.Status == ParcelTransitLegStatus.ACTIVE || x.Status == ParcelTransitLegStatus.PLANNED))
            .OrderByDescending(x => x.Status == ParcelTransitLegStatus.ACTIVE)
            .ThenByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(ct);

    public Task<ParcelTransitLeg?> GetTransitLegAsync(
        Guid parcelId,
        Guid tripId,
        CancellationToken ct = default)
        => _db.ParcelTransitLegs
            .Where(x => x.ParcelId == parcelId && x.TripId == tripId)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(ct);

    public Task<ParcelTransitLeg?> GetLatestTransitLegAsync(
        Guid parcelId,
        CancellationToken ct = default)
        => _db.ParcelTransitLegs
            .Where(x => x.ParcelId == parcelId)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(ct);

    public Task<ParcelCustodyEvent?> GetCustodyEventByIdempotencyAsync(
        Guid parcelId,
        string idempotencyKey,
        CancellationToken ct = default)
        => _db.ParcelCustodyEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ParcelId == parcelId && x.IdempotencyKey == idempotencyKey, ct);

    public async Task<IReadOnlyList<ParcelCustodyEvent>> ListCustodyEventsAsync(
        Guid parcelId,
        CancellationToken ct = default)
        => await _db.ParcelCustodyEvents
            .AsNoTracking()
            .Where(x => x.ParcelId == parcelId)
            .OrderBy(x => x.Sequence)
            .ThenBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(ct);

    public Task<ParcelCurrentCustody?> GetCurrentCustodyAsync(Guid parcelId, CancellationToken ct = default)
        => _db.ParcelCurrentCustodies.FirstOrDefaultAsync(x => x.ParcelId == parcelId, ct);

    public async Task<IReadOnlyList<ParcelCurrentCustody>> ListCurrentCustodiesAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(parcelIds, nameof(parcelIds));
        return ids.Length == 0
            ? []
            : await _db.ParcelCurrentCustodies
                .AsNoTracking()
                .Where(x => ids.Contains(x.ParcelId))
                .ToArrayAsync(ct);
    }

    public Task<ParcelIncident?> GetIncidentAsync(Guid incidentId, CancellationToken ct = default)
        => _db.ParcelIncidents.FirstOrDefaultAsync(x => x.Id == incidentId, ct);

    public Task<ParcelIncident?> GetForwardingIncidentForUpdateAsync(
        Guid parcelId,
        CancellationToken ct = default)
        => _db.ParcelIncidents
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_incidents
                WHERE parcel_id = {parcelId}
                  AND status = 'FORWARDING'
                ORDER BY created_at DESC, id
                LIMIT 1
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ParcelIncident>> ListIncidentsByIdsAsync(
        IReadOnlyCollection<Guid> incidentIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(incidentIds, nameof(incidentIds));
        return ids.Length == 0
            ? []
            : await _db.ParcelIncidents
                .AsNoTracking()
                .Where(incident => ids.Contains(incident.Id))
                .ToArrayAsync(ct);
    }

    public Task<ParcelIncident?> GetOpenIncidentAsync(
        Guid parcelId,
        ParcelIncidentType type,
        CancellationToken ct = default)
        => _db.ParcelIncidents.FirstOrDefaultAsync(x => x.ParcelId == parcelId
            && x.Type == type
            && x.Status != ParcelIncidentStatus.CLOSED
            && x.Status != ParcelIncidentStatus.RESOLVED, ct);

    public async Task<IReadOnlyList<ParcelIncident>> ListIncidentsByParcelAsync(
        Guid parcelId,
        CancellationToken ct = default)
        => await _db.ParcelIncidents
            .AsNoTracking()
            .Where(x => x.ParcelId == parcelId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelCustodyEvent>> ListCustodyEventsPageAsync(
        Guid parcelId,
        int? beforeSequence,
        int take,
        CancellationToken ct = default)
    {
        var query = _db.ParcelCustodyEvents
            .AsNoTracking()
            .Where(item => item.ParcelId == parcelId);
        if (beforeSequence.HasValue)
            query = query.Where(item => item.Sequence < beforeSequence.Value);
        return await query
            .OrderByDescending(item => item.Sequence)
            .ThenByDescending(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .Take(Math.Clamp(take, 1, 101))
            .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ParcelCustodyEvent>> ListCustodyEventsByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(parcelIds, nameof(parcelIds));
        return ids.Length == 0
            ? []
            : await _db.ParcelCustodyEvents
                .AsNoTracking()
                .Where(custodyEvent => ids.Contains(custodyEvent.ParcelId))
                .OrderBy(custodyEvent => custodyEvent.ParcelId)
                .ThenBy(custodyEvent => custodyEvent.Sequence)
                .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ParcelIncident>> ListActiveIncidentsByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(parcelIds, nameof(parcelIds));
        if (ids.Length == 0)
            return [];

        var incidents = await _db.ParcelIncidents
            .AsNoTracking()
            .Where(x => ids.Contains(x.ParcelId)
                && x.Status != ParcelIncidentStatus.CLOSED
                && x.Status != ParcelIncidentStatus.RESOLVED)
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(ct);
        return incidents
            .GroupBy(x => x.ParcelId)
            .Select(group => group.First())
            .ToArray();
    }

    public async Task<IReadOnlyList<ParcelIncident>> ListIncidentsByOperatorAsync(
        Guid operatorId,
        ParcelIncidentStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.ParcelIncidents
            .AsNoTracking()
            .Where(x => x.OperatorId == operatorId);
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
    }

    public async Task<PagedResult<ParcelIncident>> SearchIncidentsByOperatorAsync(
        Guid operatorId,
        ParcelIncidentStatus? status,
        ParcelIncidentType? type,
        string? search,
        IReadOnlyCollection<Guid> senderUserIds,
        Guid? tripId,
        Guid? assigneeId,
        string? slaState,
        ParcelCustodyExceptionRequestStatus? approvalStatus,
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        DateTimeOffset now,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var senderIds = senderUserIds.Distinct().ToArray();
        var query = _db.ParcelIncidents
            .AsNoTracking()
            .Where(incident => incident.OperatorId == operatorId);

        if (status.HasValue)
            query = query.Where(incident => incident.Status == status.Value);
        if (type.HasValue)
            query = query.Where(incident => incident.Type == type.Value);
        if (tripId.HasValue)
            query = query.Where(incident => incident.TripId == tripId.Value);
        if (assigneeId.HasValue)
        {
            query = query.Where(incident => _db.ParcelSearchTasks.Any(task =>
                task.IncidentId == incident.Id && task.AssigneeId == assigneeId.Value));
        }
        if (approvalStatus.HasValue)
        {
            query = query.Where(incident => _db.ParcelCustodyExceptionRequests.Any(request =>
                request.IncidentId == incident.Id && request.Status == approvalStatus.Value));
        }
        if (from.HasValue)
            query = query.Where(incident => incident.CreatedAt >= from.Value);
        if (toExclusive.HasValue)
            query = query.Where(incident => incident.CreatedAt < toExclusive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{term}%";
            var parsedGuid = Guid.TryParse(term, out var guid) ? guid : (Guid?)null;
            query = query.Where(incident =>
                EF.Functions.ILike(incident.LastKnownLocation ?? string.Empty, pattern)
                || (parsedGuid.HasValue && (incident.Id == parsedGuid.Value
                    || incident.ParcelId == parsedGuid.Value
                    || incident.TripId == parsedGuid.Value))
                || _db.Parcels.Any(parcel => parcel.Id == incident.ParcelId
                    && (EF.Functions.ILike(parcel.ParcelCode, pattern)
                        || EF.Functions.ILike(parcel.RecipientName, pattern)
                        || EF.Functions.ILike(parcel.TripSnapshotVehicleLicensePlate ?? string.Empty, pattern)
                        || EF.Functions.ILike(parcel.TripSnapshotRouteName ?? string.Empty, pattern)
                        || senderIds.Contains(parcel.SenderUserId))));
        }

        query = slaState?.ToUpperInvariant() switch
        {
            "NOT_STARTED" => query.Where(incident => !incident.SearchDeadline.HasValue
                && incident.Status == ParcelIncidentStatus.OPEN),
            "BREACHED" => query.Where(incident => incident.SearchDeadline.HasValue
                && incident.SearchDeadline < now
                && !_db.ParcelCustodyExceptionRequests.Any(request =>
                    request.IncidentId == incident.Id
                    && request.Status == ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL)
                && incident.Status != ParcelIncidentStatus.CLOSED
                && incident.Status != ParcelIncidentStatus.RESOLVED
                && incident.Status != ParcelIncidentStatus.LOST_CONFIRMED),
            "DUE_SOON" => query.Where(incident => incident.SearchDeadline.HasValue
                && incident.SearchDeadline >= now
                && incident.SearchDeadline <= now.AddHours(2)
                && !_db.ParcelCustodyExceptionRequests.Any(request =>
                    request.IncidentId == incident.Id
                    && request.Status == ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL)
                && incident.Status != ParcelIncidentStatus.CLOSED
                && incident.Status != ParcelIncidentStatus.RESOLVED
                && incident.Status != ParcelIncidentStatus.LOST_CONFIRMED),
            "ON_TRACK" => query.Where(incident => incident.SearchDeadline.HasValue
                && incident.SearchDeadline > now.AddHours(2)
                && !_db.ParcelCustodyExceptionRequests.Any(request =>
                    request.IncidentId == incident.Id
                    && request.Status == ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL)
                && incident.Status != ParcelIncidentStatus.CLOSED
                && incident.Status != ParcelIncidentStatus.RESOLVED
                && incident.Status != ParcelIncidentStatus.LOST_CONFIRMED),
            "CLOSED" => query.Where(incident => incident.Status == ParcelIncidentStatus.CLOSED
                || incident.Status == ParcelIncidentStatus.RESOLVED
                || incident.Status == ParcelIncidentStatus.LOST_CONFIRMED),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(incident => incident.SearchDeadline)
            .ThenByDescending(incident => incident.CreatedAt)
            .ThenBy(incident => incident.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
        return PagedResult<ParcelIncident>.Create(items, page, pageSize, total);
    }

    public Task<ParcelClaim?> GetClaimByIdAsync(Guid claimId, CancellationToken ct = default)
        => _db.ParcelClaims.FirstOrDefaultAsync(x => x.Id == claimId, ct);

    public async Task<ParcelClaim?> GetClaimByIdForUpdateAsync(
        Guid claimId,
        CancellationToken ct = default)
    {
        var matches = await _db.ParcelClaims
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_claims
                WHERE id = {claimId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(ct);
        return matches.SingleOrDefault();
    }

    public Task<ParcelClaim?> GetClaimByIncidentAsync(Guid incidentId, CancellationToken ct = default)
        => _db.ParcelClaims.FirstOrDefaultAsync(x => x.IncidentId == incidentId, ct);

    public Task<ParcelClaimAppeal?> GetClaimAppealByIdAsync(
        Guid appealId,
        CancellationToken ct = default)
        => _db.ParcelClaimAppeals.FirstOrDefaultAsync(x => x.Id == appealId, ct);

    public async Task<ParcelClaimAppeal?> GetClaimAppealByIdForUpdateAsync(
        Guid appealId,
        CancellationToken ct = default)
    {
        var matches = await _db.ParcelClaimAppeals
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_claim_appeals
                WHERE id = {appealId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(ct);
        return matches.SingleOrDefault();
    }

    public Task<ParcelClaimAppeal?> GetClaimAppealByClaimAsync(
        Guid claimId,
        CancellationToken ct = default)
        => _db.ParcelClaimAppeals.FirstOrDefaultAsync(x => x.ClaimId == claimId, ct);

    public Task<ParcelClaimAppeal?> GetClaimAppealByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken ct = default)
        => _db.ParcelClaimAppeals.FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);

    public async Task<PagedResult<ParcelClaimAppeal>> SearchClaimAppealsByOperatorAsync(
        Guid operatorId,
        ParcelClaimAppealStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.ParcelClaimAppeals
            .AsNoTracking()
            .Where(appeal => appeal.OperatorId == operatorId);
        if (status.HasValue)
            query = query.Where(appeal => appeal.Status == status.Value);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(appeal => appeal.CreatedAt)
            .ThenBy(appeal => appeal.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
        return PagedResult<ParcelClaimAppeal>.Create(items, page, pageSize, total);
    }

    public Task<ParcelCompensationPolicy?> GetCompensationPolicyAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => _db.ParcelCompensationPolicies.FirstOrDefaultAsync(x => x.OperatorId == operatorId, ct);

    public Task<UnidentifiedParcelPackage?> GetUnidentifiedPackageAsync(
        Guid packageId,
        CancellationToken ct = default)
        => _db.UnidentifiedParcelPackages.FirstOrDefaultAsync(x => x.Id == packageId, ct);

    public Task<ParcelSearchTask?> GetSearchTaskAsync(Guid taskId, CancellationToken ct = default)
        => _db.ParcelSearchTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct);

    public async Task<IReadOnlyList<ParcelSearchTask>> ListSearchTasksAsync(
        Guid incidentId,
        CancellationToken ct = default)
        => await _db.ParcelSearchTasks
            .Where(x => x.IncidentId == incidentId)
            .OrderBy(x => x.Deadline)
            .ThenBy(x => x.Id)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelSearchTask>> ListSearchTasksByIncidentsAsync(
        IReadOnlyCollection<Guid> incidentIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(incidentIds, nameof(incidentIds));
        return ids.Length == 0
            ? []
            : await _db.ParcelSearchTasks
                .AsNoTracking()
                .Where(task => ids.Contains(task.IncidentId))
                .OrderBy(task => task.Deadline)
                .ThenBy(task => task.Id)
                .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ParcelIncident>> ListExpiredSearchIncidentsAsync(
        DateTimeOffset now,
        int maxBatch,
        CancellationToken ct = default)
        => await _db.ParcelIncidents
            .Where(x => x.SearchDeadline.HasValue
                && x.SearchDeadline <= now
                && (x.Status == ParcelIncidentStatus.SEARCHING
                    || x.Status == ParcelIncidentStatus.ESCALATED
                    || x.Status == ParcelIncidentStatus.SEARCH_EXPIRED))
            .OrderBy(x => x.SearchDeadline)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(maxBatch, 1, 500))
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelClaim>> ListClaimsByParcelAsync(
        Guid parcelId,
        CancellationToken ct = default)
        => await _db.ParcelClaims
            .AsNoTracking()
            .Where(x => x.ParcelId == parcelId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelClaim>> ListLatestClaimsByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(parcelIds, nameof(parcelIds));
        if (ids.Length == 0)
            return [];

        var claims = await _db.ParcelClaims
            .AsNoTracking()
            .Where(claim => ids.Contains(claim.ParcelId))
            .OrderByDescending(claim => claim.CreatedAt)
            .ThenBy(claim => claim.Id)
            .ToArrayAsync(ct);
        return claims
            .GroupBy(claim => claim.ParcelId)
            .Select(group => group.First())
            .ToArray();
    }

    public async Task<PagedResult<ParcelClaim>> SearchClaimsByOperatorAsync(
        Guid operatorId,
        ParcelClaimStatus? status,
        string? search,
        IReadOnlyCollection<Guid> senderUserIds,
        string? slaState,
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        DateTimeOffset now,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var senderIds = senderUserIds.Distinct().ToArray();
        var query = _db.ParcelClaims
            .AsNoTracking()
            .Where(claim => claim.OperatorId == operatorId);
        if (status.HasValue)
            query = query.Where(claim => claim.Status == status.Value);
        if (from.HasValue)
            query = query.Where(claim => claim.CreatedAt >= from.Value);
        if (toExclusive.HasValue)
            query = query.Where(claim => claim.CreatedAt < toExclusive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{term}%";
            var parsedGuid = Guid.TryParse(term, out var guid) ? guid : (Guid?)null;
            query = query.Where(claim =>
                (parsedGuid.HasValue && (claim.Id == parsedGuid.Value
                    || claim.ParcelId == parsedGuid.Value
                    || claim.IncidentId == parsedGuid.Value))
                || _db.Parcels.Any(parcel => parcel.Id == claim.ParcelId
                    && (EF.Functions.ILike(parcel.ParcelCode, pattern)
                        || EF.Functions.ILike(parcel.RecipientName, pattern)
                        || EF.Functions.ILike(parcel.TripSnapshotVehicleLicensePlate ?? string.Empty, pattern)
                        || senderIds.Contains(parcel.SenderUserId))));
        }

        if (string.IsNullOrWhiteSpace(slaState))
        {
            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(claim => claim.CreatedAt)
                .ThenBy(claim => claim.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArrayAsync(ct);
            return PagedResult<ParcelClaim>.Create(items, page, pageSize, total);
        }

        var candidates = await query.ToArrayAsync(ct);
        var parcelIds = candidates.Select(claim => claim.ParcelId).Distinct().ToArray();
        var deadlines = await _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcelIds.Contains(parcel.Id))
            .Select(parcel => new
            {
                parcel.Id,
                parcel.DecisionSlaBusinessDaysSnapshot,
                parcel.PayoutSlaBusinessDaysSnapshot,
            })
            .ToDictionaryAsync(parcel => parcel.Id, ct);
        var normalizedSla = slaState.Trim().ToUpperInvariant();
        var filtered = candidates
            .Where(claim => deadlines.TryGetValue(claim.ParcelId, out var deadline)
                && GetClaimSlaState(
                    claim,
                    deadline.DecisionSlaBusinessDaysSnapshot,
                    deadline.PayoutSlaBusinessDaysSnapshot,
                    now) == normalizedSla)
            .OrderByDescending(claim => claim.CreatedAt)
            .ThenBy(claim => claim.Id)
            .ToArray();
        var filteredItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return PagedResult<ParcelClaim>.Create(filteredItems, page, pageSize, filtered.LongLength);
    }

    public async Task<IReadOnlyList<ParcelClaimEvidence>> ListClaimEvidenceAsync(
        Guid claimId,
        CancellationToken ct = default)
        => await _db.ParcelClaimEvidence
            .AsNoTracking()
            .Where(x => x.ClaimId == claimId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelClaimEvidence>> ListClaimEvidenceByClaimsAsync(
        IReadOnlyCollection<Guid> claimIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(claimIds, nameof(claimIds));
        return ids.Length == 0
            ? []
            : await _db.ParcelClaimEvidence
                .AsNoTracking()
                .Where(evidence => ids.Contains(evidence.ClaimId))
                .OrderBy(evidence => evidence.CreatedAt)
                .ThenBy(evidence => evidence.Id)
                .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ParcelClaimDecisionEvidence>> ListClaimDecisionEvidenceAsync(
        Guid claimId,
        CancellationToken ct = default)
        => await _db.ParcelClaimDecisionEvidence
            .AsNoTracking()
            .Where(x => x.ClaimId == claimId)
            .OrderBy(x => x.AcceptedAt)
            .ThenBy(x => x.EvidenceId)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelClaimDecisionEvidence>> ListClaimDecisionEvidenceByClaimsAsync(
        IReadOnlyCollection<Guid> claimIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(claimIds, nameof(claimIds));
        return ids.Length == 0
            ? []
            : await _db.ParcelClaimDecisionEvidence
                .AsNoTracking()
                .Where(link => ids.Contains(link.ClaimId))
                .OrderBy(link => link.AcceptedAt)
                .ThenBy(link => link.EvidenceId)
                .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ParcelClaimAppealDecisionEvidence>> ListClaimAppealDecisionEvidenceAsync(
        Guid appealId,
        CancellationToken ct = default)
        => await _db.ParcelClaimAppealDecisionEvidence
            .AsNoTracking()
            .Where(x => x.AppealId == appealId)
            .OrderBy(x => x.AcceptedAt)
            .ThenBy(x => x.EvidenceId)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelClaimAppealDecisionEvidence>> ListClaimAppealDecisionEvidenceByAppealsAsync(
        IReadOnlyCollection<Guid> appealIds,
        CancellationToken ct = default)
    {
        var ids = NormalizeIds(appealIds, nameof(appealIds));
        return ids.Length == 0
            ? []
            : await _db.ParcelClaimAppealDecisionEvidence
                .AsNoTracking()
                .Where(link => ids.Contains(link.AppealId))
                .OrderBy(link => link.AcceptedAt)
                .ThenBy(link => link.EvidenceId)
                .ToArrayAsync(ct);
    }

    public async Task<PagedResult<UnidentifiedParcelPackage>> ListUnidentifiedPackagesAsync(
        Guid operatorId,
        UnidentifiedParcelPackageStatus? status,
        string? search,
        Guid? tripId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.UnidentifiedParcelPackages
            .AsNoTracking()
            .Where(package => package.OperatorId == operatorId);
        if (status.HasValue)
            query = query.Where(package => package.Status == status.Value);
        if (tripId.HasValue)
            query = query.Where(package => package.TripId == tripId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(package =>
                EF.Functions.ILike(package.TemporaryExceptionTag, pattern)
                || EF.Functions.ILike(package.Description, pattern)
                || EF.Functions.ILike(package.LocationSnapshot ?? string.Empty, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(package => package.CreatedAt)
            .ThenBy(package => package.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
        return PagedResult<UnidentifiedParcelPackage>.Create(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<VietRide.Parcel.Domain.Entities.Parcel>> ListUnidentifiedMatchCandidatesAsync(
        Guid operatorId,
        Guid? tripId,
        decimal? observedWeightKg,
        int maxResults,
        CancellationToken ct = default)
    {
        var query = _db.Parcels
            .AsNoTracking()
            .Where(parcel => parcel.OperatorId == operatorId);
        if (tripId.HasValue)
            query = query.Where(parcel => parcel.TripId == tripId.Value);
        if (observedWeightKg.HasValue)
        {
            var tolerance = Math.Max(2m, observedWeightKg.Value * 0.2m);
            var minimum = observedWeightKg.Value - tolerance;
            var maximum = observedWeightKg.Value + tolerance;
            query = query.Where(parcel =>
                (parcel.ActualWeightKg ?? parcel.EstimatedWeightKg) >= minimum
                && (parcel.ActualWeightKg ?? parcel.EstimatedWeightKg) <= maximum);
        }

        return await query
            .OrderByDescending(parcel => parcel.UpdatedAt)
            .ThenBy(parcel => parcel.Id)
            .Take(Math.Clamp(maxResults, 1, 50))
            .ToArrayAsync(ct);
    }

    public async Task AddTransitLegAsync(ParcelTransitLeg entity, CancellationToken ct = default)
        => await _db.ParcelTransitLegs.AddAsync(entity, ct);

    public async Task AddCustodyEventAsync(ParcelCustodyEvent entity, CancellationToken ct = default)
        => await _db.ParcelCustodyEvents.AddAsync(entity, ct);

    public async Task AddCurrentCustodyAsync(ParcelCurrentCustody entity, CancellationToken ct = default)
        => await _db.ParcelCurrentCustodies.AddAsync(entity, ct);

    public async Task AddIncidentAsync(ParcelIncident entity, CancellationToken ct = default)
        => await _db.ParcelIncidents.AddAsync(entity, ct);

    public async Task AddSearchTaskAsync(ParcelSearchTask entity, CancellationToken ct = default)
        => await _db.ParcelSearchTasks.AddAsync(entity, ct);

    public async Task AddClaimAsync(ParcelClaim entity, CancellationToken ct = default)
        => await _db.ParcelClaims.AddAsync(entity, ct);

    public async Task AddClaimAppealAsync(ParcelClaimAppeal entity, CancellationToken ct = default)
        => await _db.ParcelClaimAppeals.AddAsync(entity, ct);

    public async Task AddClaimEvidenceAsync(ParcelClaimEvidence entity, CancellationToken ct = default)
        => await _db.ParcelClaimEvidence.AddAsync(entity, ct);

    public async Task AddClaimDecisionEvidenceAsync(
        ParcelClaimDecisionEvidence entity,
        CancellationToken ct = default)
        => await _db.ParcelClaimDecisionEvidence.AddAsync(entity, ct);

    public async Task AddClaimAppealDecisionEvidenceAsync(
        ParcelClaimAppealDecisionEvidence entity,
        CancellationToken ct = default)
        => await _db.ParcelClaimAppealDecisionEvidence.AddAsync(entity, ct);

    public async Task AddCompensationPolicyAsync(ParcelCompensationPolicy entity, CancellationToken ct = default)
        => await _db.ParcelCompensationPolicies.AddAsync(entity, ct);

    public async Task AddUnidentifiedPackageAsync(UnidentifiedParcelPackage entity, CancellationToken ct = default)
        => await _db.UnidentifiedParcelPackages.AddAsync(entity, ct);

    public Task UpdateCurrentCustodyAsync(ParcelCurrentCustody entity, CancellationToken ct = default)
    {
        _db.ParcelCurrentCustodies.Update(entity);
        return Task.CompletedTask;
    }

    public Task UpdateTransitLegAsync(ParcelTransitLeg entity, CancellationToken ct = default)
    {
        _db.ParcelTransitLegs.Update(entity);
        return Task.CompletedTask;
    }

    public Task UpdateIncidentAsync(ParcelIncident entity, CancellationToken ct = default)
    {
        _db.ParcelIncidents.Update(entity);
        return Task.CompletedTask;
    }

    public Task UpdateSearchTaskAsync(ParcelSearchTask entity, CancellationToken ct = default)
    {
        _db.ParcelSearchTasks.Update(entity);
        return Task.CompletedTask;
    }

    public Task UpdateClaimAsync(ParcelClaim entity, CancellationToken ct = default)
    {
        _db.ParcelClaims.Update(entity);
        return Task.CompletedTask;
    }

    public Task UpdateClaimAppealAsync(ParcelClaimAppeal entity, CancellationToken ct = default)
    {
        _db.ParcelClaimAppeals.Update(entity);
        return Task.CompletedTask;
    }

    public Task UpdateCompensationPolicyAsync(ParcelCompensationPolicy entity, CancellationToken ct = default)
    {
        _db.ParcelCompensationPolicies.Update(entity);
        return Task.CompletedTask;
    }

    public Task UpdateUnidentifiedPackageAsync(UnidentifiedParcelPackage entity, CancellationToken ct = default)
    {
        _db.UnidentifiedParcelPackages.Update(entity);
        return Task.CompletedTask;
    }

    private static Guid[] NormalizeIds(IReadOnlyCollection<Guid> ids, string parameterName)
    {
        if (ids.Any(id => id == Guid.Empty))
            throw new ArgumentException("Ids cannot contain an empty UUID.", parameterName);
        var normalized = ids.Distinct().ToArray();
        if (normalized.Length > 100)
            throw new ArgumentOutOfRangeException(parameterName, "At most 100 distinct ids are allowed.");
        return normalized;
    }

    private static string GetClaimSlaState(
        ParcelClaim claim,
        int decisionBusinessDays,
        int payoutBusinessDays,
        DateTimeOffset now)
    {
        DateTimeOffset? deadline = claim.Status switch
        {
            ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW
                => AddBusinessDays(claim.CreatedAt, decisionBusinessDays),
            ParcelClaimStatus.APPROVED when claim.DecidedAt.HasValue
                => AddBusinessDays(claim.DecidedAt.Value, payoutBusinessDays),
            _ => null,
        };
        if (!deadline.HasValue)
            return "CLOSED";
        if (deadline < now)
            return "BREACHED";
        return deadline <= now.AddHours(24) ? "DUE_SOON" : "ON_TRACK";
    }

    private static DateTimeOffset AddBusinessDays(DateTimeOffset start, int businessDays)
    {
        var result = start;
        for (var remaining = Math.Max(0, businessDays); remaining > 0;)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                remaining--;
        }
        return result;
    }
}
