using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Application.Features.Vouchers.ListVouchers;

/// <summary>
/// Query for GET /v1/admin/vouchers — admin oversight list of all vouchers (SYSTEM_ADMIN, Q7).
/// <para>
/// Optional filters: <see cref="OwnerOperatorId"/> / <see cref="FundingType"/> / <see cref="IsActive"/>.
/// <see cref="FundingType"/> is a string (VIETRIDE_FUNDED | OPERATOR_FUNDED) parsed in the handler —
/// keeping the Application/Api boundary free of direct Domain enum dependencies at the query level.
/// Returns only non-soft-deleted vouchers (respects VoucherConfiguration HasQueryFilter).
/// </para>
/// </summary>
public sealed record ListVouchersQuery(
    Guid? OwnerOperatorId,
    /// <summary>VIETRIDE_FUNDED or OPERATOR_FUNDED string. Null = no filter.</summary>
    string? FundingType,
    bool? IsActive,
    QueryOptions Options) : IRequest<PagedResult<VoucherListItem>>;
