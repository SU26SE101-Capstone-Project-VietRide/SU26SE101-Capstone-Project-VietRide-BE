using MediatR;
using VietRide.Booking.Application.Features.Bookings.HandleVehicleSwap;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class TripVehicleSwappedIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<TripVehicleSwappedIntegrationEvent>
{
    public async Task HandleAsync(
        TripVehicleSwappedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);

        await mediator.Send(new HandleVehicleSwapCommand(
            integrationEvent.EventId,
            new DateTimeOffset(DateTime.SpecifyKind(integrationEvent.OccurredAt, DateTimeKind.Utc)),
            integrationEvent.TripId,
            integrationEvent.OperatorId,
            integrationEvent.DepartureDateTime,
            integrationEvent.SeatImpacts
                .Select(impact => new VehicleSwapSeatImpact(
                    impact.BookingId,
                    impact.SeatNumbers,
                    impact.Reason))
                .ToArray()), cancellationToken);
    }

    private static void Validate(TripVehicleSwappedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty
            || integrationEvent.TripId == Guid.Empty
            || integrationEvent.OperatorId == Guid.Empty
            || integrationEvent.OldVehicleId == Guid.Empty
            || integrationEvent.NewVehicleId == Guid.Empty
            || integrationEvent.DriverUserId == Guid.Empty)
        {
            throw new ArgumentException("Trip vehicle-swapped contract contains an empty required id.");
        }

        if (integrationEvent.OldVehicleId == integrationEvent.NewVehicleId)
        {
            throw new ArgumentException("Trip vehicle-swapped contract requires distinct old and new vehicles.");
        }

        if (integrationEvent.OccurredAt == default || integrationEvent.OccurredAt.Kind != DateTimeKind.Utc
            || integrationEvent.DepartureDateTime == default)
        {
            throw new ArgumentException("Trip vehicle-swapped contract contains an invalid timestamp.");
        }

        ValidatePlate(integrationEvent.OldVehiclePlateNumber);
        ValidatePlate(integrationEvent.NewVehiclePlateNumber);
        ArgumentNullException.ThrowIfNull(integrationEvent.SeatImpacts);
        foreach (var impact in integrationEvent.SeatImpacts)
        {
            if (impact is null || impact.BookingId == Guid.Empty
                || impact.SeatNumbers is null || impact.SeatNumbers.Count == 0)
            {
                throw new ArgumentException("Each vehicle-swap seat impact requires a Booking and seats.");
            }

            if (impact.Reason is not "SEAT_REMOVED" and not "SEAT_DISABLED" and not "SEAT_TYPE_DOWNGRADED")
            {
                throw new ArgumentException("Trip vehicle-swapped contract contains an invalid impact reason.");
            }

            foreach (var seatNumber in impact.SeatNumbers)
            {
                if (string.IsNullOrWhiteSpace(seatNumber) || seatNumber.Trim().Length > 20)
                {
                    throw new ArgumentException("Trip vehicle-swapped contract contains an invalid seat number.");
                }
            }
        }
    }

    private static void ValidatePlate(string? plateNumber)
    {
        if (string.IsNullOrWhiteSpace(plateNumber) || plateNumber.Trim().Length > 20)
        {
            throw new ArgumentException("Trip vehicle-swapped contract contains an invalid vehicle plate number.");
        }
    }
}
