using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

/// <summary>
/// Repository contract for the OperatorVoucherConsent aggregate.
/// Extends <see cref="IRepository{TEntity,TId}"/> with consent-specific queries.
/// </summary>
public interface IOperatorVoucherConsentRepository : IRepository<OperatorVoucherConsent, Guid>
{
    /// <summary>
    /// Returns the consent row with the given id scoped to the specified operator.
    /// Returns <c>null</c> if the consent does not exist or belongs to a different operator
    /// (cross-operator → caller maps to 403/404 for tenant isolation).
    /// </summary>
    Task<OperatorVoucherConsent?> FindByIdAndOperatorAsync(
        Guid id,
        Guid operatorId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paged list of consent rows for the specified operator, optionally filtered by status.
    /// Used by GET /v1/operator/voucher-consents.
    /// </summary>
    Task<IReadOnlyList<OperatorVoucherConsent>> ListByOperatorAsync(
        Guid operatorId,
        OperatorVoucherConsentStatus? status,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all consent rows for a given voucher (admin view).
    /// Used by GET /v1/admin/vouchers/{voucherId}/consents.
    /// </summary>
    Task<IReadOnlyList<OperatorVoucherConsent>> ListByVoucherAsync(
        Guid voucherId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the ACCEPTED consent row for a (voucher, operator) pair, or <c>null</c> if none.
    /// Used at checkout to verify operator opted in to an OPERATOR_FUNDED admin voucher.
    /// </summary>
    Task<OperatorVoucherConsent?> FindAcceptedByVoucherAndOperatorAsync(
        Guid voucherId,
        Guid operatorId,
        CancellationToken ct = default);
}
