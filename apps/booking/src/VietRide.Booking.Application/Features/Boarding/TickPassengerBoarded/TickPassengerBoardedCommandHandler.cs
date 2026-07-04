using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;

public sealed class TickPassengerBoardedCommandHandler
    : IRequestHandler<TickPassengerBoardedCommand, TickPassengerBoardedResult>
{
    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripServiceClient;
    private readonly IClock _clock;

    public TickPassengerBoardedCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripServiceClient,
        IClock clock)
    {
        _bookings = bookings;
        _tripServiceClient = tripServiceClient;
        _clock = clock;
    }

    public async Task<TickPassengerBoardedResult> Handle(
        TickPassengerBoardedCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await _tripServiceClient.GetTripSnapshotAsync(
            request.TripId,
            cancellationToken);

        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip not found.");
        }

        if (trip.DriverUserId != request.CallerUserId
            && trip.AssistantUserId != request.CallerUserId)
        {
            throw new ForbiddenException("FORBIDDEN", "Caller is not assigned to this trip.");
        }

        var locatedBooking = _bookings.QueryNoTracking()
            .Where(booking => booking.Passengers.Any(
                passenger => passenger.Id == request.PassengerRecordId))
            .Select(booking => new
            {
                booking.Id,
                booking.TripId,
            })
            .SingleOrDefault();

        if (locatedBooking is null)
        {
            throw new CodedNotFoundException(
                "BOOKING_NOT_FOUND",
                "Passenger record was not found.");
        }

        if (locatedBooking.TripId != request.TripId)
        {
            throw new CodedValidationException(
                "BOOKING_NOT_FOR_THIS_TRIP",
                "Passenger record does not belong to this trip.");
        }

        var booking = await _bookings.FindByIdWithPassengersAsync(
            locatedBooking.Id,
            cancellationToken);

        var passenger = booking?.Passengers.SingleOrDefault(
            candidate => candidate.Id == request.PassengerRecordId);

        if (passenger is null)
        {
            throw new CodedNotFoundException(
                "BOOKING_NOT_FOUND",
                "Passenger record was not found.");
        }

        if (passenger.BoardingStatus == PassengerBoardingStatus.BOARDED)
        {
            throw new ConflictException(
                "BOOKING_PASSENGER_ALREADY_BOARDED",
                "Passenger has already boarded.");
        }

        var boardedAt = _clock.UtcNow;
        passenger.MarkBoarded(boardedAt);

        return new TickPassengerBoardedResult(
            passenger.Id,
            passenger.BoardingStatus.ToString(),
            boardedAt,
            passenger.BoardedAtStopId);
    }
}
