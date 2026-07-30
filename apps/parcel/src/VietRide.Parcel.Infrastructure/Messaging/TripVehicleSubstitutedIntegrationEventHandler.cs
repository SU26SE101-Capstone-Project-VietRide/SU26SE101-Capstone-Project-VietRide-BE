using MediatR;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripVehicleSubstitutedIntegrationEventHandler
    : IIntegrationEventHandler<TripVehicleSubstitutedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripVehicleSubstitutedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        TripVehicleSubstitutedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);
        await _mediator.Send(
            new HandleVehicleSubstitutedCommand(
                integrationEvent.EventId,
                integrationEvent.OldTripId,
                integrationEvent.NewTripId,
                integrationEvent.OperatorId,
                integrationEvent.Reason.Trim()),
            cancellationToken);
    }

    private static void Validate(TripVehicleSubstitutedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty
            || integrationEvent.SubstitutionId != integrationEvent.EventId
            || integrationEvent.OperatorId == Guid.Empty
            || integrationEvent.OldTripId == Guid.Empty
            || integrationEvent.NewTripId == Guid.Empty
            || integrationEvent.OldTripId == integrationEvent.NewTripId
            || integrationEvent.OldVehicleId == Guid.Empty
            || integrationEvent.NewVehicleId == Guid.Empty
            || integrationEvent.ActorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Trip vehicle-substituted contract contains an invalid required id.");
        }

        if (integrationEvent.OccurredAt == default
            || integrationEvent.OccurredAt.Kind != DateTimeKind.Utc
            || integrationEvent.DisruptedAt == default
            || new DateTimeOffset(integrationEvent.OccurredAt)
                != integrationEvent.DisruptedAt.ToUniversalTime()
            || integrationEvent.NewTripDepartureDateTime == default)
        {
            throw new ArgumentException(
                "Trip vehicle-substituted contract contains an invalid timestamp.");
        }

        if (!string.Equals(
                integrationEvent.OldTripStatus,
                "DISRUPTED",
                StringComparison.Ordinal)
            || !string.Equals(
                integrationEvent.NewTripStatus,
                "BOARDING",
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(integrationEvent.NewVehiclePlateNumber)
            || string.IsNullOrWhiteSpace(integrationEvent.Reason)
            || integrationEvent.Reason.Trim().Length > 500)
        {
            throw new ArgumentException(
                "Trip vehicle-substituted contract contains invalid required data.");
        }

        if (integrationEvent.Mappings is null
            || integrationEvent.Mappings.Any(mapping =>
                mapping.BookingId == Guid.Empty
                || mapping.PassengerId == Guid.Empty
                || string.IsNullOrWhiteSpace(mapping.OriginalBoardingStatus)))
        {
            throw new ArgumentException(
                "Trip vehicle-substituted contract contains an invalid passenger mapping.");
        }
    }
}
