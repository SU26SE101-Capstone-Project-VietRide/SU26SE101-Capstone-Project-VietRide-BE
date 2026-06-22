using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Features.Bookings.CreateBooking;

/// <summary>
/// Handles POST /v1/bookings — the core seat-lock booking saga.
/// <para>Saga happy path (WALLET):</para>
/// <list type="number">
///   <item>Fetch trip snapshot → validate trip is SCHEDULED.</item>
///   <item>Lock seats (all-or-nothing) via <see cref="ITripServiceClient"/>.</item>
///   <item>Validate voucher + compute discount (read-only) via <see cref="IVoucherService"/>.</item>
///   <item>Create <see cref="BookingEntity"/> PENDING_PAYMENT (with correct discount) + N
///   <see cref="Passenger"/> rows in one tx.</item>
///   <item>Record VoucherUsage row (same DbContext UoW) via <see cref="IVoucherService"/>.</item>
///   <item>Charge via <see cref="IPaymentServiceClient"/> (stub success this day).</item>
///   <item>On success: call book-seats → Confirm() → enqueue booking.booking.confirmed (same tx).</item>
///   <item>On any downstream failure after lock: release-seats via <see cref="IBookingService"/>;
///   if a VoucherUsage was written, physically DELETE it via <see cref="IVoucherService.CompensateAsync"/>.</item>
/// </list>
/// <para>
/// Compensation (release-seats) lives in <see cref="IBookingService.ReleaseSeatsAsync"/>
/// per BSOT §3.2.5/§3.2.6 line 686 — NOT inlined here, so Day-17 cancel/refund reuses it.
/// </para>
/// </summary>
public sealed class CreateBookingCommandHandler
    : IRequestHandler<CreateBookingCommand, CreateBookingResult>
{
    private const string EventType = "booking.booking.confirmed";
    private const int SeatLockTtlSeconds = 10 * 60; // SEAT_LOCK_TTL_MINUTES=10 (BSOT §10 line 2360)

    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IBookingService _bookingService;
    private readonly IVoucherService _voucherService;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<CreateBookingCommandHandler> _logger;

    public CreateBookingCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IPaymentServiceClient paymentClient,
        IBookingService bookingService,
        IVoucherService voucherService,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<CreateBookingCommandHandler> logger)
    {
        _bookings = bookings;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
        _bookingService = bookingService;
        _voucherService = voucherService;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateBookingResult> Handle(
        CreateBookingCommand request,
        CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // 0. Business-rule guard — max 5 seats per booking (BSOT §5.9 registered code 422)
        //    FluentValidation catches shape errors in the pipeline; this guard fires for
        //    any path that bypasses validation (e.g. direct handler calls in tests) and
        //    surfaces BOOKING_MAX_SEATS_EXCEEDED with HTTP 422 via CodedValidationException.
        // -----------------------------------------------------------------------
        if (request.Seats.Count > 5)
        {
            throw new CodedValidationException(
                "BOOKING_MAX_SEATS_EXCEEDED",
                "A booking cannot exceed 5 seats.");
        }

        // -----------------------------------------------------------------------
        // 1. Fetch trip snapshot — validate trip exists and is SCHEDULED
        // -----------------------------------------------------------------------
        var trip = await _tripClient.GetTripSnapshotAsync(request.TripId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", $"Trip '{request.TripId}' not found.");
        }

        if (!string.Equals(trip.Status, "SCHEDULED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "BOOKING_TRIP_NOT_BOOKABLE",
                $"Trip '{request.TripId}' is not in SCHEDULED status.");
        }

        // -----------------------------------------------------------------------
        // 3. Lock seats (all-or-nothing)
        // -----------------------------------------------------------------------
        var seatNumbers = request.Seats.Select(s => s.SeatNumber).ToList();
        var lockIdempotencyKey = $"lock-{request.PassengerUserId}-{request.TripId}-{string.Join(",", seatNumbers)}";

        var lockOutcome = await _tripClient.LockSeatsAsync(
            request.TripId,
            seatNumbers,
            holdOwnerId: request.PassengerUserId,
            idempotencyKey: lockIdempotencyKey,
            ttlSeconds: SeatLockTtlSeconds,
            cancellationToken: cancellationToken);

        // Map lock outcome to typed exceptions — no booking created on lock failure
        Guid seatLockToken;
        switch (lockOutcome)
        {
            case LockSeatsOutcome.Success success:
                seatLockToken = success.Data.SeatLockToken;
                break;

            case LockSeatsOutcome.SeatUnavailable unavailable:
                throw new ConflictException(
                    "BOOKING_SEAT_UNAVAILABLE",
                    $"One or more seats are unavailable: {string.Join(", ", unavailable.UnavailableSeats)}.");

            case LockSeatsOutcome.TripNotBookable notBookable:
                throw new ConflictException("BOOKING_TRIP_NOT_BOOKABLE", notBookable.Message);

            case LockSeatsOutcome.TripNotFound:
                throw new CodedNotFoundException("TRIP_NOT_FOUND", $"Trip '{request.TripId}' not found.");

            default:
                // TODO Day 15/16: map LockSeatsOutcome.TransportError to a registered BSOT §5.9
                // error code (e.g. TRIP_SERVICE_UNAVAILABLE) instead of surfacing 500 INTERNAL_ERROR.
                var errorMsg = lockOutcome is LockSeatsOutcome.TransportError te ? te.Message : "Unknown lock error.";
                throw new InvalidOperationException($"Seat lock failed: {errorMsg}");
        }

        // -----------------------------------------------------------------------
        // 4. Validate voucher + compute discount (read-only — no DB writes yet)
        //    Voucher errors (VOUCHER_NOT_FOUND / _EXPIRED / _NOT_APPLICABLE etc.) propagate
        //    before any booking row exists; seats are locked but a cleanup job handles orphaned locks.
        // -----------------------------------------------------------------------
        var now = _clock.UtcNow;
        var baseFare = Money.FromRaw(trip.BaseFare);

        Money discountAmount = Money.Zero;
        Guid? validatedVoucherId = null;

        if (!string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            var validation = await _voucherService.ValidateAndComputeDiscountAsync(
                voucherCode: request.VoucherCode,
                operatorId: trip.OperatorId,
                routeId: trip.RouteId,
                userId: request.PassengerUserId,
                orderAmount: baseFare,
                now: now,
                ct: cancellationToken);

            discountAmount = validation.Discount;
            validatedVoucherId = validation.VoucherId;
        }

        var totalAmount = baseFare - discountAmount;

        // -----------------------------------------------------------------------
        // 5. Create Booking PENDING_PAYMENT + Passengers in one transaction
        //    (TransactionBehavior wraps the handler — SaveChanges called by UoW)
        // -----------------------------------------------------------------------
        BookingEntity booking;
        Guid? voucherUsageId = null;

        try
        {
            var bookingCode = BookingCode.Generate(now);

            booking = BookingEntity.CreatePendingPayment(
                bookingCode: bookingCode,
                passengerUserId: request.PassengerUserId,
                tripId: request.TripId,
                operatorId: trip.OperatorId,
                pickupStationId: request.PickupStationId,
                pickupStopId: request.PickupStopId,
                dropoffStationId: request.DropoffStationId,
                dropoffStopId: request.DropoffStopId,
                baseFare: baseFare,
                discountAmount: discountAmount,
                totalAmount: totalAmount,
                tripSnapshotOriginName: trip.OriginStation.Name,
                tripSnapshotDestName: trip.DestinationStation.Name,
                tripSnapshotDeparture: trip.DepartureDateTime,
                tripSnapshotRouteName: null);

            // Add passenger rows (operational-only — no PII stored)
            foreach (var seat in request.Seats)
            {
                booking.AddPassenger(seat.SeatNumber);
            }

            await _bookings.AddAsync(booking, cancellationToken);

            // Record VoucherUsage row (same DbContext UoW) now that booking.Id is known.
            if (validatedVoucherId.HasValue)
            {
                voucherUsageId = await _voucherService.RecordUsageAsync(
                    voucherId: validatedVoucherId.Value,
                    userId: request.PassengerUserId,
                    bookingId: booking.Id,
                    bookingGroupId: null,
                    discountAmount: discountAmount,
                    ct: cancellationToken);
            }
        }
        catch
        {
            // Booking entity creation failed — release the held seats (compensation).
            // voucherUsageId is null here (usage row not yet committed) so no usage delete needed.
            await _bookingService.ReleaseSeatsAsync(
                tripId: request.TripId,
                seatLockToken: seatLockToken,
                seatNumbers: seatNumbers,
                ct: cancellationToken);
            throw;
        }

        // -----------------------------------------------------------------------
        // 6. Charge payment (stub returns success for WALLET this day)
        //    Handler does NOT flip CONFIRMED unless the seam returns success.
        // -----------------------------------------------------------------------
        string? paymentRedirectUrl = null;
        var chargeIdempotencyKey = $"charge-{booking.Id}";

        ChargeOutcome chargeOutcome;
        try
        {
            chargeOutcome = await _paymentClient.ChargeAsync(
                referenceType: "BOOKING",
                referenceId: booking.Id,
                userId: request.PassengerUserId,
                amount: totalAmount.Amount,
                method: request.PaymentMethod,
                idempotencyKey: chargeIdempotencyKey,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment charge threw for booking {BookingId}; releasing seats.", booking.Id);
            await CompensateSeatsAndVoucherAsync(
                request.TripId, seatLockToken, seatNumbers, booking.Id, voucherUsageId, cancellationToken);
            throw;
        }

        if (chargeOutcome is ChargeOutcome.InsufficientFunds insuf)
        {
            await CompensateSeatsAndVoucherAsync(
                request.TripId, seatLockToken, seatNumbers, booking.Id, voucherUsageId, cancellationToken);
            // PAYMENT_INSUFFICIENT_WALLET (BSOT §5.9 registered code, 402).
            // ConflictException maps to 409; 402 mapping is deferred to Day 15/16 payment work.
            throw new ConflictException("PAYMENT_INSUFFICIENT_WALLET", insuf.Message);
        }

        if (chargeOutcome is ChargeOutcome.TransportError transportError)
        {
            await CompensateSeatsAndVoucherAsync(
                request.TripId, seatLockToken, seatNumbers, booking.Id, voucherUsageId, cancellationToken);
            // TODO Day 15/16: map ChargeOutcome.TransportError to a registered BSOT §5.9
            // error code (e.g. PAYMENT_SERVICE_UNAVAILABLE) instead of surfacing 500 INTERNAL_ERROR.
            throw new InvalidOperationException($"Payment transport error: {transportError.Message}");
        }

        var chargeSuccess = (ChargeOutcome.Success)chargeOutcome;
        paymentRedirectUrl = chargeSuccess.Data.PaymentRedirectUrl;
        var chargeStatus = chargeSuccess.Data.Status;

        // VNPay path — leave PENDING_PAYMENT, return redirect URL; no seat confirmation yet
        if (string.Equals(request.PaymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(chargeStatus, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return new CreateBookingResult(
                BookingId: booking.Id,
                BookingCode: booking.BookingCode.Value,
                Status: booking.Status.ToString(),
                TotalAmount: booking.TotalAmount.Amount,
                DiscountAmount: booking.DiscountAmount.Amount,
                PaymentRedirectUrl: paymentRedirectUrl);
        }

        // -----------------------------------------------------------------------
        // 7. WALLET path — book-seats → Confirm → enqueue confirmed event
        //    (all in the same TransactionBehavior transaction)
        // -----------------------------------------------------------------------
        var passengerAssignments = booking.Passengers
            .Select(p => new PassengerSeatAssignment(p.Id, p.SeatNumber))
            .ToList();

        var booked = await _tripClient.BookSeatsAsync(
            request.TripId,
            seatLockToken,
            booking.Id,
            passengerAssignments,
            cancellationToken);

        if (!booked)
        {
            // Lock expired between lock and book — compensate and surface error
            await CompensateSeatsAndVoucherAsync(
                request.TripId, seatLockToken, seatNumbers, booking.Id, voucherUsageId, cancellationToken);
            throw new ConflictException(
                "BOOKING_SEAT_UNAVAILABLE",
                "Seat lock expired before booking could be confirmed.");
        }

        booking.Confirm(now);

        // Enqueue booking.booking.confirmed (same tx — outbox committed by TransactionBehavior).
        // voucherUsageId propagated in event payload (BSOT:1741 optional field, replaces hardcoded null).
        var confirmedEvent = new
        {
            bookingId = booking.Id,
            tripId = booking.TripId,
            totalAmount = booking.TotalAmount.Amount,
            userId = booking.PassengerUserId,
            voucherUsageId,
        };

        await _outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(confirmedEvent),
            cancellationToken);

        _logger.LogInformation(
            "Booking {BookingId} confirmed for trip {TripId}, {SeatCount} seat(s).",
            booking.Id,
            booking.TripId,
            booking.Passengers.Count);

        return new CreateBookingResult(
            BookingId: booking.Id,
            BookingCode: booking.BookingCode.Value,
            Status: booking.Status.ToString(),
            TotalAmount: booking.TotalAmount.Amount,
            DiscountAmount: booking.DiscountAmount.Amount,
            PaymentRedirectUrl: paymentRedirectUrl);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Compensation: release seats and (if a voucher usage was created) physically delete it.
    /// Best-effort — logs but does not re-throw (mirrors IBookingService.ReleaseSeatsAsync contract).
    /// </summary>
    private async Task CompensateSeatsAndVoucherAsync(
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        Guid bookingId,
        Guid? voucherUsageId,
        CancellationToken ct)
    {
        await _bookingService.ReleaseSeatsAsync(tripId, seatLockToken, seatNumbers, ct);

        if (voucherUsageId.HasValue)
        {
            try
            {
                await _voucherService.CompensateAsync(bookingId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to delete voucher usage for booking {BookingId} during compensation.",
                    bookingId);
            }
        }
    }
}
