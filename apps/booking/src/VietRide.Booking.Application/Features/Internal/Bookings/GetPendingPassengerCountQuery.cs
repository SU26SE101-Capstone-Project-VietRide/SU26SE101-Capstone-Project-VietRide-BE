using MediatR;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record GetPendingPassengerCountQuery(
    string TripId,
    string StopId,
    string? OperatorId) : IRequest<PendingPassengerCountDto>;
