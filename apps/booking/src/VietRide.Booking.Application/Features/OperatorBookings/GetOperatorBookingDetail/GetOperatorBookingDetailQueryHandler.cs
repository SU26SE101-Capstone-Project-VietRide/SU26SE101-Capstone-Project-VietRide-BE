using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

public sealed class GetOperatorBookingDetailQueryHandler : IRequestHandler<GetOperatorBookingDetailQuery, OperatorBookingDetailDto>
{
    private readonly IBookingRepository _bookings;

    public GetOperatorBookingDetailQueryHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<OperatorBookingDetailDto> Handle(GetOperatorBookingDetailQuery request, CancellationToken cancellationToken)
    {
        var detail = await _bookings.GetOperatorBookingDetailAsync(request.BookingId, request.OperatorId, cancellationToken);
        if (detail is not null)
            return detail;

        if (await _bookings.BookingExistsAsync(request.BookingId, cancellationToken))
            throw new ForbiddenException("FORBIDDEN", "Booking belongs to another operator.");

        throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
    }
}
