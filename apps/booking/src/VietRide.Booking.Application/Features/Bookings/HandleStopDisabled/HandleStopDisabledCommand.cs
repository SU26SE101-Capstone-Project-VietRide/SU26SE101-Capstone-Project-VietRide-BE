using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.HandleStopDisabled;

public sealed record HandleStopDisabledCommand(Guid EventId, Guid StopId, Guid OperatorId, Guid? ReplacedByStopId)
    : IRequest<int>;
