using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelRepository : IParcelRepository
{
    private readonly ParcelDbContext _db;

    public ParcelRepository(ParcelDbContext db)
    {
        _db = db;
    }

    public async Task<ParcelEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Parcels.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ParcelEntity> AddAsync(ParcelEntity entity, CancellationToken ct)
    {
        await _db.Parcels.AddAsync(entity, ct);
        return entity;
    }

    public void Update(ParcelEntity entity)
        => _db.Parcels.Update(entity);

    public void Remove(ParcelEntity entity)
        => _db.Parcels.Remove(entity);

    public IQueryable<ParcelEntity> Query()
        => _db.Parcels;

    public IQueryable<ParcelEntity> QueryNoTracking()
        => _db.Parcels.AsNoTracking();

    public async Task<ParcelEntity?> FindByParcelCodeAsync(string parcelCode, CancellationToken ct = default)
        => await _db.Parcels.FirstOrDefaultAsync(p => p.ParcelCode == parcelCode, ct);

    // ---- Payment deposit transitions (PENDING_PAYMENT) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositSucceededAsync(
        Guid parcelId, long depositAmount, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_PAYMENT && p.DepositAmount.Amount == depositAmount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositFailedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.EXPIRED)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositExpiredAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.EXPIRED)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    // ---- Additional payment transitions (PENDING_ADDITIONAL_PAYMENT) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalSucceededAsync(
        Guid parcelId, long additionalAmount, Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT && p.AdditionalAmount.Amount == additionalAmount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING)
                .SetProperty(p => p.AdditionalPaymentId, paymentId)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalFailedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalExpiredAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, "PARCEL_ADDITIONAL_PAYMENT_TIMEOUT")
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalExpiredByDeadlineAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT
                && p.AdditionalPaymentDeadline != null
                && p.AdditionalPaymentDeadline <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, "PARCEL_ADDITIONAL_PAYMENT_TIMEOUT")
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    // ---- Operator review transitions (PENDING_OPERATOR_REVIEW) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryApproveReviewAsync(
        Guid parcelId, Guid reviewedByUserId, Money depositAmount, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_PAYMENT)
                .SetProperty(p => p.ReviewDecision, ParcelReviewDecision.APPROVED)
                .SetProperty(p => p.ReviewedByUserId, reviewedByUserId)
                .SetProperty(p => p.ReviewedAt, now)
                .SetProperty(p => p.DepositAmount, depositAmount)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRejectReviewAsync(
        Guid parcelId, Guid reviewedByUserId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.ReviewDecision, ParcelReviewDecision.REJECTED)
                .SetProperty(p => p.ReviewedByUserId, reviewedByUserId)
                .SetProperty(p => p.ReviewedAt, now)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    // ---- Reweigh transitions (PENDING) ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryReweighNoFeeAsync(
        Guid parcelId, decimal actualWeightKg, ParcelSizeCategory actualSizeCategory, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ActualWeightKg, actualWeightKg)
                .SetProperty(p => p.SizeCategory, actualSizeCategory)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryReweighWithFeeAsync(
        Guid parcelId, decimal actualWeightKg, ParcelSizeCategory actualSizeCategory,
        Money additionalAmount, DateTimeOffset deadline, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
                .SetProperty(p => p.ActualWeightKg, actualWeightKg)
                .SetProperty(p => p.SizeCategory, actualSizeCategory)
                .SetProperty(p => p.AdditionalAmount, additionalAmount)
                .SetProperty(p => p.AdditionalPaymentDeadline, deadline)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<bool> TryAssignAdditionalPaymentIdAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.AdditionalPaymentId, paymentId)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0;
    }

    // ---- Hangfire job candidate queries (lightweight projections) ----

    public async Task<IReadOnlyList<Guid>> ListReviewTimedOutIdsAsync(
        DateTimeOffset cutoff, int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING
                && p.CreatedAt <= cutoff)
            .OrderBy(p => p.CreatedAt)
            .Take(maxBatch)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListAdditionalPaymentTimedOutIdsAsync(
        DateTimeOffset now, int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.PENDING_ADDITIONAL_PAYMENT
                && p.AdditionalPaymentDeadline != null
                && p.AdditionalPaymentDeadline <= now)
            .OrderBy(p => p.AdditionalPaymentDeadline)
            .Take(maxBatch)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PendingParcelTripRef>> ListPendingForLoadCheckAsync(
        int maxBatch, CancellationToken ct)
    {
        return await _db.Parcels
            .Where(p => p.Status == ParcelStatus.PENDING)
            .OrderBy(p => p.CreatedAt)
            .Take(maxBatch)
            .Select(p => new PendingParcelTripRef(p.Id, p.TripId, p.CreatedAt))
            .ToListAsync(ct);
    }

    // ---- Hangfire job atomic transitions ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryAutoRejectReviewAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_OPERATOR_REVIEW
                && p.ReviewDecision == ParcelReviewDecision.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.ReviewDecision, ParcelReviewDecision.REJECTED)
                .SetProperty(p => p.ReviewedAt, now)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryAutoRejectPendingAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.PENDING)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.REJECTED)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    // ---- Phase 6: Loading / Unloading ----

    public async Task<ParcelPaymentTransitionSnapshot?> TryMarkLoadedAsync(
        Guid parcelId, Guid tripId, string parcelCode, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING
                && p.TripId == tripId
                && p.ParcelCode == parcelCode)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.LOADED)
                .SetProperty(p => p.LoadedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryUnloadToPendingConfirmAsync(
        Guid parcelId, Guid deliveryToken, DateTimeOffset deliveryTokenExpiresAt, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId && p.Status == ParcelStatus.IN_TRANSIT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERED_PENDING_CONFIRM)
                .SetProperty(p => p.UnloadedAt, now)
                .SetProperty(p => p.DeliveredPendingConfirmAt, now)
                .SetProperty(p => p.DeliveryToken, deliveryToken)
                .SetProperty(p => p.DeliveryTokenExpiresAt, deliveryTokenExpiresAt)
                .SetProperty(p => p.DeliveryTokenRevokedAt, (DateTimeOffset?)null)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkSetInTransitByTripIdAsync(Guid tripId, DateTimeOffset now, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            UPDATE vietride_parcel.parcels
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                updated_at = @now
            WHERE trip_id = @trip_id
              AND status = CAST(@source_status AS vietride_parcel.parcel_status)
            RETURNING id, parcel_code, operator_id, trip_id, status::text, deposit_amount, additional_amount;
            """,
            command =>
            {
                AddParameter(command, "target_status", ParcelStatus.IN_TRANSIT.ToString());
                AddParameter(command, "source_status", ParcelStatus.LOADED.ToString());
                AddParameter(command, "trip_id", tripId);
                AddParameter(command, "now", now);
            },
            ct);
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkSetPendingOperatorActionByTripIdAsync(Guid tripId, DateTimeOffset now, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            UPDATE vietride_parcel.parcels
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                updated_at = @now
            WHERE trip_id = @trip_id
              AND status IN (
                  CAST(@loaded_status AS vietride_parcel.parcel_status),
                  CAST(@in_transit_status AS vietride_parcel.parcel_status))
            RETURNING id, parcel_code, operator_id, trip_id, status::text, deposit_amount, additional_amount;
            """,
            command =>
            {
                AddParameter(command, "target_status", ParcelStatus.PENDING_OPERATOR_ACTION.ToString());
                AddParameter(command, "loaded_status", ParcelStatus.LOADED.ToString());
                AddParameter(command, "in_transit_status", ParcelStatus.IN_TRANSIT.ToString());
                AddParameter(command, "trip_id", tripId);
                AddParameter(command, "now", now);
            },
            ct);
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRequestTransferAsync(
        Guid parcelId, Guid operatorId, Guid targetTripId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.OperatorId == operatorId
                && p.TripId != targetTripId
                && (p.Status == ParcelStatus.LOADED || p.Status == ParcelStatus.IN_TRANSIT))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.PENDING_TRANSFER_CONFIRM)
                .SetProperty(p => p.TransferTargetTripId, targetTripId)
                .SetProperty(p => p.TransferRequestedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryConfirmTransferAsync(
        Guid parcelId, Guid targetTripId, string parcelCode, Guid confirmedByUserId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.Status == ParcelStatus.PENDING_TRANSFER_CONFIRM
                && p.TransferTargetTripId == targetTripId
                && p.ParcelCode == parcelCode)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.TripId, targetTripId)
                .SetProperty(p => p.Status, ParcelStatus.LOADED)
                .SetProperty(p => p.TransferConfirmedAt, now)
                .SetProperty(p => p.TransferConfirmedByUserId, confirmedByUserId)
                .SetProperty(p => p.LoadedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryReturnAsync(
        Guid parcelId, Guid operatorId, Guid returnedByUserId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.OperatorId == operatorId
                && (p.Status == ParcelStatus.PENDING_OPERATOR_ACTION || p.Status == ParcelStatus.TRANSFER_ESCALATED))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.RETURNED)
                .SetProperty(p => p.ReturnReason, reason)
                .SetProperty(p => p.ReturnedAt, now)
                .SetProperty(p => p.ReturnedByUserId, returnedByUserId)
                .SetProperty(p => p.UpdatedAt, now), ct);

        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryRejectPreAcceptanceByTripIdAsync(
        Guid tripId, DateTimeOffset now, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            UPDATE vietride_parcel.parcels
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                rejection_reason = @reason,
                rejected_at = @now,
                updated_at = @now
            WHERE trip_id = @trip_id
              AND status IN (
                  CAST(@pending_payment_status AS vietride_parcel.parcel_status),
                  CAST(@pending_review_status AS vietride_parcel.parcel_status))
            RETURNING id, parcel_code, operator_id, trip_id, status::text, deposit_amount, additional_amount;
            """,
            command =>
            {
                AddParameter(command, "target_status", ParcelStatus.REJECTED.ToString());
                AddParameter(command, "pending_payment_status", ParcelStatus.PENDING_PAYMENT.ToString());
                AddParameter(command, "pending_review_status", ParcelStatus.PENDING_OPERATOR_REVIEW.ToString());
                AddParameter(command, "trip_id", tripId);
                AddParameter(command, "reason", "TRIP_CANCELLED");
                AddParameter(command, "now", now);
            },
            ct);
    }

    public async Task<IReadOnlyList<ParcelEventSnapshot>> TryCancelPendingByTripIdAsync(
        Guid tripId, DateTimeOffset now, CancellationToken ct)
    {
        return await ExecuteBulkReturningAsync(
            """
            UPDATE vietride_parcel.parcels
            SET status = CAST(@target_status AS vietride_parcel.parcel_status),
                cancellation_reason = @reason,
                updated_at = @now
            WHERE trip_id = @trip_id
              AND status = CAST(@source_status AS vietride_parcel.parcel_status)
            RETURNING id, parcel_code, operator_id, trip_id, status::text, deposit_amount, additional_amount;
            """,
            command =>
            {
                AddParameter(command, "target_status", ParcelStatus.CANCELLED.ToString());
                AddParameter(command, "source_status", ParcelStatus.PENDING.ToString());
                AddParameter(command, "trip_id", tripId);
                AddParameter(command, "reason", "TRIP_CANCELLED");
                AddParameter(command, "now", now);
            },
            ct);
    }

    // ---- Phase 6: Queries ----

    public async Task<PagedResult<ParcelEntity>> ListReceivedByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Parcels
            .Where(p => p.RecipientUserId == userId)
            .OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<ParcelEntity>.Create(items, page, pageSize, total);
    }

    // ---- Phase 7: Delivery Token ----

    public async Task<ParcelEntity?> FindByDeliveryTokenAsync(Guid token, CancellationToken ct)
        => await _db.Parcels.FirstOrDefaultAsync(p => p.DeliveryToken == token, ct);

    public async Task<ParcelPaymentTransitionSnapshot?> TryConfirmDeliveryAsync(
        Guid parcelId, Guid token, string ip, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.DeliveryToken == token
                && p.DeliveryTokenRevokedAt == null
                && p.DeliveryTokenExpiresAt != null
                && p.DeliveryTokenExpiresAt > now
                && (p.Status == ParcelStatus.DELIVERED_PENDING_CONFIRM || p.Status == ParcelStatus.DELIVERY_REJECTED))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERY_CONFIRMED)
                .SetProperty(p => p.ConfirmedAt, now)
                .SetProperty(p => p.ConfirmedByIp, ip)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    public async Task<ParcelPaymentTransitionSnapshot?> TryRejectDeliveryAsync(
        Guid parcelId, Guid token, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.Parcels
            .Where(p => p.Id == parcelId
                && p.DeliveryToken == token
                && p.DeliveryTokenRevokedAt == null
                && p.DeliveryTokenExpiresAt != null
                && p.DeliveryTokenExpiresAt > now
                && p.Status == ParcelStatus.DELIVERED_PENDING_CONFIRM)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, ParcelStatus.DELIVERY_REJECTED)
                .SetProperty(p => p.RejectedAt, now)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return affected > 0 ? BuildSnapshot(await _db.Parcels.AsNoTracking().FirstAsync(p => p.Id == parcelId, ct)) : null;
    }

    private static ParcelPaymentTransitionSnapshot BuildSnapshot(ParcelEntity p)
        => new(
            p.Id, p.ParcelCode, p.Status, p.DepositAmount.Amount, p.AdditionalAmount.Amount,
            p.OperatorId, p.TripId, p.BookingId, p.SenderUserId, p.SizeCategory, p.AdditionalPaymentId);

    private async Task<IReadOnlyList<ParcelEventSnapshot>> ExecuteBulkReturningAsync(
        string sql,
        Action<System.Data.Common.DbCommand> configure,
        CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        configure(command);

        var snapshots = new List<ParcelEventSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            snapshots.Add(new ParcelEventSnapshot(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                Enum.Parse<ParcelStatus>(reader.GetString(4)),
                reader.FieldCount > 5 ? reader.GetInt64(5) : 0,
                reader.FieldCount > 6 ? reader.GetInt64(6) : 0));
        }

        return snapshots;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
