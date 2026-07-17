using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Features.Bookings.EditPickup;

/// <summary>
/// Handles POST /v1/bookings/{bookingId}/edit-pickup.
/// v1 is price-neutral-only: any fare change is rejected and no payment/refund/event seam is called.
/// </summary>
public sealed class EditPickupCommandHandler : IRequestHandler<EditPickupCommand, EditPickupResult>
{
    private const int EditCutoffHours = 2;

    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripClient;
    private readonly IBookingStationCanonicalizer _stationCanonicalizer;
    private readonly IClock _clock;

    public EditPickupCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IClock clock,
        IBookingStationCanonicalizer stationCanonicalizer)
    {
        _bookings = bookings;
        _tripClient = tripClient;
        _clock = clock;
        _stationCanonicalizer = stationCanonicalizer;
    }

    public async Task<EditPickupResult> Handle(
        EditPickupCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await _bookings.FindByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
        {
            throw new CodedNotFoundException(
                "BOOKING_NOT_FOUND",
                $"Booking '{request.BookingId}' not found.");
        }

        EnsureEditable(booking, request.PassengerUserId);

        var trip = await _tripClient.GetTripSnapshotAsync(booking.TripId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                $"Trip '{booking.TripId}' not found.");
        }

        var stationCanonicalization = await _stationCanonicalizer.LockAndResolveAsync(
            BookingStationCanonicalization.Collect(
                request.PickupStationId,
                booking.PickupStationId,
                booking.DropoffStationId,
                trip.OriginStation.Id,
                trip.DestinationStation.Id),
            cancellationToken);
        request = request with
        {
            PickupStationId = stationCanonicalization.Resolve(request.PickupStationId),
        };
        trip = BookingStationCanonicalization.ResolveTrip(trip, stationCanonicalization);
        booking = await _bookings.FindByIdForUpdateAsync(request.BookingId, cancellationToken)
            ?? throw new CodedNotFoundException(
                "BOOKING_NOT_FOUND",
                $"Booking '{request.BookingId}' not found.");
        EnsureEditable(booking, request.PassengerUserId);

        if (_clock.UtcNow >= trip.DepartureDateTime.AddHours(-EditCutoffHours))
        {
            throw new ConflictException(
                "BOOKING_CUTOFF_EXCEEDED",
                "Pickup cannot be edited after the departure cutoff.");
        }

        var pickupStop = ResolvePickupStop(request, trip);
        if (pickupStop is not null && !pickupStop.AllowPickup)
        {
            throw new CodedValidationException(
                "STOP_NOT_PICKUP_ALLOWED",
                "The selected stop is not allowed for pickup.");
        }

        var newFare = ResolvePickupFare(request, trip, pickupStop);
        if (newFare != booking.BaseFare)
        {
            throw new ConflictException(
                "BOOKING_EDIT_PICKUP_PRICE_CHANGED",
                "Pickup edit would change the booking fare. Cancel and rebook to change fare.");
        }

        booking.ChangePickup(request.PickupStationId, request.PickupStopId);
        _bookings.Update(booking);

        return new EditPickupResult(
            BookingId: booking.Id,
            Pickup: new EditPickupResult.PickupDto(booking.PickupStationId, booking.PickupStopId),
            FareDelta: Money.Zero.Amount,
            RefundAmount: Money.Zero.Amount,
            PaymentRedirectUrl: null);
    }

    private static void EnsureEditable(BookingEntity booking, Guid passengerUserId)
    {
        if (booking.PassengerUserId != passengerUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only the booking owner may edit pickup.");
        }

        if (booking.Status != BookingStatus.CONFIRMED)
        {
            throw new ConflictException(
                "BOOKING_CUTOFF_EXCEEDED",
                "Only confirmed bookings may be edited before cutoff.");
        }

        if (booking.ShuttleIntent?.IsActive == true)
        {
            throw new ConflictException(
                "SHUTTLE_PICKUP_LOCKED",
                "Pickup cannot be edited while a shuttle intent is active.");
        }
    }

    private static TripStopSnapshot? ResolvePickupStop(EditPickupCommand request, TripSnapshot trip)
    {
        if (request.PickupStationId.HasValue)
        {
            return null;
        }

        var pickupStop = trip.Stops.FirstOrDefault(s => s.StopId == request.PickupStopId);
        if (pickupStop is null)
        {
            throw new CodedNotFoundException(
                "STOP_NOT_FOUND",
                $"Pickup stop '{request.PickupStopId}' is not on the trip route.");
        }

        return pickupStop;
    }

    private static Money ResolvePickupFare(EditPickupCommand request, TripSnapshot trip, TripStopSnapshot? pickupStop)
    {
        if (request.PickupStationId.HasValue)
        {
            return Money.FromRaw(trip.BaseFare);
        }

        return Money.FromRaw(pickupStop!.FareFromThisStop ?? trip.BaseFare);
    }
}
