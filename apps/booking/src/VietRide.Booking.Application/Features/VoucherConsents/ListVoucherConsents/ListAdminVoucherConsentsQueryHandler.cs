using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Handles GET /v1/admin/vouchers/{voucherId}/consents — returns all consent rows for a voucher.
/// SYSTEM_ADMIN only; no tenant-scoping (admin sees all operators).
/// </summary>
public sealed class ListAdminVoucherConsentsQueryHandler
    : IRequestHandler<ListAdminVoucherConsentsQuery, AdminVoucherConsentsResult>
{
    private readonly IOperatorVoucherConsentRepository _consents;

    public ListAdminVoucherConsentsQueryHandler(IOperatorVoucherConsentRepository consents)
    {
        _consents = consents;
    }

    public async Task<AdminVoucherConsentsResult> Handle(
        ListAdminVoucherConsentsQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await _consents.ListByVoucherAsync(request.VoucherId, cancellationToken);

        var items = rows.Select(c => new AdminVoucherConsentItem(
            Id: c.Id,
            OperatorId: c.OperatorId,
            VoucherId: c.VoucherId,
            Status: c.Status.ToString(),
            RequestedAt: c.RequestedAt,
            RespondedAt: c.RespondedAt,
            RespondedByUserId: c.RespondedByUserId,
            RejectReason: c.RejectReason))
            .ToList();

        return new AdminVoucherConsentsResult(
            VoucherId: request.VoucherId,
            Items: items);
    }
}
