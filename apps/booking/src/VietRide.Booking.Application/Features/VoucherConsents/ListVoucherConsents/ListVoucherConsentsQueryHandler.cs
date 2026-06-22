using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Handles GET /v1/operator/voucher-consents — returns operator-scoped consent rows
/// with optional status filter. Tenant isolation enforced by scoping to <c>CallerOperatorId</c>.
/// </summary>
public sealed class ListVoucherConsentsQueryHandler
    : IRequestHandler<ListVoucherConsentsQuery, ListVoucherConsentsResult>
{
    private readonly IOperatorVoucherConsentRepository _consents;

    public ListVoucherConsentsQueryHandler(IOperatorVoucherConsentRepository consents)
    {
        _consents = consents;
    }

    public async Task<ListVoucherConsentsResult> Handle(
        ListVoucherConsentsQuery request,
        CancellationToken cancellationToken)
    {
        // Parse optional status string — validated here so the Api layer stays domain-free.
        OperatorVoucherConsentStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<OperatorVoucherConsentStatus>(request.Status, ignoreCase: true, out var s))
            {
                throw new CodedValidationException(
                    "INVALID_STATUS",
                    $"status must be one of: {string.Join(", ", Enum.GetNames<OperatorVoucherConsentStatus>())}.");
            }

            parsedStatus = s;
        }

        var rows = await _consents.ListByOperatorAsync(
            request.CallerOperatorId,
            parsedStatus,
            cancellationToken);

        var items = rows.Select(c => new VoucherConsentListItem(
            Id: c.Id,
            VoucherId: c.VoucherId,
            VoucherCode: c.Voucher!.Code,
            VoucherType: c.Voucher.Type.ToString(),
            VoucherValue: c.Voucher.Value,
            ValidFrom: c.Voucher.ValidFrom,
            ValidUntil: c.Voucher.ValidUntil,
            MinOrderAmount: c.Voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: c.Voucher.MaxDiscountAmount?.Amount,
            ApplicableRouteIds: c.Voucher.ApplicableRouteIds.Count > 0
                ? c.Voucher.ApplicableRouteIds.AsReadOnly()
                : null,
            Status: c.Status.ToString(),
            RequestedAt: c.RequestedAt,
            RespondedAt: c.RespondedAt,
            RespondedByUserId: c.RespondedByUserId))
            .ToList();

        return new ListVoucherConsentsResult(items);
    }
}
