using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelStopDepartureApprovalRepository
    : IParcelStopDepartureApprovalRepository
{
    private readonly ParcelDbContext _db;

    public ParcelStopDepartureApprovalRepository(ParcelDbContext db)
    {
        _db = db;
    }

    public async Task AcquireTripStopLockAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default)
    {
        var lockKey = BitConverter.ToInt64(tripId.ToByteArray(), 0)
            ^ BitConverter.ToInt64(stopId.ToByteArray(), 0);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey});",
            ct);
    }

    public Task<ParcelStopDepartureApprovalRequest?> GetByIdAsync(
        Guid requestId,
        CancellationToken ct = default)
        => _db.ParcelStopDepartureApprovalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == requestId, ct);

    public async Task<ParcelStopDepartureApprovalRequest?> GetByIdForUpdateAsync(
        Guid requestId,
        CancellationToken ct = default)
    {
        var matches = await _db.ParcelStopDepartureApprovalRequests
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_stop_departure_approval_requests
                WHERE id = {requestId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(ct);
        return matches.SingleOrDefault();
    }

    public Task<ParcelStopDepartureApprovalRequest?> GetByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken ct = default)
        => _db.ParcelStopDepartureApprovalRequests
            .FirstOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, ct);

    public Task<ParcelStopDepartureApprovalRequest?> GetLatestByTripStopAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default)
        => _db.ParcelStopDepartureApprovalRequests
            .AsNoTracking()
            .Where(item => item.TripId == tripId && item.StopId == stopId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<ParcelStopDepartureApprovalRequest?> GetLatestByTripStopForUpdateAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default)
    {
        var matches = await _db.ParcelStopDepartureApprovalRequests
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_stop_departure_approval_requests
                WHERE trip_id = {tripId} AND stop_id = {stopId}
                ORDER BY created_at DESC, id DESC
                LIMIT 1
                FOR UPDATE
                """)
            .AsTracking()
            .ToListAsync(ct);
        return matches.SingleOrDefault();
    }

    public async Task<IReadOnlyList<ParcelStopDepartureApprovalRequest>> ListPendingByOperatorAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.ParcelStopDepartureApprovalRequests
            .AsNoTracking()
            .Where(item => item.OperatorId == operatorId
                && item.Status == ParcelStopDepartureApprovalStatus.PENDING_APPROVAL)
            .OrderByDescending(item => item.RequestedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<ParcelStopDepartureApprovalRequest>> ListPendingByTripForUpdateAsync(
        Guid tripId,
        Guid? stopId = null,
        CancellationToken ct = default)
    {
        var query = stopId.HasValue
            ? _db.ParcelStopDepartureApprovalRequests.FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_stop_departure_approval_requests
                WHERE trip_id = {tripId} AND stop_id = {stopId.Value} AND status = 'PENDING_APPROVAL'
                ORDER BY id
                FOR UPDATE
                """)
            : _db.ParcelStopDepartureApprovalRequests.FromSqlInterpolated($"""
                SELECT *
                FROM vietride_parcel.parcel_stop_departure_approval_requests
                WHERE trip_id = {tripId} AND status = 'PENDING_APPROVAL'
                ORDER BY id
                FOR UPDATE
                """);
        return await query.AsTracking().ToArrayAsync(ct);
    }

    public async Task AddAsync(
        ParcelStopDepartureApprovalRequest entity,
        CancellationToken ct = default)
        => await _db.ParcelStopDepartureApprovalRequests.AddAsync(entity, ct);
}
