using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Booking.Application.Features.Bookings.HandleTripCancelled;

[SkipTransaction]
public sealed record HandleTripCancelledCommand(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TripId,
    Guid OperatorId,
    DateTimeOffset CancelledAt,
    string CancelReason,
    bool AllowOperatorReason = false) : IRequest<int>;
