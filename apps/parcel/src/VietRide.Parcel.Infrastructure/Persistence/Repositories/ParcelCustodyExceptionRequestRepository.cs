using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelCustodyExceptionRequestRepository
    : IParcelCustodyExceptionRequestRepository
{
    private readonly ParcelDbContext _db;

    public ParcelCustodyExceptionRequestRepository(ParcelDbContext db)
    {
        _db = db;
    }

    public Task<ParcelCustodyExceptionRequest?> GetByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken ct = default)
        => _db.ParcelCustodyExceptionRequests
            .FirstOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, ct);

    public Task<ParcelCustodyExceptionRequest?> GetLatestByParcelAsync(
        Guid parcelId,
        CancellationToken ct = default)
        => _db.ParcelCustodyExceptionRequests
            .Where(item => item.ParcelId == parcelId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<ParcelCustodyExceptionRequest?> GetLatestByParcelForUpdateAsync(
        Guid parcelId,
        CancellationToken ct = default)
    {
        var matches = await _db.ParcelCustodyExceptionRequests
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_custody_exception_requests
                WHERE parcel_id = {parcelId}
                ORDER BY created_at DESC, id DESC
                LIMIT 1
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(ct);
        return matches.SingleOrDefault();
    }

    public Task<ParcelCustodyExceptionRequest?> GetByIncidentAsync(
        Guid incidentId,
        CancellationToken ct = default)
        => _db.ParcelCustodyExceptionRequests
            .FirstOrDefaultAsync(item => item.IncidentId == incidentId, ct);

    public async Task<ParcelCustodyExceptionRequest?> GetByIncidentForUpdateAsync(
        Guid incidentId,
        CancellationToken ct = default)
    {
        var matches = await _db.ParcelCustodyExceptionRequests
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_custody_exception_requests
                WHERE incident_id = {incidentId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(ct);
        return matches.SingleOrDefault();
    }

    public async Task<IReadOnlySet<Guid>> ListPendingIncidentIdsAsync(
        IReadOnlyCollection<Guid> incidentIds,
        CancellationToken ct = default)
    {
        if (incidentIds.Count == 0)
            return new HashSet<Guid>();
        var ids = await _db.ParcelCustodyExceptionRequests
            .AsNoTracking()
            .Where(item => incidentIds.Contains(item.IncidentId)
                && item.Status == ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL)
            .Select(item => item.IncidentId)
            .ToArrayAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<ParcelCustodyExceptionRequest>> ListLatestByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default)
    {
        if (parcelIds.Count == 0)
            return [];
        var requests = await _db.ParcelCustodyExceptionRequests
            .AsNoTracking()
            .Where(item => parcelIds.Contains(item.ParcelId))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToArrayAsync(ct);
        return requests
            .GroupBy(item => item.ParcelId)
            .Select(group => group.First())
            .ToArray();
    }

    public async Task AddAsync(
        ParcelCustodyExceptionRequest entity,
        CancellationToken ct = default)
        => await _db.ParcelCustodyExceptionRequests.AddAsync(entity, ct);
}
