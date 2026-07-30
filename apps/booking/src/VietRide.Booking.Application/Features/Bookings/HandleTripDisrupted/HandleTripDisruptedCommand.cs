using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Booking.Application.Features.Bookings.HandleTripDisrupted;

[SkipTransaction]
public sealed record HandleTripDisruptedCommand(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TripId,
    Guid OperatorId,
    DateTimeOffset TerminalAt,
    bool HasSubstitution,
    string? Reason) : IRequest<int>;
