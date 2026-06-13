using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;

/// <summary>
/// Handles POST /v1/bookings/round-trip — creates two independent bookings linked by
/// a display-only bookingGroupId.
/// <para>Order mirrors CreateBookingCommandHandler per-leg:</para>
/// <list type="number">
///   <item>Fetch both trip snapshots and validate both are SCHEDULED.</item>
///   <item>Validate outbound route has returnRouteId and return departs after outbound arrival.</item>
///   <item>Lock outbound seats, then return seats; if return lock fails, release outbound.</item>
///   <item>Create two PENDING_PAYMENT bookings with independent fares and discountAmount=0.</item>
///   <item>WALLET: batch-charge once for both BOOKING references. VNPay: one BOOKING_GROUP charge.</item>
///   <item>On WALLET success: book seats for both legs, confirm both, enqueue one confirmed event per leg.</item>
/// </list>
/// </summary>
public sealed class CreateRoundTripBookingCommandHandler
    : IRequestHandler<CreateRoundTripBookingCommand, CreateRoundTripBookingResult>
{
    private const string EventType = "booking.booking.confirmed";
    private const int SeatLockTtlSeconds = 10 * 60;

    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IBookingService _bookingService;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<CreateRoundTripBookingCommandHandler> _logger;

    public CreateRoundTripBookingCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IPaymentServiceClient paymentClient,
        IBookingService bookingService,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<CreateRoundTripBookingCommandHandler> logger)
    {
        _bookings = bookings;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
        _bookingService = bookingService;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateRoundTripBookingResult> Handle(
        CreateRoundTripBookingCommand request,
        CancellationToken cancellationToken)
    {
        EnsureSeatCount(request.Outbound);
        EnsureSeatCount(request.Return);

        var outboundTrip = await GetScheduledTripAsync(request.Outbound.TripId, cancellationToken);
        var returnTrip = await GetScheduledTripAsync(request.Return.TripId, cancellationToken);

        if (outboundTrip.ReturnRouteId is null)
        {
            throw new CodedValidationException(
                "ROUTE_RETURN_NOT_CONFIGURED",
                "Outbound route is not configured with a return route.");
        }

        if (returnTrip.DepartureDateTime <= outboundTrip.EstimatedArrivalTime)
        {
            throw new CodedValidationException(
                "BOOKING_ROUND_TRIP_INVALID",
                "Return trip departure must be after outbound trip arrival.");
        }

        var outboundSeatNumbers = request.Outbound.Seats.Select(s => s.SeatNumber).ToList();
        var returnSeatNumbers = request.Return.Seats.Select(s => s.SeatNumber).ToList();

        var outboundLockToken = await LockLegAsync(
            request.PassengerUserId,
            request.Outbound.TripId,
            outboundSeatNumbers,
            cancellationToken);

        Guid returnLockToken;
        try
        {
            returnLockToken = await LockLegAsync(
                request.PassengerUserId,
                request.Return.TripId,
                returnSeatNumbers,
                cancellationToken);
        }
        catch
        {
            await _bookingService.ReleaseSeatsAsync(
                request.Outbound.TripId,
                outboundLockToken,
                outboundSeatNumbers,
                cancellationToken);
            throw;
        }

        var bookingGroupId = Guid.NewGuid();
        var outboundTotal = Money.FromRaw(outboundTrip.BaseFare);
        var returnTotal = Money.FromRaw(returnTrip.BaseFare);
        var discountAmount = Money.Zero;

        BookingEntity outboundBooking;
        BookingEntity returnBooking;
        try
        {
            outboundBooking = CreatePendingBooking(
                request.PassengerUserId,
                request.Outbound,
                outboundTrip,
                outboundTotal,
                discountAmount,
                bookingGroupId,
                TripDirection.OUTBOUND);

            returnBooking = CreatePendingBooking(
                request.PassengerUserId,
                request.Return,
                returnTrip,
                returnTotal,
                discountAmount,
                bookingGroupId,
                TripDirection.RETURN);

            await _bookings.AddAsync(outboundBooking, cancellationToken);
            await _bookings.AddAsync(returnBooking, cancellationToken);
        }
        catch
        {
            await ReleaseBothLegsAsync(
                request,
                outboundLockToken,
                outboundSeatNumbers,
                returnLockToken,
                returnSeatNumbers,
                cancellationToken);
            throw;
        }

        var grandTotal = outboundBooking.TotalAmount + returnBooking.TotalAmount;
        var paymentRedirectUrl = await ChargeAsync(
            request,
            bookingGroupId,
            outboundBooking,
            returnBooking,
            grandTotal,
            outboundLockToken,
            outboundSeatNumbers,
            returnLockToken,
            returnSeatNumbers,
            cancellationToken);

        if (string.Equals(request.PaymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult(bookingGroupId, outboundBooking, returnBooking, grandTotal.Amount, paymentRedirectUrl);
        }

        await BookConfirmAndPublishAsync(
            outboundBooking,
            request.Outbound.TripId,
            outboundLockToken,
            outboundSeatNumbers,
            cancellationToken);

        await BookConfirmAndPublishAsync(
            returnBooking,
            request.Return.TripId,
            returnLockToken,
            returnSeatNumbers,
            cancellationToken);

        _logger.LogInformation(
            "Round-trip booking group {BookingGroupId} confirmed with outbound {OutboundBookingId} and return {ReturnBookingId}.",
            bookingGroupId,
            outboundBooking.Id,
            returnBooking.Id);

        return BuildResult(bookingGroupId, outboundBooking, returnBooking, grandTotal.Amount, paymentRedirectUrl);
    }

    private static void EnsureSeatCount(CreateRoundTripBookingCommand.RoundTripBookingLegCommand leg)
    {
        if (leg.Seats.Count > 5)
        {
            throw new CodedValidationException(
                "BOOKING_MAX_SEATS_EXCEEDED",
                "A booking cannot exceed 5 seats.");
        }
    }

    private async Task<TripSnapshot> GetScheduledTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await _tripClient.GetTripSnapshotAsync(tripId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", $"Trip '{tripId}' not found.");
        }

        if (!string.Equals(trip.Status, "SCHEDULED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "BOOKING_TRIP_NOT_BOOKABLE",
                $"Trip '{tripId}' is not in SCHEDULED status.");
        }

        return trip;
    }

    private async Task<Guid> LockLegAsync(
        Guid passengerUserId,
        Guid tripId,
        IReadOnlyList<string> seatNumbers,
        CancellationToken cancellationToken)
    {
        var lockIdempotencyKey = $"lock-{passengerUserId}-{tripId}-{string.Join(",", seatNumbers)}";

        var lockOutcome = await _tripClient.LockSeatsAsync(
            tripId,
            seatNumbers,
            holdOwnerId: passengerUserId,
            idempotencyKey: lockIdempotencyKey,
            ttlSeconds: SeatLockTtlSeconds,
            cancellationToken: cancellationToken);

        return lockOutcome switch
        {
            LockSeatsOutcome.Success success => success.Data.SeatLockToken,
            LockSeatsOutcome.SeatUnavailable unavailable => throw new ConflictException(
                "BOOKING_SEAT_UNAVAILABLE",
                $"One or more seats are unavailable: {string.Join(", ", unavailable.UnavailableSeats)}."),
            LockSeatsOutcome.TripNotBookable notBookable => throw new ConflictException(
                "BOOKING_TRIP_NOT_BOOKABLE",
                notBookable.Message),
            LockSeatsOutcome.TripNotFound => throw new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                $"Trip '{tripId}' not found."),
            LockSeatsOutcome.TransportError transportError => throw new InvalidOperationException(
                $"Seat lock failed: {transportError.Message}"),
            _ => throw new InvalidOperationException("Seat lock failed: Unknown lock error."),
        };
    }

    private BookingEntity CreatePendingBooking(
        Guid passengerUserId,
        CreateRoundTripBookingCommand.RoundTripBookingLegCommand leg,
        TripSnapshot trip,
        Money totalAmount,
        Money discountAmount,
        Guid bookingGroupId,
        TripDirection tripDirection)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(_clock.UtcNow),
            passengerUserId: passengerUserId,
            tripId: leg.TripId,
            operatorId: trip.OperatorId,
            pickupStationId: leg.PickupStationId,
            pickupStopId: leg.PickupStopId,
            dropoffStationId: leg.DropoffStationId,
            dropoffStopId: leg.DropoffStopId,
            baseFare: totalAmount,
            discountAmount: discountAmount,
            totalAmount: totalAmount,
            tripSnapshotOriginName: trip.OriginStation.Name,
            tripSnapshotDestName: trip.DestinationStation.Name,
            tripSnapshotDeparture: trip.DepartureDateTime,
            tripSnapshotRouteName: null,
            bookingGroupId: bookingGroupId,
            tripDirection: tripDirection);

        foreach (var seat in leg.Seats)
        {
            booking.AddPassenger(seat.SeatNumber);
        }

        return booking;
    }

    private async Task<string?> ChargeAsync(
        CreateRoundTripBookingCommand request,
        Guid bookingGroupId,
        BookingEntity outboundBooking,
        BookingEntity returnBooking,
        Money grandTotal,
        Guid outboundLockToken,
        IReadOnlyList<string> outboundSeatNumbers,
        Guid returnLockToken,
        IReadOnlyList<string> returnSeatNumbers,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(request.PaymentMethod, "WALLET", StringComparison.OrdinalIgnoreCase))
            {
                var batchOutcome = await _paymentClient.BatchChargeAsync(
                    userId: request.PassengerUserId,
                    method: request.PaymentMethod,
                    items:
                    [
                        new BatchChargeItem("BOOKING", outboundBooking.Id, outboundBooking.TotalAmount.Amount),
                        new BatchChargeItem("BOOKING", returnBooking.Id, returnBooking.TotalAmount.Amount),
                    ],
                    idempotencyKey: $"charge-round-trip-{request.IdempotencyKey}",
                    cancellationToken: cancellationToken);

                switch (batchOutcome)
                {
                    case BatchChargeOutcome.Success success:
                        EnsureWalletBatchSucceeded(success);
                        return null;
                    case BatchChargeOutcome.InsufficientFunds insufficientFunds:
                        throw new ConflictException("PAYMENT_INSUFFICIENT_WALLET", insufficientFunds.Message);
                    case BatchChargeOutcome.TransportError transportError:
                        throw new InvalidOperationException($"Payment transport error: {transportError.Message}");
                    default:
                        throw new InvalidOperationException("Payment batch charge failed: Unknown payment error.");
                }
            }

            var chargeOutcome = await _paymentClient.ChargeAsync(
                referenceType: "BOOKING_GROUP",
                referenceId: bookingGroupId,
                userId: request.PassengerUserId,
                amount: grandTotal.Amount,
                method: request.PaymentMethod,
                idempotencyKey: $"charge-round-trip-{request.IdempotencyKey}",
                cancellationToken: cancellationToken);

            switch (chargeOutcome)
            {
                case ChargeOutcome.Success success:
                    return success.Data.PaymentRedirectUrl;
                case ChargeOutcome.InsufficientFunds insufficientFunds:
                    throw new ConflictException("PAYMENT_INSUFFICIENT_WALLET", insufficientFunds.Message);
                case ChargeOutcome.TransportError transportError:
                    throw new InvalidOperationException($"Payment transport error: {transportError.Message}");
                default:
                    throw new InvalidOperationException("Payment charge failed: Unknown payment error.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment charge threw for round-trip booking group {BookingGroupId}; releasing seats.", bookingGroupId);
            await ReleaseBothLegsAsync(request, outboundLockToken, outboundSeatNumbers, returnLockToken, returnSeatNumbers, cancellationToken);
            throw;
        }
    }

    private static void EnsureWalletBatchSucceeded(BatchChargeOutcome.Success success)
    {
        var payments = success.Payments;

        if (payments.Count != 2
            || payments.Any(p => !string.Equals(p.ReferenceType, "BOOKING", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(p.Status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                || p.ReferenceId == Guid.Empty))
        {
            throw new InvalidOperationException("Payment batch charge did not return succeeded BOOKING payments for both legs.");
        }
    }

    private async Task BookConfirmAndPublishAsync(
        BookingEntity booking,
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        CancellationToken cancellationToken)
    {
        var passengerAssignments = booking.Passengers
            .Select(p => new PassengerSeatAssignment(p.Id, p.SeatNumber))
            .ToList();

        var booked = await _tripClient.BookSeatsAsync(
            tripId,
            seatLockToken,
            booking.Id,
            passengerAssignments,
            cancellationToken);

        if (!booked)
        {
            await _bookingService.ReleaseSeatsAsync(tripId, seatLockToken, seatNumbers, cancellationToken);
            throw new ConflictException(
                "BOOKING_SEAT_UNAVAILABLE",
                "Seat lock expired before booking could be confirmed.");
        }

        booking.Confirm(_clock.UtcNow);

        var confirmedEvent = new
        {
            bookingId = booking.Id,
            tripId = booking.TripId,
            totalAmount = booking.TotalAmount.Amount,
            userId = booking.PassengerUserId,
            voucherUsageId = (Guid?)null,
        };

        await _outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(confirmedEvent),
            cancellationToken);
    }

    private async Task ReleaseBothLegsAsync(
        CreateRoundTripBookingCommand request,
        Guid outboundLockToken,
        IReadOnlyList<string> outboundSeatNumbers,
        Guid returnLockToken,
        IReadOnlyList<string> returnSeatNumbers,
        CancellationToken cancellationToken)
    {
        await _bookingService.ReleaseSeatsAsync(
            request.Outbound.TripId,
            outboundLockToken,
            outboundSeatNumbers,
            cancellationToken);

        await _bookingService.ReleaseSeatsAsync(
            request.Return.TripId,
            returnLockToken,
            returnSeatNumbers,
            cancellationToken);
    }

    private static CreateRoundTripBookingResult BuildResult(
        Guid bookingGroupId,
        BookingEntity outboundBooking,
        BookingEntity returnBooking,
        long grandTotal,
        string? paymentRedirectUrl)
        => new(
            bookingGroupId,
            new CreateRoundTripBookingResult.RoundTripBookingResult(
                outboundBooking.Id,
                outboundBooking.BookingCode.Value,
                outboundBooking.TotalAmount.Amount,
                outboundBooking.DiscountAmount.Amount),
            new CreateRoundTripBookingResult.RoundTripBookingResult(
                returnBooking.Id,
                returnBooking.BookingCode.Value,
                returnBooking.TotalAmount.Amount,
                returnBooking.DiscountAmount.Amount),
            grandTotal,
            paymentRedirectUrl);
}
