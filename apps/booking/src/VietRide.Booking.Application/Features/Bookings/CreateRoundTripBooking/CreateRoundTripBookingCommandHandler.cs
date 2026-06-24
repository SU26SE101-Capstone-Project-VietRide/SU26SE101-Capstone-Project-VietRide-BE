using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Entities;
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
///   <item>Validate voucher per leg independently (read-only); apply group-level usage-limit cap
///   (outbound-first) to prevent TOCTOU over-application when totalUsageLimit or perUserLimit
///   would be exceeded across both legs.</item>
///   <item>Create two PENDING_PAYMENT bookings with independent fares and per-leg discounts.</item>
///   <item>Record one VoucherUsage row per leg (same transaction, each carries its booking_id + booking_group_id).</item>
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
    private readonly IVoucherService _voucherService;
    private readonly IVoucherRepository _voucherRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<CreateRoundTripBookingCommandHandler> _logger;

    public CreateRoundTripBookingCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IPaymentServiceClient paymentClient,
        IBookingService bookingService,
        IVoucherService voucherService,
        IVoucherRepository voucherRepository,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<CreateRoundTripBookingCommandHandler> logger)
    {
        _bookings = bookings;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
        _bookingService = bookingService;
        _voucherService = voucherService;
        _voucherRepository = voucherRepository;
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

        var roundTripLockOutcome = await _tripClient.LockRoundTripSeatsAsync(
            request.Outbound.TripId,
            outboundSeatNumbers,
            request.Return.TripId,
            returnSeatNumbers,
            request.PassengerUserId,
            idempotencyKey: $"lock-round-trip-{request.IdempotencyKey}",
            ttlSeconds: SeatLockTtlSeconds,
            cancellationToken: cancellationToken);

        var (outboundLockToken, returnLockToken) = roundTripLockOutcome switch
        {
            LockRoundTripSeatsOutcome.Success success => (success.Outbound.SeatLockToken, success.Return.SeatLockToken),
            LockRoundTripSeatsOutcome.SeatUnavailable unavailable => throw new ConflictException(
                "BOOKING_SEAT_UNAVAILABLE",
                $"One or more seats are unavailable: {string.Join(", ", unavailable.UnavailableSeats)}."),
            LockRoundTripSeatsOutcome.TripNotBookable notBookable => throw new ConflictException(
                "BOOKING_TRIP_NOT_BOOKABLE",
                notBookable.Message),
            LockRoundTripSeatsOutcome.TripNotFound notFound => throw new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                $"Trip '{notFound.TripId}' not found."),
            LockRoundTripSeatsOutcome.TransportError transportError => throw new InvalidOperationException(
                $"Round-trip seat lock failed: {transportError.Message}"),
            _ => throw new InvalidOperationException("Round-trip seat lock failed: Unknown lock error."),
        };

        var bookingGroupId = Guid.NewGuid();
        var now = _clock.UtcNow;
        var outboundBaseFare = Money.FromRaw(outboundTrip.BaseFare);
        var returnBaseFare = Money.FromRaw(returnTrip.BaseFare);

        // -----------------------------------------------------------------------
        // 4. Validate voucher per leg independently (read-only — no DB writes yet).
        //
        //    Each leg's min-order is checked against its own baseFare. VOUCHER_MIN_ORDER_NOT_MET
        //    is caught and silently skips that leg (discount = 0, no usage row).
        //
        //    TOCTOU group-level cap (B1):
        //    After both per-leg validations, we take a consistent snapshot of the current
        //    usage counts and cap how many of the two legs can actually consume the voucher.
        //    This prevents a totalUsageLimit=1 (or perUserLimit=1) voucher from being applied
        //    to both legs because both saw the same stale count before either usage was written.
        //    Semantics: outbound-first — if only one slot remains, only the outbound leg gets
        //    the discount; the return leg is treated as no-voucher (discount 0, no usage row).
        // -----------------------------------------------------------------------
        var outboundDiscount = Money.Zero;
        var returnDiscount = Money.Zero;
        Guid? outboundValidatedVoucherId = null;
        Guid? returnValidatedVoucherId = null;

        if (!string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            // Outbound leg
            try
            {
                var outboundValidation = await _voucherService.ValidateAndComputeDiscountAsync(
                    voucherCode: request.VoucherCode,
                    operatorId: outboundTrip.OperatorId,
                    routeId: outboundTrip.RouteId,
                    userId: request.PassengerUserId,
                    orderAmount: outboundBaseFare,
                    now: now,
                    ct: cancellationToken);
                outboundDiscount = outboundValidation.Discount;
                outboundValidatedVoucherId = outboundValidation.VoucherId;
            }
            catch (CodedValidationException ex) when (ex.ErrorCode == "VOUCHER_MIN_ORDER_NOT_MET")
            {
                // Outbound leg does not meet min-order — skip discount for this leg only.
                _logger.LogDebug(
                    "Voucher '{VoucherCode}' not applied to outbound leg: min-order not met.",
                    request.VoucherCode);
            }

            // Return leg — use same voucher code; validate independently.
            try
            {
                var returnValidation = await _voucherService.ValidateAndComputeDiscountAsync(
                    voucherCode: request.VoucherCode,
                    operatorId: returnTrip.OperatorId,
                    routeId: returnTrip.RouteId,
                    userId: request.PassengerUserId,
                    orderAmount: returnBaseFare,
                    now: now,
                    ct: cancellationToken);
                returnDiscount = returnValidation.Discount;
                returnValidatedVoucherId = returnValidation.VoucherId;
            }
            catch (CodedValidationException ex) when (ex.ErrorCode == "VOUCHER_MIN_ORDER_NOT_MET")
            {
                // Return leg does not meet min-order — skip discount for this leg only.
                _logger.LogDebug(
                    "Voucher '{VoucherCode}' not applied to return leg: min-order not met.",
                    request.VoucherCode);
            }

            // -----------------------------------------------------------------------
            // Group-level usage-limit cap (B1 fix).
            //
            // Both ValidateAndComputeDiscountAsync calls read CountUsagesAsync /
            // CountUsagesByUserAsync independently — they see the same pre-write count, so
            // a voucher with remaining capacity = 1 passes both per-leg checks.
            // We re-read the counts here (one snapshot after both validates) and cap the
            // number of legs that can consume the voucher to the actual remaining slots.
            //
            // Only executed when at least one leg validated successfully AND both legs
            // resolved to the same voucherId (same voucher object — normal case since the
            // same voucherCode is used for both legs).
            // -----------------------------------------------------------------------
            if (outboundValidatedVoucherId.HasValue || returnValidatedVoucherId.HasValue)
            {
                // All successful validations resolve to the same voucherId for the same code.
                var voucherId = outboundValidatedVoucherId ?? returnValidatedVoucherId!.Value;

                var allowed = await ComputeAllowedLegsAsync(
                    voucherId,
                    request.PassengerUserId,
                    legsWantingVoucher: (outboundValidatedVoucherId.HasValue ? 1 : 0)
                                      + (returnValidatedVoucherId.HasValue ? 1 : 0),
                    cancellationToken);

                // Outbound is always preferred: if only 1 slot remains and both legs
                // passed validation, the return leg loses its discount.
                if (allowed == 0)
                {
                    // Neither leg can be covered despite per-leg validation passing.
                    // (Race: another request consumed the last slot between validate and here.)
                    outboundDiscount = Money.Zero;
                    returnDiscount = Money.Zero;
                    outboundValidatedVoucherId = null;
                    returnValidatedVoucherId = null;
                    _logger.LogDebug(
                        "Voucher '{VoucherCode}' group-level cap: 0 slots remaining — no legs discounted.",
                        request.VoucherCode);
                }
                else if (allowed == 1 && outboundValidatedVoucherId.HasValue && returnValidatedVoucherId.HasValue)
                {
                    // Only one slot remains — keep outbound, drop return.
                    returnDiscount = Money.Zero;
                    returnValidatedVoucherId = null;
                    _logger.LogDebug(
                        "Voucher '{VoucherCode}' group-level cap: 1 slot remaining — only outbound leg discounted.",
                        request.VoucherCode);
                }
                // allowed >= 2: both legs keep their discounts (no cap needed).
            }
        }

        var outboundTotal = outboundBaseFare - outboundDiscount;
        var returnTotal = returnBaseFare - returnDiscount;

        BookingEntity outboundBooking;
        BookingEntity returnBooking;
        Guid? outboundVoucherUsageId = null;
        Guid? returnVoucherUsageId = null;
        try
        {
            outboundBooking = CreatePendingBooking(
                request.PassengerUserId,
                request.Outbound,
                outboundTrip,
                outboundBaseFare,
                outboundDiscount,
                outboundTotal,
                bookingGroupId,
                TripDirection.OUTBOUND,
                outboundLockToken);

            returnBooking = CreatePendingBooking(
                request.PassengerUserId,
                request.Return,
                returnTrip,
                returnBaseFare,
                returnDiscount,
                returnTotal,
                bookingGroupId,
                TripDirection.RETURN,
                returnLockToken);

            await _bookings.AddAsync(outboundBooking, cancellationToken);
            await _bookings.AddAsync(returnBooking, cancellationToken);

            // Record VoucherUsage rows (same DbContext UoW) now that booking IDs are known.
            // Each row carries its own booking_id + the shared booking_group_id.
            if (outboundValidatedVoucherId.HasValue)
            {
                outboundVoucherUsageId = await _voucherService.RecordUsageAsync(
                    voucherId: outboundValidatedVoucherId.Value,
                    userId: request.PassengerUserId,
                    bookingId: outboundBooking.Id,
                    bookingGroupId: bookingGroupId,
                    discountAmount: outboundDiscount,
                    ct: cancellationToken);
            }

            if (returnValidatedVoucherId.HasValue)
            {
                returnVoucherUsageId = await _voucherService.RecordUsageAsync(
                    voucherId: returnValidatedVoucherId.Value,
                    userId: request.PassengerUserId,
                    bookingId: returnBooking.Id,
                    bookingGroupId: bookingGroupId,
                    discountAmount: returnDiscount,
                    ct: cancellationToken);
            }
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
            outboundVoucherUsageId,
            returnVoucherUsageId,
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
            outboundVoucherUsageId,
            cancellationToken);

        await BookConfirmAndPublishAsync(
            returnBooking,
            request.Return.TripId,
            returnLockToken,
            returnSeatNumbers,
            returnVoucherUsageId,
            cancellationToken);

        _logger.LogInformation(
            "Round-trip booking group {BookingGroupId} confirmed with outbound {OutboundBookingId} and return {ReturnBookingId}.",
            bookingGroupId,
            outboundBooking.Id,
            returnBooking.Id);

        return BuildResult(bookingGroupId, outboundBooking, returnBooking, grandTotal.Amount, paymentRedirectUrl);
    }

    // -----------------------------------------------------------------------
    // Group-level usage-limit cap helper (B1)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the number of round-trip legs (0, 1, or 2) that may consume the voucher,
    /// based on a consistent post-validate snapshot of the usage counts.
    /// <para>
    /// <paramref name="legsWantingVoucher"/> is how many legs passed per-leg validation (1 or 2).
    /// We cap to the minimum of: remaining total slots and remaining per-user slots.
    /// </para>
    /// </summary>
    private async Task<int> ComputeAllowedLegsAsync(
        Guid voucherId,
        Guid userId,
        int legsWantingVoucher,
        CancellationToken ct)
    {
        var voucher = await _voucherRepository.GetByIdAsync(voucherId, ct);
        if (voucher is null)
        {
            // Voucher disappeared between validate and cap check — treat as 0 allowed.
            return 0;
        }

        // Remaining total slots (null limit = unlimited = int.MaxValue)
        var remainingTotal = int.MaxValue;
        if (voucher.TotalUsageLimit.HasValue)
        {
            var currentTotal = await _voucherRepository.CountUsagesAsync(voucherId, ct);
            remainingTotal = Math.Max(0, voucher.TotalUsageLimit.Value - currentTotal);
        }

        // Remaining per-user slots (null limit = unlimited = int.MaxValue)
        var remainingUser = int.MaxValue;
        if (voucher.PerUserLimit.HasValue)
        {
            var currentUser = await _voucherRepository.CountUsagesByUserAsync(voucherId, userId, ct);
            remainingUser = Math.Max(0, voucher.PerUserLimit.Value - currentUser);
        }

        var allowed = Math.Min(remainingTotal, remainingUser);

        // Never exceed what the per-leg validations already approved.
        return Math.Min(allowed, legsWantingVoucher);
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

    private BookingEntity CreatePendingBooking(
        Guid passengerUserId,
        CreateRoundTripBookingCommand.RoundTripBookingLegCommand leg,
        TripSnapshot trip,
        Money baseFare,
        Money discountAmount,
        Money totalAmount,
        Guid bookingGroupId,
        TripDirection tripDirection,
        Guid seatLockToken)
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
            baseFare: baseFare,
            discountAmount: discountAmount,
            totalAmount: totalAmount,
            tripSnapshotOriginName: trip.OriginStation.Name,
            tripSnapshotDestName: trip.DestinationStation.Name,
            tripSnapshotDeparture: trip.DepartureDateTime,
            tripSnapshotRouteName: null,
            bookingGroupId: bookingGroupId,
            tripDirection: tripDirection,
            seatLockToken: seatLockToken);

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
        Guid? outboundVoucherUsageId,
        Guid? returnVoucherUsageId,
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
                        EnsureWalletBatchSucceeded(success, outboundBooking.Id, returnBooking.Id);
                        return null;
                    case BatchChargeOutcome.InsufficientFunds insufficientFunds:
                        await CompensateSeatsAndVouchersAsync(
                            request, outboundLockToken, outboundSeatNumbers, returnLockToken, returnSeatNumbers,
                            outboundBooking.Id, returnBooking.Id, outboundVoucherUsageId, returnVoucherUsageId,
                            cancellationToken);
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
                    // S1 fix: compensate voucher usages before re-throwing (mirrors WALLET path).
                    await CompensateSeatsAndVouchersAsync(
                        request, outboundLockToken, outboundSeatNumbers, returnLockToken, returnSeatNumbers,
                        outboundBooking.Id, returnBooking.Id, outboundVoucherUsageId, returnVoucherUsageId,
                        cancellationToken);
                    throw new ConflictException("PAYMENT_INSUFFICIENT_WALLET", insufficientFunds.Message);
                case ChargeOutcome.TransportError transportError:
                    throw new InvalidOperationException($"Payment transport error: {transportError.Message}");
                default:
                    throw new InvalidOperationException("Payment charge failed: Unknown payment error.");
            }
        }
        catch (ConflictException)
        {
            // ConflictException paths above have already compensated; re-throw without a
            // second compensation.
            throw;
        }
        catch (Exception ex)
        {
            // S2 fix: any non-ConflictException (e.g. EnsureWalletBatchSucceeded,
            // TransportError → wrapped InvalidOperationException, network failure) must
            // also compensate voucher usage rows, not only seats.
            _logger.LogError(
                ex,
                "Payment charge threw for round-trip booking group {BookingGroupId}; compensating seats and voucher usages.",
                bookingGroupId);
            await CompensateSeatsAndVouchersAsync(
                request, outboundLockToken, outboundSeatNumbers, returnLockToken, returnSeatNumbers,
                outboundBooking.Id, returnBooking.Id, outboundVoucherUsageId, returnVoucherUsageId,
                cancellationToken);
            throw;
        }
    }

    private static void EnsureWalletBatchSucceeded(
        BatchChargeOutcome.Success success,
        Guid outboundBookingId,
        Guid returnBookingId)
    {
        var payments = success.Payments;
        var expectedReferenceIds = new HashSet<Guid> { outboundBookingId, returnBookingId };

        if (payments.Count != 2
            || payments.Any(p => !string.Equals(p.ReferenceType, "BOOKING", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(p.Status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                || p.ReferenceId == Guid.Empty)
            || !payments.Select(p => p.ReferenceId).ToHashSet().SetEquals(expectedReferenceIds))
        {
            throw new InvalidOperationException("Payment batch charge did not return succeeded BOOKING payments for both legs.");
        }
    }

    private async Task BookConfirmAndPublishAsync(
        BookingEntity booking,
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        Guid? voucherUsageId,
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

        // voucherUsageId propagated in event payload (BSOT:1741 optional field).
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
    }

    /// <summary>
    /// Compensation: release seats for both legs and (if voucher usages were created)
    /// physically delete them. Best-effort — logs but does not re-throw.
    /// </summary>
    /// <param name="outboundVoucherUsageId">Presence guard — <c>.HasValue</c> indicates a usage
    /// row was written for the outbound leg. CompensateAsync takes the booking ID, not the usage
    /// ID; the value itself is not passed through.</param>
    /// <param name="returnVoucherUsageId">Presence guard — same semantics as
    /// <paramref name="outboundVoucherUsageId"/> for the return leg.</param>
    private async Task CompensateSeatsAndVouchersAsync(
        CreateRoundTripBookingCommand request,
        Guid outboundLockToken,
        IReadOnlyList<string> outboundSeatNumbers,
        Guid returnLockToken,
        IReadOnlyList<string> returnSeatNumbers,
        Guid outboundBookingId,
        Guid returnBookingId,
        Guid? outboundVoucherUsageId,
        Guid? returnVoucherUsageId,
        CancellationToken cancellationToken)
    {
        await ReleaseBothLegsAsync(
            request,
            outboundLockToken,
            outboundSeatNumbers,
            returnLockToken,
            returnSeatNumbers,
            cancellationToken);

        if (outboundVoucherUsageId.HasValue)
        {
            try
            {
                await _voucherService.CompensateAsync(outboundBookingId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to delete voucher usage for outbound booking {BookingId} during compensation.",
                    outboundBookingId);
            }
        }

        if (returnVoucherUsageId.HasValue)
        {
            try
            {
                await _voucherService.CompensateAsync(returnBookingId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to delete voucher usage for return booking {BookingId} during compensation.",
                    returnBookingId);
            }
        }
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
