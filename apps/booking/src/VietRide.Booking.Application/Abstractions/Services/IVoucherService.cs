using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Abstractions.Services;

/// <summary>
/// Shared voucher validation + discount-application service used at checkout.
/// <para>
/// Implements the canonical checkout validation order (Q8 RESOLVED, re-plan 2026-06-19):
/// (1) exists + not soft-deleted + is_active;
/// (2) within valid_from..valid_until window;
/// (3) applicability — operator-scope + route-scope + consent gate;
/// (4) min_order_amount met;
/// (5) usage limits (total + per-user) not exceeded;
/// (6) compute discount (PERCENT_OFF capped at max_discount_amount, rounded half-up AwayFromZero).
/// </para>
/// <para>
/// Usage pattern: call <see cref="ValidateAndComputeDiscountAsync"/> BEFORE creating the Booking
/// entity (to get the discount amount), then create the entity with the correct amounts, then
/// call <see cref="RecordUsageAsync"/> with the new booking id (both run in the same
/// <c>TransactionBehavior</c> unit-of-work). Call <see cref="CompensateAsync"/> on any failure
/// path after a usage row was written.
/// </para>
/// </summary>
public interface IVoucherService
{
    /// <summary>
    /// Validates the voucher identified by <paramref name="voucherCode"/> against the booking
    /// context, computes and returns the discount, but does NOT write any database row.
    /// <para>
    /// Applicability branches (re-plan Q8):
    /// <list type="bullet">
    ///   <item>(a) <c>owner_operator_id == operatorId</c> — operator-owned, self-funded. Skips
    ///   operator-scope check and consent check; all other filters still apply.</item>
    ///   <item>(b) <c>owner_operator_id IS NULL</c> — platform voucher.
    ///   <c>VIETRIDE_FUNDED</c>: operator scope is unrestricted (applies to all operators);
    ///   <c>OPERATOR_FUNDED</c>: requires an ACCEPTED consent row for <paramref name="operatorId"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <returns>A <see cref="VoucherValidationResult"/> carrying the voucher id and computed discount.</returns>
    /// <exception cref="VietRide.Shared.Application.Exceptions.CodedNotFoundException">
    /// Thrown with code <c>VOUCHER_NOT_FOUND</c> if the voucher does not exist, is soft-deleted,
    /// or is inactive (<c>is_active = false</c>). An inactive voucher is treated as not-found so
    /// that callers cannot probe activation state.
    /// </exception>
    /// <exception cref="VietRide.Shared.Application.Exceptions.CodedValidationException">
    /// Thrown with the appropriate registered code for each validation failure:
    /// <c>VOUCHER_EXPIRED</c>, <c>VOUCHER_NOT_APPLICABLE</c>,
    /// <c>VOUCHER_MIN_ORDER_NOT_MET</c>, <c>VOUCHER_USAGE_LIMIT_REACHED</c>,
    /// <c>VOUCHER_USER_LIMIT_REACHED</c>.
    /// </exception>
    Task<VoucherValidationResult> ValidateAndComputeDiscountAsync(
        string voucherCode,
        Guid operatorId,
        Guid routeId,
        Guid userId,
        Money orderAmount,
        DateTimeOffset now,
        CancellationToken ct = default,
        string service = "BOOKING",
        string? paymentMethod = null);

    /// <summary>
    /// Writes the <c>VoucherUsage</c> row in the current unit-of-work (same DbContext transaction
    /// as the booking creation). Must be called after <see cref="ValidateAndComputeDiscountAsync"/>
    /// and after the booking entity has been persisted (to have a valid <paramref name="bookingId"/>).
    /// </summary>
    Task<Guid> RecordUsageAsync(
        Guid voucherId,
        Guid userId,
        Guid bookingId,
        Guid? bookingGroupId,
        Money discountAmount,
        CancellationToken ct = default);

    Task<Guid> RecordUsageForReferenceAsync(
        Guid voucherId,
        Guid userId,
        string? referenceType,
        Guid referenceId,
        Guid? bookingGroupId,
        Money discountAmount,
        CancellationToken ct = default);

    /// <summary>
    /// Physically deletes the <c>VoucherUsage</c> row for the given booking (compensation path).
    /// <para>
    /// Called when a booking is cancelled or payment fails after the usage row was written.
    /// ON DELETE CASCADE does not fire for a booking soft-delete, so this explicit delete is
    /// required per v7:4562. Idempotent — no-op if no usage row exists for the booking.
    /// </para>
    /// </summary>
    Task CompensateAsync(Guid bookingId, CancellationToken ct = default);

    Task CompensateByReferenceAsync(string referenceType, Guid referenceId, CancellationToken ct = default);
}

/// <summary>
/// Result of <see cref="IVoucherService.ValidateAndComputeDiscountAsync"/>.
/// </summary>
public sealed record VoucherValidationResult(
    Guid VoucherId,
    Money Discount);
