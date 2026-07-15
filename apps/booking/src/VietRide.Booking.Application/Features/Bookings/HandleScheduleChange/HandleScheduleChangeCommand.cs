using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;

[SkipTransaction]
public sealed record HandleScheduleChangeCommand(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TripId,
    Guid OperatorId,
    DateTimeOffset OldDeparture,
    DateTimeOffset NewDeparture,
    string Severity) : IRequest<int>;
