using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.HandleTripDisrupted;

public sealed class HandleTripDisruptedCommandHandler(
    IBookingRepository bookings,
    IBookingStatusHistoryRepository statusHistory,
    ITripServiceClient tripClient,
    IVoucherService voucherService,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<HandleTripDisruptedCommand, int>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string DisruptionReason = "OPERATOR_DISRUPTED_IN_PROGRESS";

    public async Task<int> Handle(
        HandleTripDisruptedCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        if (request.HasSubstitution)
        {
            return 0;
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await bookings.AcquireEventLockAsync(request.EventId, cancellationToken);
            var eligible = await bookings.GetDisruptionBookingsForUpdateAsync(
                request.TripId,
                request.OperatorId,
                cancellationToken);
            if (eligible.Count == 0)
            {
                return 0;
            }

            var trip = await tripClient.GetOperationalTripSnapshotAsync(
                request.TripId,
                cancellationToken);
            EnsureAuthoritativeTripSnapshot(trip, request);

            foreach (var booking in eligible)
            {
                var previousStatus = booking.Status;
                var refund = BookingDisruptionRefundCalculator.Calculate(booking, trip);
                booking.Disrupt(request.TerminalAt);
                bookings.Update(booking);

                await statusHistory.AddAsync(
                    BookingStatusHistory.Create(
                        booking.Id,
                        BookingStatus.DISRUPTED,
                        request.TerminalAt,
                        BookingStatusHistorySource.DisruptOnTripDisrupted,
                        actorUserId: null,
                        reasonCode: DisruptionReason),
                    cancellationToken);

                await voucherService.CompensateAsync(
                    booking.Id,
                    cancellationToken);

                var occurredAt = clock.UtcNow;
                var cancelledEventId = Guid.NewGuid();
                var cancelled = new BookingCancelledIntegrationEvent(
                    cancelledEventId,
                    occurredAt,
                    booking.Id,
                    booking.BookingCode.Value,
                    booking.PassengerUserId,
                    refund.RefundAmount,
                    true,
                    DisruptionReason,
                    booking.Tickets
                        .Select(ticket => ticket.TicketCode.Value)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    booking.Tickets.Count,
                    booking.TripId,
                    previousStatus.ToString(),
                    booking.Passengers.Select(passenger => passenger.SeatNumber).OfType<string>().Order(StringComparer.Ordinal).ToArray());
                await outbox.EnqueueAsync(
                    cancelledEventId,
                    BookingCancelledIntegrationEvent.EventTypeValue,
                    JsonSerializer.Serialize(cancelled, JsonOptions),
                    cancellationToken);

                var disruptedEventId = Guid.NewGuid();
                var disrupted = new BookingDisruptedIntegrationEvent(
                    disruptedEventId,
                    occurredAt,
                    booking.Id,
                    booking.BookingCode.Value,
                    request.TripId,
                    request.OperatorId,
                    booking.PassengerUserId,
                    refund.TraveledRatio,
                    refund.RefundAmount,
                    DisruptionReason);
                await outbox.EnqueueAsync(
                    disruptedEventId,
                    BookingDisruptedIntegrationEvent.EventTypeValue,
                    JsonSerializer.Serialize(disrupted, JsonOptions),
                    cancellationToken);
            }

            return eligible.Count;
        }, cancellationToken);
    }

    private static void EnsureAuthoritativeTripSnapshot(
        TripSnapshot trip,
        HandleTripDisruptedCommand request)
    {
        if (trip.TripId != request.TripId
            || trip.OperatorId != request.OperatorId
            || !string.Equals(trip.Status, "DISRUPTED", StringComparison.Ordinal))
        {
            throw new BookingUpstreamUnavailableException(
                "Trip operational snapshot does not match the disruption event.");
        }
    }

    private static void Validate(HandleTripDisruptedCommand request)
    {
        if (request.EventId == Guid.Empty
            || request.TripId == Guid.Empty
            || request.OperatorId == Guid.Empty)
        {
            throw new ArgumentException(
                "Trip-disrupted event, Trip, and Operator ids must be non-empty.");
        }

        if (request.OccurredAt == default || request.TerminalAt == default)
        {
            throw new ArgumentException("Trip-disrupted timestamps are required.");
        }

        if (request.Reason?.Length > 500)
        {
            throw new ArgumentException("Trip disruption reason cannot exceed 500 characters.");
        }
    }
}
