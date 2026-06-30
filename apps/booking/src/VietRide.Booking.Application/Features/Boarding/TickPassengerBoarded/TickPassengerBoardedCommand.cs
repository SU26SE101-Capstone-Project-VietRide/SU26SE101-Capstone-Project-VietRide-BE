using MediatR;

namespace VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;

public sealed record TickPassengerBoardedCommand(
    Guid TripId,
    Guid PassengerRecordId,
    Guid CallerUserId) : IRequest<TickPassengerBoardedResult>;
