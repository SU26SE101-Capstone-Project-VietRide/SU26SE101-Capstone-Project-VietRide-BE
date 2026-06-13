using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Bookings.EditDropoff;

/// <summary>
/// Handles POST /v1/bookings/{bookingId}/edit-dropoff.
/// v1 dropoff edits are price-neutral: no fare/refund/charge side effects.
/// </summary>
public sealed class EditDropoffCommandHandler : IRequestHandler<EditDropoffCommand, EditDropoffResult>
{
    private const int EditCutoffHours = 2;

    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripClient;
    private readonly IClock _clock;

    public EditDropoffCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IClock clock)
    {
        _bookings = bookings;
        _tripClient = tripClient;
        _clock = clock;
    }

    public async Task<EditDropoffResult> Handle(
        EditDropoffCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await _bookings.FindByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
        {
            throw new CodedNotFoundException(
                "BOOKING_NOT_FOUND",
                $"Booking '{request.BookingId}' not found.");
        }

        if (booking.PassengerUserId != request.PassengerUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only the booking owner may edit dropoff.");
        }

        if (booking.Status != BookingStatus.CONFIRMED)
        {
            throw new ConflictException(
                "BOOKING_CUTOFF_EXCEEDED",
                "Only confirmed bookings may be edited before cutoff.");
        }

        var trip = await _tripClient.GetTripSnapshotAsync(booking.TripId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                $"Trip '{booking.TripId}' not found.");
        }

        if (_clock.UtcNow >= trip.DepartureDateTime.AddHours(-EditCutoffHours))
        {
            throw new ConflictException(
                "BOOKING_CUTOFF_EXCEEDED",
                "Dropoff cannot be edited after the departure cutoff.");
        }

        if (request.DropoffStationId.HasValue)
        {
            ValidateDropoffStation(request.DropoffStationId.Value, trip);
        }

        var dropoffStop = ResolveDropoffStop(request, trip);
        if (dropoffStop is not null)
        {
            ValidateDropoffStop(booking.PickupStopId, dropoffStop, trip);
        }

        booking.ChangeDropoff(request.DropoffStationId, request.DropoffStopId);
        _bookings.Update(booking);

        return new EditDropoffResult(
            BookingId: booking.Id,
            Dropoff: new EditDropoffResult.DropoffDto(booking.DropoffStationId, booking.DropoffStopId),
            FareDelta: Money.Zero.Amount);
    }

    private static void ValidateDropoffStation(Guid dropoffStationId, TripSnapshot trip)
    {
        if (trip.DestinationStation.Id != dropoffStationId)
        {
            throw new CodedNotFoundException(
                "STATION_NOT_FOUND",
                $"Dropoff station '{dropoffStationId}' does not match the trip destination station.");
        }
    }

    private static TripStopSnapshot? ResolveDropoffStop(EditDropoffCommand request, TripSnapshot trip)
    {
        if (!request.DropoffStopId.HasValue)
        {
            return null;
        }

        var dropoffStop = trip.Stops.FirstOrDefault(s => s.StopId == request.DropoffStopId);
        if (dropoffStop is null)
        {
            throw new CodedNotFoundException(
                "STOP_NOT_FOUND",
                $"Dropoff stop '{request.DropoffStopId}' is not on the trip route.");
        }

        return dropoffStop;
    }

    private static void ValidateDropoffStop(Guid? pickupStopId, TripStopSnapshot dropoffStop, TripSnapshot trip)
    {
        if (!dropoffStop.AllowDropoff)
        {
            throw new CodedValidationException(
                "STOP_NOT_DROPOFF_ALLOWED",
                "The selected stop is not allowed for dropoff.");
        }

        if (!pickupStopId.HasValue)
        {
            return;
        }

        var pickupStop = trip.Stops.FirstOrDefault(s => s.StopId == pickupStopId);
        if (pickupStop is null)
        {
            throw new CodedNotFoundException(
                "STOP_NOT_FOUND",
                $"Pickup stop '{pickupStopId}' is not on the trip route.");
        }

        if (dropoffStop.OrderIndex <= pickupStop.OrderIndex)
        {
            throw new CodedValidationException(
                "STOP_NOT_DROPOFF_ALLOWED",
                "Dropoff stop must be after the pickup stop.");
        }
    }
}
