using MediatR;
using VietRide.Booking.Application.Features.Bookings.VehicleSubstitution;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class TripVehicleSubstitutedIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<TripVehicleSubstitutedIntegrationEvent>
{
    public async Task HandleAsync(
        TripVehicleSubstitutedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);
        await mediator.Send(new ApplyVehicleSubstitutionCommand(
            integrationEvent.EventId,
            integrationEvent.OccurredAt,
            integrationEvent.OperatorId,
            integrationEvent.OldTripId,
            integrationEvent.NewTripId,
            integrationEvent.NewVehicleId,
            integrationEvent.NewVehiclePlateNumber,
            integrationEvent.NewTripDepartureDateTime,
            integrationEvent.ActorUserId,
            integrationEvent.NotifyPassengers,
            integrationEvent.Mappings.Select(mapping => new VehicleSubstitutionMapping(
                mapping.BookingId,
                mapping.PassengerId,
                mapping.OriginalSeatNumber,
                mapping.NewSeatNumber,
                mapping.OriginalBoardingStatus)).ToArray()), cancellationToken);
    }

    private static void Validate(TripVehicleSubstitutedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty
            || integrationEvent.SubstitutionId != integrationEvent.EventId
            || integrationEvent.OperatorId == Guid.Empty
            || integrationEvent.OldTripId == Guid.Empty
            || integrationEvent.OldVehicleId == Guid.Empty
            || integrationEvent.NewTripId == Guid.Empty
            || integrationEvent.NewVehicleId == Guid.Empty
            || integrationEvent.ActorUserId == Guid.Empty)
        {
            throw new ArgumentException("Trip vehicle-substituted contract contains an invalid required id.");
        }
        if (integrationEvent.OldTripId == integrationEvent.NewTripId
            || integrationEvent.OldVehicleId == integrationEvent.NewVehicleId)
        {
            throw new ArgumentException("Trip vehicle-substituted contract requires distinct old and replacement ids.");
        }
        if (integrationEvent.OccurredAt == default
            || integrationEvent.DisruptedAt == default
            || integrationEvent.NewTripDepartureDateTime == default
            || integrationEvent.DisruptedAt != integrationEvent.OccurredAt)
        {
            throw new ArgumentException("Trip vehicle-substituted contract contains an invalid timestamp.");
        }
        if (integrationEvent.OldTripStatus != "DISRUPTED" || integrationEvent.NewTripStatus != "BOARDING")
            throw new ArgumentException("Trip vehicle-substituted contract contains an invalid Trip status.");
        ValidateText(integrationEvent.NewVehiclePlateNumber, 20, "vehicle plate");
        ValidateText(integrationEvent.Reason, 500, "reason");
        if (integrationEvent.Mappings.GroupBy(mapping => mapping.PassengerId).Any(group => group.Count() != 1))
            throw new ArgumentException("Trip vehicle-substituted contract contains a duplicate Passenger mapping.");

        foreach (var mapping in integrationEvent.Mappings)
        {
            if (mapping.BookingId == Guid.Empty || mapping.PassengerId == Guid.Empty)
                throw new ArgumentException("Trip vehicle-substituted mapping contains an empty required id.");
            if (mapping.OriginalBoardingStatus is not "BOARDED" and not "PENDING")
                throw new ArgumentException("Trip vehicle-substituted mapping contains an invalid boarding status.");
            ValidateSeat(mapping.OriginalSeatNumber);
            ValidateSeat(mapping.NewSeatNumber);
        }
    }

    private static void ValidateText(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maxLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException($"Trip vehicle-substituted {field} is invalid.");
        }
    }

    private static void ValidateSeat(string? seatNumber)
    {
        if (seatNumber is not null)
            ValidateText(seatNumber, 20, "seat number");
    }
}
