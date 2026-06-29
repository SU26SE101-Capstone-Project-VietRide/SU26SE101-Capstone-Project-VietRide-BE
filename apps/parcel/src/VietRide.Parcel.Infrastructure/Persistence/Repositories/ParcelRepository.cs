using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
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

    private static ParcelPaymentTransitionSnapshot BuildSnapshot(ParcelEntity p)
        => new(
            p.Id, p.ParcelCode, p.Status, p.DepositAmount.Amount, p.AdditionalAmount.Amount,
            p.OperatorId, p.TripId, p.BookingId, p.SenderUserId, p.SizeCategory, p.AdditionalPaymentId);
}
