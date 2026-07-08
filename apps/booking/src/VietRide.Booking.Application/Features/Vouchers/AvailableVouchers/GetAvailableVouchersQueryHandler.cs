using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Vouchers.AvailableVouchers;

public sealed class GetAvailableVouchersQueryHandler
    : IRequestHandler<GetAvailableVouchersQuery, IReadOnlyList<AvailableVoucherItem>>
{
    private readonly IVoucherRepository _vouchers;
    private readonly ITripServiceClient _tripClient;
    private readonly IVoucherService _voucherService;
    private readonly IClock _clock;

    public GetAvailableVouchersQueryHandler(
        IVoucherRepository vouchers,
        ITripServiceClient tripClient,
        IVoucherService voucherService,
        IClock clock)
    {
        _vouchers = vouchers;
        _tripClient = tripClient;
        _voucherService = voucherService;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AvailableVoucherItem>> Handle(
        GetAvailableVouchersQuery request,
        CancellationToken cancellationToken)
    {
        var service = request.Service.Trim().ToUpperInvariant();
        var now = _clock.UtcNow;
        var operatorId = request.OperatorId;
        var routeId = request.RouteId;

        if (request.TripId.HasValue && (!operatorId.HasValue || !routeId.HasValue))
        {
            var trip = await _tripClient.GetTripSnapshotAsync(request.TripId.Value, cancellationToken);
            if (trip is not null)
            {
                operatorId ??= trip.OperatorId;
                routeId ??= trip.RouteId;
            }
        }

        var orderAmount = Money.FromRaw(request.OrderAmount ?? 0);
        var paymentMethod = request.PaymentMethod?.Trim().ToUpperInvariant();

        if (!operatorId.HasValue || !routeId.HasValue)
        {
            return [];
        }

        var candidates = await _vouchers.QueryNoTracking()
            .Where(v => v.IsActive && v.ValidFrom <= now && v.ValidUntil >= now)
            .Where(v => v.ApplicableServices.Count == 0 || v.ApplicableServices.Contains(service))
            .Where(v => paymentMethod == null || v.ApplicablePaymentMethods.Count == 0 || v.ApplicablePaymentMethods.Contains(paymentMethod))
            .Where(v => !operatorId.HasValue || v.OwnerOperatorId == null || v.OwnerOperatorId == operatorId.Value)
            .Where(v => !operatorId.HasValue || v.ApplicableOperatorIds.Count == 0 || v.ApplicableOperatorIds.Contains(operatorId.Value))
            .Where(v => !routeId.HasValue || v.ApplicableRouteIds.Count == 0 || v.ApplicableRouteIds.Contains(routeId.Value))
            .OrderBy(v => v.ValidUntil)
            .Take(50)
            .ToListAsync(cancellationToken);

        var available = new List<AvailableVoucherItem>();
        foreach (var voucher in candidates)
        {
            try
            {
                var validation = await _voucherService.ValidateAndComputeDiscountAsync(
                    voucher.Code,
                    operatorId.Value,
                    routeId.Value,
                    request.UserId,
                    orderAmount,
                    now,
                    cancellationToken,
                    service,
                    paymentMethod);

                available.Add(new AvailableVoucherItem(
                    voucher.Id,
                    voucher.Code,
                    voucher.Name,
                    voucher.Type.ToString(),
                    voucher.Value,
                    voucher.MinOrderAmount.Amount,
                    voucher.MaxDiscountAmount?.Amount,
                    validation.Discount.Amount,
                    voucher.ApplicableServices,
                    voucher.ApplicablePaymentMethods,
                    voucher.ValidUntil));
            }
            catch (CodedValidationException)
            {
                // Available endpoint is a preview; checkout remains the final source of truth.
            }
            catch (CodedNotFoundException)
            {
                // Treat inactive/deleted race as unavailable.
            }
        }

        return available;
    }
}
