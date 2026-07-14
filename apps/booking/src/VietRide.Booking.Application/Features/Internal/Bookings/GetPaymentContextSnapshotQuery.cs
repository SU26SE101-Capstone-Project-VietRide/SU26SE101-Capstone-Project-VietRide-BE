using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record GetPaymentContextSnapshotQuery(
    string ReferenceType,
    Guid ReferenceId) : IRequest<PaymentContextSnapshotDto>;

public sealed record PaymentContextSnapshotDto(
    int Version,
    bool CanBackfill,
    string? QuarantineReason,
    IReadOnlyList<PaymentAllocationSnapshotDto> Allocations);

public sealed record PaymentAllocationSnapshotDto(
    Guid ReferenceId,
    string ReferenceType,
    Guid OperatorId,
    Guid TripId,
    long GrossAmount,
    long VoucherVietRideFundedAmount,
    long VoucherOperatorFundedAmount);

public sealed class GetPaymentContextSnapshotQueryHandler
    : IRequestHandler<GetPaymentContextSnapshotQuery, PaymentContextSnapshotDto>
{
    private readonly IBookingRepository _bookings;

    public GetPaymentContextSnapshotQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<PaymentContextSnapshotDto> Handle(
        GetPaymentContextSnapshotQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ReferenceId == Guid.Empty
            || request.ReferenceType is not ("BOOKING" or "BOOKING_GROUP"))
        {
            throw new CodedValidationException(
                "PAYMENT_CONTEXT_REFERENCE_INVALID",
                "Booking payment context supports BOOKING or BOOKING_GROUP references.");
        }

        var query = _bookings.QueryNoTracking();
        query = request.ReferenceType == "BOOKING"
            ? query.Where(booking => booking.Id == request.ReferenceId)
            : query.Where(booking => booking.BookingGroupId == request.ReferenceId);

        var snapshots = await query
            .OrderBy(booking => booking.TripDirection)
            .ThenBy(booking => booking.Id)
            .Select(booking => new
            {
                booking.Id,
                booking.OperatorId,
                booking.TripId,
                GrossAmount = booking.TotalAmount.Amount + booking.DiscountAmount.Amount,
                PaidAmount = booking.TotalAmount.Amount,
                DiscountAmount = booking.DiscountAmount.Amount,
            })
            .ToListAsync(cancellationToken);

        if (snapshots.Count == 0)
            throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking payment reference was not found.");

        if (snapshots.Any(snapshot => snapshot.DiscountAmount > 0))
        {
            return new PaymentContextSnapshotDto(
                1,
                false,
                "LEGACY_VOUCHER_FUNDING_UNRESOLVED",
                []);
        }

        return new PaymentContextSnapshotDto(
            1,
            true,
            null,
            snapshots.Select(snapshot => new PaymentAllocationSnapshotDto(
                snapshot.Id,
                "BOOKING",
                snapshot.OperatorId,
                snapshot.TripId,
                snapshot.GrossAmount,
                0,
                0)).ToArray());
    }
}
