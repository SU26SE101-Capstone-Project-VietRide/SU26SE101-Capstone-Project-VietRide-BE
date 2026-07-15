using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.HandleVehicleSwap;

public sealed record HandleVehicleSwapCommand(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TripId,
    Guid OperatorId,
    DateTimeOffset DepartureDateTime,
    IReadOnlyCollection<VehicleSwapSeatImpact> SeatImpacts) : IRequest<int>;

public sealed record VehicleSwapSeatImpact(
    Guid BookingId,
    IReadOnlyCollection<string> SeatNumbers,
    string Reason);
