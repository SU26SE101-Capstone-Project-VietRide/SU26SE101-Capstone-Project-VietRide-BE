using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;

public sealed class TickPassengerBoardedCommandHandler
    : IRequestHandler<TickPassengerBoardedCommand, TickPassengerBoardedResult>
{
    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripServiceClient;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TickPassengerBoardedCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripServiceClient,
        IClock clock,
        IIntegrationEventOutbox outbox)
    {
        _bookings = bookings;
        _tripServiceClient = tripServiceClient;
        _clock = clock;
        _outbox = outbox;
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

        var ticket = booking!.Tickets.SingleOrDefault(
            candidate => candidate.PassengerId == request.PassengerRecordId);

        if (ticket is null)
        {
            throw new CodedNotFoundException(
                "TICKET_NOT_FOUND",
                "Ticket was not found for this passenger record.");
        }

        if (ticket.Status != TicketStatus.ISSUED)
        {
            throw new ConflictException(
                "TICKET_NOT_BOARDABLE",
                "Ticket is not in ISSUED status.");
        }

        if (passenger.BoardingStatus == PassengerBoardingStatus.BOARDED)
        {
            throw new ConflictException(
                "BOOKING_PASSENGER_ALREADY_BOARDED",
                "Passenger has already boarded.");
        }

        var boardedAt = _clock.UtcNow;
        passenger.MarkBoarded(boardedAt);
        ticket.MarkUsed(boardedAt);

        var eventId = Guid.NewGuid();
        var boardedEvent = new PassengerBoardedIntegrationEvent(
            eventId,
            boardedAt,
            booking.Id,
            booking.BookingCode.Value,
            request.TripId,
            passenger.Id,
            passenger.SeatNumber ?? throw new InvalidOperationException("Boarded passenger must have a seat number."),
            ticket.TicketCode.Value);
        await _outbox.EnqueueAsync(
            eventId,
            boardedEvent.EventType,
            JsonSerializer.Serialize(boardedEvent, JsonOptions),
            cancellationToken);

        return new TickPassengerBoardedResult(
            passenger.Id,
            passenger.BoardingStatus.ToString(),
            boardedAt,
            passenger.BoardedAtStopId,
            ticket.Id,
            ticket.TicketCode.Value,
            ticket.Status.ToString());
    }
}
