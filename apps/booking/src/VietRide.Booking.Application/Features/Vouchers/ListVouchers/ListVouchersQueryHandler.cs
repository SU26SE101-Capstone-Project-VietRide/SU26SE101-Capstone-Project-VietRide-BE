using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Booking.Application.Features.Vouchers.ListVouchers;

/// <summary>
/// Handles voucher list queries for platform-admin and operator-owned views.
/// Returns only non-soft-deleted vouchers (VoucherConfiguration HasQueryFilter applied at repo layer).
/// </summary>
public sealed class ListVouchersQueryHandler
    : IRequestHandler<ListVouchersQuery, PagedResult<VoucherListItem>>
{
    private static readonly HashSet<string> AllowedSortFields =
    [
        "createdAt", "validFrom", "validUntil", "code", "name", "isActive", "usedCount",
    ];

    private readonly IVoucherRepository _vouchers;

    public ListVouchersQueryHandler(IVoucherRepository vouchers)
    {
        _vouchers = vouchers;
    }

    public async Task<PagedResult<VoucherListItem>> Handle(
        ListVouchersQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Options.SortBy)
            && !AllowedSortFields.Contains(request.Options.SortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", "Unsupported voucher sort field.");

        // Parse optional fundingType string (already validated by FluentValidation)
        VoucherFundingType? fundingType = null;
        if (!string.IsNullOrEmpty(request.FundingType))
            fundingType = Enum.Parse<VoucherFundingType>(request.FundingType, ignoreCase: true);

        VoucherType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
            type = Enum.Parse<VoucherType>(request.Type, ignoreCase: true);

        var validity = request.ValidAt.HasValue
            ? BusinessTime.GetUtcDayRange(request.ValidAt.Value)
            : (UtcRange?)null;

        var (items, total) = await _vouchers.ListAsync(
            ownerOperatorId: request.OwnerOperatorId,
            platformOnly: request.PlatformOnly,
            fundingType: fundingType,
            isActive: request.IsActive,
            page: request.Options.Page,
            pageSize: request.Options.PageSize,
            sortBy: request.Options.SortBy,
            sortDir: request.Options.SortDir,
            ct: cancellationToken,
            search: request.Search,
            service: request.Service?.Trim().ToUpperInvariant(),
            type: type,
            validFromInclusive: validity?.FromUtc,
            validUntilExclusive: validity?.ToUtcExclusive);

        var usageCounts = await _vouchers.GetUsageCountsAsync(
            items.Select(item => item.Id).ToArray(),
            cancellationToken) ?? new Dictionary<Guid, int>();

        var mapped = items.Select(v => new VoucherListItem(
            v.Id,
            v.Code,
            v.Name,
            v.Type.ToString(),
            v.Value,
            v.MinOrderAmount.Amount,
            v.MaxDiscountAmount?.Amount,
            v.TotalUsageLimit,
            v.PerUserLimit,
            v.NewUserOnly,
            v.ApplicableServices,
            v.ApplicablePaymentMethods,
            v.ApplicableOperatorIds,
            v.ApplicableRouteIds,
            v.FundingType.ToString(),
            v.OwnerOperatorId,
            v.IsActive,
            v.ValidFrom,
            v.ValidUntil,
            v.CreatedAt,
            usageCounts.GetValueOrDefault(v.Id))).ToList();

        return PagedResult<VoucherListItem>.Create(
            mapped,
            request.Options.Page,
            request.Options.PageSize,
            total);
    }
}
