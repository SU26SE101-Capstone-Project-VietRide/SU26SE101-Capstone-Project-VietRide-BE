using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed class ScanBookingCodeForTripQueryHandler
    : IRequestHandler<ScanBookingCodeForTripQuery, ScanBookingCodeForTripResult>
{
    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripServiceClient;

    public ScanBookingCodeForTripQueryHandler(
        IBookingRepository bookings,
        ITripServiceClient tripServiceClient)
    {
        _bookings = bookings;
        _tripServiceClient = tripServiceClient;
    }

    public async Task<ScanBookingCodeForTripResult> Handle(
        ScanBookingCodeForTripQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await _tripServiceClient.GetTripSnapshotAsync(
            request.TripId,
            cancellationToken);

        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip not found.");
        }

        if (request.CallerUserId != trip.DriverUserId
            && request.CallerUserId != trip.AssistantUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Caller is not assigned to this trip.");
        }

        var booking = await _bookings.FindByBookingCodeAsync(
            request.BookingCode,
            cancellationToken);

        if (booking is null)
        {
            throw BookingNotFound();
        }

        if (booking.TripId != request.TripId)
        {
            throw new CodedValidationException(
                "BOOKING_NOT_FOR_THIS_TRIP",
                "Booking does not belong to this trip.");
        }

        if (booking.Status != BookingStatus.CONFIRMED)
        {
            throw BookingNotFound();
        }

        var bookingWithPassengers = await _bookings.FindByIdWithPassengersAsync(
            booking.Id,
            cancellationToken);

        if (bookingWithPassengers is null)
        {
            throw BookingNotFound();
        }

        if (bookingWithPassengers.TripId != request.TripId)
        {
            throw new CodedValidationException(
                "BOOKING_NOT_FOR_THIS_TRIP",
                "Booking does not belong to this trip.");
        }

        if (bookingWithPassengers.Status != BookingStatus.CONFIRMED)
        {
            throw BookingNotFound();
        }

        var items = bookingWithPassengers.Passengers
            .OrderBy(passenger => passenger.SeatNumber, StringComparer.Ordinal)
            .Select(passenger => new ScanBookingCodePassengerItem(
                passenger.SeatNumber,
                passenger.BoardingStatus.ToString()))
            .ToArray();

        return new ScanBookingCodeForTripResult(items);
    }

    private static CodedNotFoundException BookingNotFound()
        => new("BOOKING_NOT_FOUND", "Booking not found.");
}
