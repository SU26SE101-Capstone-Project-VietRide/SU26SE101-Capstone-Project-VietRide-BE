using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record GetActiveBookingCountByStopQuery(Guid StopId, Guid OperatorId) : IRequest<int>;

public sealed class GetActiveBookingCountByStopHandler(IBookingRepository bookings)
    : IRequestHandler<GetActiveBookingCountByStopQuery, int>
{
    public Task<int> Handle(GetActiveBookingCountByStopQuery request, CancellationToken cancellationToken)
        => Task.FromResult(bookings.QueryNoTracking().Count(x => x.OperatorId == request.OperatorId
            && (x.PickupStopId == request.StopId || x.DropoffStopId == request.StopId)
            && (x.Status == BookingStatus.PENDING_PAYMENT || x.Status == BookingStatus.CONFIRMED)));
}
