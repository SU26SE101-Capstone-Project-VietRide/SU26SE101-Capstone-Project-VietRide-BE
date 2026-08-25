using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.VehicleSubstitution;

public sealed class ApplyVehicleSubstitutionCommandHandler(
    IBookingRepository bookings,
    IBookingTransferRepository transfers,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<ApplyVehicleSubstitutionCommand, int>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> Handle(
        ApplyVehicleSubstitutionCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await bookings.AcquireEventLockAsync(request.SourceEventId, cancellationToken);

        var mappingsByBooking = request.Mappings
            .GroupBy(mapping => mapping.BookingId)
            .OrderBy(group => group.Key)
            .ToArray();
        var mappedBookingIds = mappingsByBooking.Select(group => group.Key).ToArray();
        var mappedPassengerIds = request.Mappings.Select(mapping => mapping.PassengerId).ToArray();
        var eligibleBookings = await bookings.GetVehicleSubstitutionBookingsForUpdateAsync(
            request.OldTripId,
            request.OperatorId,
            mappedBookingIds,
            cancellationToken);
        var existingTransfers = await transfers.GetByPassengerTripPairAsync(
            mappedPassengerIds,
            request.OldTripId,
            request.NewTripId,
            cancellationToken);
        var existingPassengerIds = existingTransfers.Select(transfer => transfer.PassengerId).ToHashSet();
        var eligibleById = eligibleBookings.ToDictionary(booking => booking.Id);
        var occurredAt = clock.UtcNow;
        var changedBookings = 0;

        foreach (var mappingGroup in mappingsByBooking)
        {
            if (!eligibleById.TryGetValue(mappingGroup.Key, out var booking))
                continue;

            var eventTransfers = new List<BookingTransferredIntegrationEvent.Transfer>();
            foreach (var mapping in mappingGroup.OrderBy(item => item.PassengerId))
            {
                if (existingPassengerIds.Contains(mapping.PassengerId))
                    continue;

                var passenger = booking.Passengers.SingleOrDefault(item => item.Id == mapping.PassengerId);
                if (passenger is null || passenger.BoardingStatus.ToString() != mapping.OriginalBoardingStatus)
                    continue;

                var confirmationStatus = passenger.BoardingStatus == PassengerBoardingStatus.BOARDED
                    ? BookingTransferConfirmationStatus.PENDING_CONFIRM
                    : BookingTransferConfirmationStatus.NOT_REQUIRED;
                passenger.ApplyVehicleSubstitutionSeat(mapping.NewSeatNumber);
                var ticketId = booking.Tickets
                    .SingleOrDefault(ticket => ticket.PassengerId == passenger.Id)?.Id;
                var transfer = BookingTransfer.Create(
                    booking.Id,
                    passenger.Id,
                    ticketId,
                    request.OldTripId,
                    request.NewTripId,
                    mapping.OriginalSeatNumber,
                    mapping.NewSeatNumber,
                    confirmationStatus,
                    occurredAt,
                    request.ActorUserId,
                    originalSeatType: mapping.OriginalSeatType,
                    newSeatType: mapping.NewSeatType,
                    isSeatDowngrade: mapping.IsSeatDowngrade);
                await transfers.AddAsync(transfer, cancellationToken);
                existingPassengerIds.Add(passenger.Id);
                eventTransfers.Add(new BookingTransferredIntegrationEvent.Transfer(
                    passenger.Id,
                    transfer.OriginalSeatNumber,
                    transfer.NewSeatNumber,
                    transfer.ConfirmationStatus.ToString(),
                    mapping.OriginalBoardingStatus));
            }

            if (eventTransfers.Count == 0)
                continue;

            booking.ApplyVehicleSubstitution(request.OldTripId, request.NewTripId);
            var integrationEvent = new BookingTransferredIntegrationEvent(
                Guid.NewGuid(),
                occurredAt,
                request.SourceEventId,
                booking.Id,
                booking.PassengerUserId,
                request.OperatorId,
                request.OldTripId,
                request.NewTripId,
                request.NewVehicleId,
                request.NewVehiclePlateNumber,
                request.NewTripDepartureDateTime,
                request.NotifyPassengers,
                eventTransfers);
            await outbox.EnqueueAsync(
                integrationEvent.EventId,
                BookingTransferredIntegrationEvent.EventTypeValue,
                JsonSerializer.Serialize(integrationEvent, JsonOptions),
                cancellationToken);

            var unseatedTransfers = eventTransfers
                .Where(transfer => transfer.NewSeatNumber is null)
                .ToArray();
            if (unseatedTransfers.Length > 0)
            {
                var shortageEvent = new BookingSeatShortageDetectedIntegrationEvent(
                    Guid.NewGuid(),
                    occurredAt,
                    request.SourceEventId,
                    booking.Id,
                    booking.BookingCode.Value,
                    request.OperatorId,
                    request.OldTripId,
                    request.NewTripId,
                    unseatedTransfers.Length,
                    unseatedTransfers
                        .Select(transfer => transfer.OriginalSeatNumber)
                        .Where(seat => seat is not null)
                        .Select(seat => seat!)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(seat => seat, StringComparer.Ordinal)
                        .ToArray());
                await outbox.EnqueueAsync(
                    shortageEvent.EventId,
                    BookingSeatShortageDetectedIntegrationEvent.EventTypeValue,
                    JsonSerializer.Serialize(shortageEvent, JsonOptions),
                    cancellationToken);
            }
            changedBookings++;
        }

        if (changedBookings > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return changedBookings;
    }

    private static void Validate(ApplyVehicleSubstitutionCommand request)
    {
        if (request.SourceEventId == Guid.Empty
            || request.OperatorId == Guid.Empty
            || request.OldTripId == Guid.Empty
            || request.NewTripId == Guid.Empty
            || request.NewVehicleId == Guid.Empty
            || request.ActorUserId == Guid.Empty)
        {
            throw new ArgumentException("Vehicle-substitution command contains an empty required id.");
        }
        if (request.OldTripId == request.NewTripId)
            throw new ArgumentException("Replacement Trip must differ from the original Trip.");
        if (request.OccurredAt == default || request.NewTripDepartureDateTime == default)
            throw new ArgumentException("Vehicle-substitution command contains an invalid timestamp.");
        if (string.IsNullOrWhiteSpace(request.NewVehiclePlateNumber)
            || request.NewVehiclePlateNumber != request.NewVehiclePlateNumber.Trim()
            || request.NewVehiclePlateNumber.Length > 20)
        {
            throw new ArgumentException("Replacement vehicle plate number is invalid.");
        }
        if (request.Mappings.GroupBy(mapping => mapping.PassengerId).Any(group => group.Count() != 1))
            throw new ArgumentException("Vehicle-substitution mappings must contain each Passenger once.");
    }
}
