using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Application.Features.Vouchers.ListVouchers;

/// <summary>
/// Handles GET /v1/admin/vouchers — returns paged voucher list for admin oversight (Q7).
/// Returns only non-soft-deleted vouchers (VoucherConfiguration HasQueryFilter applied at repo layer).
/// </summary>
public sealed class ListVouchersQueryHandler
    : IRequestHandler<ListVouchersQuery, PagedResult<VoucherListItem>>
{
    private readonly IVoucherRepository _vouchers;

    public ListVouchersQueryHandler(IVoucherRepository vouchers)
    {
        _vouchers = vouchers;
    }

    public async Task<PagedResult<VoucherListItem>> Handle(
        ListVouchersQuery request,
        CancellationToken cancellationToken)
    {
        // Parse optional fundingType string (already validated by FluentValidation)
        VoucherFundingType? fundingType = null;
        if (!string.IsNullOrEmpty(request.FundingType))
            fundingType = Enum.Parse<VoucherFundingType>(request.FundingType, ignoreCase: true);

        var (items, total) = await _vouchers.ListAsync(
            ownerOperatorId: request.OwnerOperatorId,
            fundingType: fundingType,
            isActive: request.IsActive,
            page: request.Options.Page,
            pageSize: request.Options.PageSize,
            sortBy: request.Options.SortBy,
            sortDir: request.Options.SortDir,
            ct: cancellationToken);

        var mapped = items
            .Select(v => new VoucherListItem(
                Id: v.Id,
                Code: v.Code,
                Name: v.Name,
                Type: v.Type,
                Value: v.Value,
                FundingType: v.FundingType,
                OwnerOperatorId: v.OwnerOperatorId,
                IsActive: v.IsActive,
                ValidFrom: v.ValidFrom,
                ValidUntil: v.ValidUntil,
                CreatedAt: v.CreatedAt))
            .ToList();

        return PagedResult<VoucherListItem>.Create(
            mapped,
            request.Options.Page,
            request.Options.PageSize,
            total);
    }
}
