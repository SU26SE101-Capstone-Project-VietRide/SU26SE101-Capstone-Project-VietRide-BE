using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.TripEvents;

public sealed record HandleTripCompletedCommand(
    Guid TripId,
    DateTimeOffset CompletedAt,
    bool HasSubstitution) : IRequest<int>;
