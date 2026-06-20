using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Application.Abstractions.Repositories;

/// <summary>
/// Repository contract for the Voucher aggregate.
/// Extends <see cref="IRepository{TEntity,TId}"/> with voucher-specific queries.
/// </summary>
public interface IVoucherRepository : IRepository<Voucher, Guid>
{
    /// <summary>
    /// Returns true if a non-soft-deleted voucher with the given code exists
    /// (partial unique index <c>uq_vouchers_code WHERE deleted_at IS NULL</c>).
    /// </summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Returns the voucher with the given code (non-soft-deleted, respects HasQueryFilter).
    /// </summary>
    Task<Voucher?> FindByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Returns a paged list of vouchers with optional filters for the admin oversight endpoint.
    /// Respects HasQueryFilter (soft-deleted vouchers excluded).
    /// </summary>
    Task<(IReadOnlyList<Voucher> Items, long Total)> ListAsync(
        Guid? ownerOperatorId,
        VoucherFundingType? fundingType,
        bool? isActive,
        int page,
        int pageSize,
        string? sortBy,
        string sortDir,
        CancellationToken ct = default);

    /// <summary>
    /// Adds an <see cref="OperatorVoucherConsent"/> to the change tracker (same transaction as the voucher).
    /// Used by <c>CreateVoucherCommandHandler</c> for OPERATOR_FUNDED consent fan-out.
    /// </summary>
    Task AddConsentAsync(OperatorVoucherConsent consent, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // Operator-scoped queries (Task 14.1b — appended after 14.1 methods; do not modify above)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the voucher with the given id scoped to the specified owner operator.
    /// Returns <c>null</c> if the voucher does not exist, is soft-deleted, or belongs to a
    /// different operator (cross-operator → caller maps to 404 VOUCHER_NOT_FOUND for tenant isolation).
    /// </summary>
    Task<Voucher?> FindByIdAndOwnerAsync(Guid id, Guid ownerOperatorId, CancellationToken ct = default);

    /// <summary>
    /// Returns the voucher with the given id scoped to the specified owner operator,
    /// bypassing the global soft-delete query filter (<c>IgnoreQueryFilters</c>).
    /// Used by DELETE to implement idempotency: an already-soft-deleted voucher owned by the
    /// caller is returned so the handler can detect and no-op; a non-existent or cross-operator
    /// voucher still returns <c>null</c> → 404 VOUCHER_NOT_FOUND (tenant isolation preserved).
    /// </summary>
    Task<Voucher?> FindByIdAndOwnerIgnoringSoftDeleteAsync(Guid id, Guid ownerOperatorId, CancellationToken ct = default);

    /// <summary>
    /// Returns the total number of <see cref="VoucherUsage"/> rows for the given voucher
    /// (all users combined). Used by the PATCH freeze-on-first-use guard (Q6): if &gt;= 1
    /// the economic fields are frozen.
    /// </summary>
    Task<int> CountUsagesAsync(Guid voucherId, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // VoucherUsage methods (Task 14.3 — checkout record + compensation)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Adds a <see cref="VoucherUsage"/> to the change tracker in the same transaction
    /// as the booking creation (same <see cref="BookingDbContext"/> unit-of-work).
    /// </summary>
    Task AddUsageAsync(VoucherUsage usage, CancellationToken ct = default);

    /// <summary>
    /// Returns the number of <see cref="VoucherUsage"/> rows for a (voucher, user) pair.
    /// Used at checkout to check the per-user usage limit before recording a new usage.
    /// </summary>
    Task<int> CountUsagesByUserAsync(Guid voucherId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Physically deletes the <see cref="VoucherUsage"/> row for the given booking (compensation).
    /// Called when a booking is cancelled/refunded after a voucher was applied.
    /// <para>
    /// ON DELETE CASCADE does not fire for a booking soft-delete — this explicit delete is
    /// required per v7:4562. Idempotent: no-op if no row exists for the booking.
    /// </para>
    /// </summary>
    Task DeleteUsageByBookingAsync(Guid bookingId, CancellationToken ct = default);
}
