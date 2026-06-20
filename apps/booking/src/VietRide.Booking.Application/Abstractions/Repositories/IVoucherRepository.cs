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
}
