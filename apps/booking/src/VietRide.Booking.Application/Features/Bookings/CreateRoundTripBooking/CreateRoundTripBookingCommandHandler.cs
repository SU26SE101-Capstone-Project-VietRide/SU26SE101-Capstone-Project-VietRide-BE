using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Application.Features.Bookings.History;
using VietRide.Booking.Domain.Constants;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;
    private readonly ITripServiceClient _tripClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IBookingService _bookingService;
    private readonly IVoucherService _voucherService;
    private readonly IVoucherRepository _voucherRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IBookingStationCanonicalizer _stationCanonicalizer;
    private readonly IIdentityUserServiceClient _identityUsers;
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
        ILogger<CreateRoundTripBookingCommandHandler> logger,
        IBookingStatusHistoryRepository statusHistory,
        IBookingStationCanonicalizer stationCanonicalizer,
        IIdentityUserServiceClient identityUsers)
    {
        _bookings = bookings;
        _statusHistory = statusHistory;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
        _bookingService = bookingService;
        _voucherService = voucherService;
        _voucherRepository = voucherRepository;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
        _stationCanonicalizer = stationCanonicalizer;
        _identityUsers = identityUsers;
    }

    public async Task<CreateRoundTripBookingResult> Handle(
        CreateRoundTripBookingCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        GuardMobileReturnMode(request.PaymentMethod, request.PaymentReturnMode);

        EnsureSeatCount(request.Outbound);
        EnsureSeatCount(request.Return);

        var outboundTrip = await GetScheduledTripAsync(request.Outbound.TripId, now, cancellationToken);
        var returnTrip = await GetScheduledTripAsync(request.Return.TripId, now, cancellationToken);
        var stationCanonicalization = await _stationCanonicalizer.LockAndResolveAsync(
            BookingStationCanonicalization.Collect(
                request.Outbound.PickupStationId,
                request.Outbound.DropoffStationId,
                request.Return.PickupStationId,
                request.Return.DropoffStationId,
                outboundTrip.OriginStation.Id,
                outboundTrip.DestinationStation.Id,
                returnTrip.OriginStation.Id,
                returnTrip.DestinationStation.Id),
            cancellationToken);
        request = request with
        {
            Outbound = request.Outbound with
            {
                PickupStationId = stationCanonicalization.Resolve(request.Outbound.PickupStationId),
                DropoffStationId = stationCanonicalization.Resolve(request.Outbound.DropoffStationId),
            },
            Return = request.Return with
            {
                PickupStationId = stationCanonicalization.Resolve(request.Return.PickupStationId),
                DropoffStationId = stationCanonicalization.Resolve(request.Return.DropoffStationId),
            },
        };
        outboundTrip = BookingStationCanonicalization.ResolveTrip(outboundTrip, stationCanonicalization);
        returnTrip = BookingStationCanonicalization.ResolveTrip(returnTrip, stationCanonicalization);
        ValidateStopSelections(outboundTrip, request.Outbound.PickupStopId, request.Outbound.DropoffStopId);
        ValidateStopSelections(returnTrip, request.Return.PickupStopId, request.Return.DropoffStopId);
        var outboundShuttleDistances = await ValidateShuttleRequestAsync(
            request.Outbound, outboundTrip, now, cancellationToken);
        var returnShuttleDistances = await ValidateShuttleRequestAsync(
            request.Return, returnTrip, now, cancellationToken);

        if (outboundTrip.ReturnRouteId is null)
        {
            throw new CodedValidationException(
                "ROUTE_RETURN_NOT_CONFIGURED",
                "Outbound route is not configured with a return route.");
        }

        if (returnTrip.RouteId != outboundTrip.ReturnRouteId.Value)
        {
            throw new CodedValidationException(
                "BOOKING_ROUND_TRIP_INVALID",
                "Return trip does not use the outbound route's configured return route.",
                [new ValidationError("return.tripId", "Return trip does not use the configured return route.")]);
        }

        if (returnTrip.DepartureDateTime <= outboundTrip.EstimatedArrivalTime)
        {
            throw new CodedValidationException(
                "BOOKING_ROUND_TRIP_INVALID",
                "Return trip departure must be after outbound trip arrival.");
        }

        var buyerProfile = await GetRequiredBuyerProfileAsync(
            request.PassengerUserId,
            cancellationToken);

        var outboundSeatNumbers = request.Outbound.Seats.Select(s => s.SeatNumber.Trim()).ToList();
        var returnSeatNumbers = request.Return.Seats.Select(s => s.SeatNumber.Trim()).ToList();

        var roundTripLockOutcome = await _tripClient.LockRoundTripSeatsAsync(
            request.Outbound.TripId,
            outboundSeatNumbers,
            request.Return.TripId,
            returnSeatNumbers,
            request.PassengerUserId,
            idempotencyKey: request.IdempotencyKey,
            ttlSeconds: SeatLockTtlSeconds,
            cancellationToken: cancellationToken);

        var (outboundLockToken, outboundLockExpiresAt, returnLockToken, returnLockExpiresAt) = roundTripLockOutcome switch
        {
            LockRoundTripSeatsOutcome.Success success => (
                success.Outbound.SeatLockToken,
                success.Outbound.ExpiresAt,
                success.Return.SeatLockToken,
                success.Return.ExpiresAt),
            LockRoundTripSeatsOutcome.SeatUnavailable unavailable => throw new ConflictException(
                "BOOKING_SEAT_UNAVAILABLE",
                "One or more requested seats are unavailable.",
                unavailable.Conflicts
                    .Select(conflict => new ValidationError(conflict.Field, conflict.SeatNumber))
                    .ToArray()),
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
        var paymentDueAt = outboundLockExpiresAt <= returnLockExpiresAt
            ? outboundLockExpiresAt
            : returnLockExpiresAt;

        var bookingGroupId = Guid.NewGuid();
        var outboundPerSeatFare = Money.FromRaw(ResolvePerSeatFare(outboundTrip, request.Outbound.PickupStopId));
        var returnPerSeatFare = Money.FromRaw(ResolvePerSeatFare(returnTrip, request.Return.PickupStopId));
        var outboundBaseFare = Money.FromRaw(outboundPerSeatFare.Amount * outboundSeatNumbers.Count);
        var returnBaseFare = Money.FromRaw(returnPerSeatFare.Amount * returnSeatNumbers.Count);

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
        VoucherFundingType? outboundVoucherFundingType = null;
        VoucherFundingType? returnVoucherFundingType = null;

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
                outboundVoucherFundingType = outboundValidation.FundingType;
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
                returnVoucherFundingType = returnValidation.FundingType;
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
                buyerProfile,
                request.Outbound,
                outboundTrip,
                outboundBaseFare,
                outboundDiscount,
                outboundTotal,
                outboundPerSeatFare,
                bookingGroupId,
                TripDirection.OUTBOUND,
                outboundLockToken,
                now,
                outboundShuttleDistances);

            returnBooking = CreatePendingBooking(
                request.PassengerUserId,
                buyerProfile,
                request.Return,
                returnTrip,
                returnBaseFare,
                returnDiscount,
                returnTotal,
                returnPerSeatFare,
                bookingGroupId,
                TripDirection.RETURN,
                returnLockToken,
                now,
                returnShuttleDistances);

            await _bookings.AddAsync(outboundBooking, cancellationToken);
            await _bookings.AddAsync(returnBooking, cancellationToken);
            await _statusHistory.AddAsync(
                BookingStatusHistory.Create(
                    outboundBooking.Id,
                    BookingStatus.PENDING_PAYMENT,
                    now,
                    BookingStatusHistorySource.CreateRoundTripBooking,
                    request.PassengerUserId),
                cancellationToken);
            await _statusHistory.AddAsync(
                BookingStatusHistory.Create(
                    returnBooking.Id,
                    BookingStatus.PENDING_PAYMENT,
                    now,
                    BookingStatusHistorySource.CreateRoundTripBooking,
                    request.PassengerUserId),
                cancellationToken);

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
        var payment = await ChargeAsync(
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
            outboundVoucherFundingType,
            returnVoucherFundingType,
            paymentDueAt,
            cancellationToken);

        var vehicles = await ResolveVehiclesAsync(
            [request.Outbound.TripId, request.Return.TripId],
            cancellationToken);

        if (string.Equals(request.PaymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult(bookingGroupId, outboundBooking, returnBooking, grandTotal.Amount,
                payment.PaymentId,
                "PENDING_PAYMENT",
                payment.RedirectUrl,
                payment.PaymentReturnMode,
                payment.VnPaySdk,
                vehicles.GetValueOrDefault(request.Outbound.TripId),
                vehicles.GetValueOrDefault(request.Return.TripId));
        }

        await BookConfirmAndPublishAsync(
            outboundBooking,
            request.Outbound.TripId,
            outboundTrip,
            outboundLockToken,
            outboundSeatNumbers,
            outboundVoucherUsageId,
            now,
            cancellationToken);

        await BookConfirmAndPublishAsync(
            returnBooking,
            request.Return.TripId,
            returnTrip,
            returnLockToken,
            returnSeatNumbers,
            returnVoucherUsageId,
            now,
            cancellationToken);

        _logger.LogInformation(
            "Round-trip booking group {BookingGroupId} confirmed with outbound {OutboundBookingId} and return {ReturnBookingId}.",
            bookingGroupId,
            outboundBooking.Id,
            returnBooking.Id);

        return BuildResult(bookingGroupId, outboundBooking, returnBooking, grandTotal.Amount,
            null, "CONFIRMED", null,
            outboundVehicle: vehicles.GetValueOrDefault(request.Outbound.TripId),
            returnVehicle: vehicles.GetValueOrDefault(request.Return.TripId));
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

    private async Task<TripSnapshot> GetScheduledTripAsync(
        Guid tripId,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
    {
        var trip = await _tripClient.GetTripSnapshotAsync(tripId, pricingAt, cancellationToken);
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

        if (!trip.DriverUserId.HasValue)
        {
            throw new BookingUpstreamUnavailableException(
                $"Trip '{tripId}' does not have an assigned driver.");
        }

        return trip;
    }

    private async Task<(int? InboundDistanceMeters, int? OutboundDistanceMeters)> ValidateShuttleRequestAsync(
        CreateRoundTripBookingCommand.RoundTripBookingLegCommand leg,
        TripSnapshot trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (leg.ShuttlePickup is null && leg.ShuttleDropoff is null)
        {
            return (null, null);
        }

        if (leg.ShuttlePickup is not null
            && (string.IsNullOrWhiteSpace(leg.ShuttlePickup.Address)
                || leg.ShuttlePickup.Latitude is < -90m or > 90m
                || leg.ShuttlePickup.Longitude is < -180m or > 180m))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Shuttle pickup address and coordinates are invalid.");
        }

        if (leg.ShuttleDropoff is not null
            && (string.IsNullOrWhiteSpace(leg.ShuttleDropoff.Address)
                || leg.ShuttleDropoff.Latitude is < -90m or > 90m
                || leg.ShuttleDropoff.Longitude is < -180m or > 180m))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Shuttle dropoff address and coordinates are invalid.");
        }

        if (leg.ShuttlePickup is not null
            && (leg.PickupStopId.HasValue || leg.PickupStationId != trip.OriginStation.Id
                || !trip.OriginStation.IsActive
                || !trip.OriginStation.SupportsShuttle
                || !trip.OriginStation.Latitude.HasValue
                || !trip.OriginStation.Longitude.HasValue))
        {
            throw new CodedValidationException(
                "SHUTTLE_STATION_NOT_SUPPORTED",
                "Shuttle is available only at a supported origin Station.");
        }

        if (leg.ShuttleDropoff is not null
            && (leg.DropoffStopId.HasValue || leg.DropoffStationId != trip.DestinationStation.Id
                || !trip.DestinationStation.IsActive
                || !trip.DestinationStation.SupportsShuttle
                || !trip.DestinationStation.Latitude.HasValue
                || !trip.DestinationStation.Longitude.HasValue))
        {
            throw new CodedValidationException(
                "SHUTTLE_STATION_NOT_SUPPORTED",
                "Shuttle is available only at a supported destination Station.");
        }

        if (now >= trip.DepartureDateTime.AddMinutes(-30))
        {
            throw new ConflictException(
                "SHUTTLE_REQUEST_CUTOFF_PASSED",
                "The shuttle request cutoff has passed.");
        }

        var inboundDistance = leg.ShuttlePickup is null
            ? (int?)null
            : await ResolveShuttleDistanceAsync(
                leg.TripId,
                BookingShuttleIntent.InboundDirection,
                leg.ShuttlePickup.Latitude,
                leg.ShuttlePickup.Longitude,
                cancellationToken);
        var outboundDistance = leg.ShuttleDropoff is null
            ? (int?)null
            : await ResolveShuttleDistanceAsync(
                leg.TripId,
                BookingShuttleIntent.OutboundDirection,
                leg.ShuttleDropoff.Latitude,
                leg.ShuttleDropoff.Longitude,
                cancellationToken);
        return (inboundDistance, outboundDistance);
    }

    private async Task<int> ResolveShuttleDistanceAsync(
        Guid tripId,
        string direction,
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken)
    {
        var outcome = await _tripClient.GetShuttleRoadDistanceAsync(
            tripId, direction, latitude, longitude, cancellationToken);
        return ShuttleDistancePolicy.Resolve(outcome);
    }

    private BookingEntity CreatePendingBooking(
        Guid passengerUserId,
        BookingBuyerSnapshotProfile buyerProfile,
        CreateRoundTripBookingCommand.RoundTripBookingLegCommand leg,
        TripSnapshot trip,
        Money baseFare,
        Money discountAmount,
        Money totalAmount,
        Money perSeatFare,
        Guid bookingGroupId,
        TripDirection tripDirection,
        Guid seatLockToken,
        DateTimeOffset now,
        (int? InboundDistanceMeters, int? OutboundDistanceMeters) shuttleDistances)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(now),
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
            seatLockToken: seatLockToken,
            tripCurrentDeparture: trip.DepartureDateTime,
            buyerDisplayName: buyerProfile.DisplayName,
            buyerPhone: buyerProfile.Phone,
            buyerEmail: buyerProfile.Email,
            buyerAvatarUrl: buyerProfile.AvatarUrl);

        var ticketAllocations = BuildTicketAllocations(leg.Seats, perSeatFare, discountAmount, now);
        foreach (var allocation in ticketAllocations)
        {
            booking.AddTicketedPassenger(
                allocation.SeatNumber,
                allocation.TicketCode,
                allocation.FareAmount,
                allocation.DiscountAmount,
                allocation.PaidAmount);
        }

        if (leg.ShuttlePickup is not null)
        {
            booking.RequestShuttle(
                BookingShuttleIntent.InboundDirection,
                leg.ShuttlePickup.Address,
                leg.ShuttlePickup.Latitude,
                leg.ShuttlePickup.Longitude,
                shuttleDistances.InboundDistanceMeters);
        }

        if (leg.ShuttleDropoff is not null)
        {
            booking.RequestShuttle(
                BookingShuttleIntent.OutboundDirection,
                leg.ShuttleDropoff.Address,
                leg.ShuttleDropoff.Latitude,
                leg.ShuttleDropoff.Longitude,
                shuttleDistances.OutboundDistanceMeters);
        }

        return booking;
    }

    private async Task<BookingBuyerSnapshotProfile> GetRequiredBuyerProfileAsync(
        Guid buyerUserId,
        CancellationToken cancellationToken)
    {
        var profiles = await _identityUsers.GetUsersAsync([buyerUserId], cancellationToken);
        return profiles.TryGetValue(buyerUserId, out var profile)
            ? profile
            : throw new BookingUpstreamUnavailableException(
                "Identity did not return the authenticated Booking buyer.");
    }

    private async Task<PaymentChargeInfo> ChargeAsync(
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
        VoucherFundingType? outboundVoucherFundingType,
        VoucherFundingType? returnVoucherFundingType,
        DateTimeOffset paymentDueAt,
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
                        new BatchChargeItem(
                            "BOOKING",
                            outboundBooking.Id,
                            outboundBooking.TotalAmount.Amount,
                            CreateLegPaymentContext(outboundBooking, outboundVoucherFundingType)),
                        new BatchChargeItem(
                            "BOOKING",
                            returnBooking.Id,
                            returnBooking.TotalAmount.Amount,
                            CreateLegPaymentContext(returnBooking, returnVoucherFundingType)),
                    ],
                    idempotencyKey: request.IdempotencyKey,
                    cancellationToken: cancellationToken);

                switch (batchOutcome)
                {
                    case BatchChargeOutcome.Success success:
                        EnsureWalletBatchSucceeded(success, outboundBooking.Id, returnBooking.Id);
                        return new PaymentChargeInfo(null, null);
                    case BatchChargeOutcome.InsufficientFunds insufficientFunds:
                        throw new BookingPaymentException(402, "PAYMENT_INSUFFICIENT_WALLET", insufficientFunds.Message);
                    case BatchChargeOutcome.TransportError transportError:
                        throw new BookingPaymentException(502, "PAYMENT_VNPAY_ERROR", transportError.Message);
                    default:
                        throw new InvalidOperationException("Payment batch charge failed: Unknown payment error.");
                }
            }

            if (paymentDueAt <= _clock.UtcNow)
            {
                throw new BookingPaymentException(
                    422,
                    "PAYMENT_DEADLINE_PASSED",
                    "The round-trip seat-lock payment deadline has passed.");
            }

            var chargeOutcome = await _paymentClient.ChargeAsync(
                referenceType: "BOOKING_GROUP",
                referenceId: bookingGroupId,
                userId: request.PassengerUserId,
                amount: grandTotal.Amount,
                method: request.PaymentMethod,
                idempotencyKey: request.IdempotencyKey,
                context: CreateGroupPaymentContext(
                    outboundBooking,
                    returnBooking,
                    outboundVoucherFundingType,
                    returnVoucherFundingType),
                dueAt: paymentDueAt,
                paymentReturnMode: request.PaymentReturnMode,
                cancellationToken: cancellationToken);

            switch (chargeOutcome)
            {
                case ChargeOutcome.Success success:
                    return new PaymentChargeInfo(
                        success.Data.PaymentId,
                        success.Data.PaymentRedirectUrl,
                        success.Data.PaymentReturnMode,
                        success.Data.VnPaySdk);
                case ChargeOutcome.InsufficientFunds insufficientFunds:
                    // S1 fix: compensate voucher usages before re-throwing (mirrors WALLET path).
                    throw new BookingPaymentException(402, "PAYMENT_INSUFFICIENT_WALLET", insufficientFunds.Message);
                case ChargeOutcome.DeadlinePassed deadlinePassed:
                    throw new BookingPaymentException(422, "PAYMENT_DEADLINE_PASSED", deadlinePassed.Message);
                case ChargeOutcome.TransportError transportError:
                    throw new BookingPaymentException(
                        transportError.StatusCode,
                        transportError.ErrorCode,
                        transportError.Message);
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

    private static PaymentContextSnapshot CreateLegPaymentContext(
        BookingEntity booking,
        VoucherFundingType? fundingType)
        => new(1, [CreateAllocation(booking, fundingType)]);

    private static PaymentContextSnapshot CreateGroupPaymentContext(
        BookingEntity outboundBooking,
        BookingEntity returnBooking,
        VoucherFundingType? outboundFundingType,
        VoucherFundingType? returnFundingType)
        => new(1,
        [
            CreateAllocation(outboundBooking, outboundFundingType),
            CreateAllocation(returnBooking, returnFundingType),
        ]);

    private static PaymentAllocationSnapshot CreateAllocation(
        BookingEntity booking,
        VoucherFundingType? fundingType)
        => new(
            booking.Id,
            "BOOKING",
            booking.OperatorId,
            booking.TripId,
            checked(booking.TotalAmount.Amount + booking.DiscountAmount.Amount),
            fundingType == VoucherFundingType.VIETRIDE_FUNDED ? booking.DiscountAmount.Amount : 0,
            fundingType == VoucherFundingType.OPERATOR_FUNDED ? booking.DiscountAmount.Amount : 0,
            booking.BookingCode.Value);

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
        TripSnapshot trip,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        Guid? voucherUsageId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var passengerAssignments = booking.Passengers
            .Select(p => new PassengerSeatAssignment(
                p.Id,
                p.SeatNumber
                    ?? throw new InvalidOperationException(
                        "A newly created checkout passenger must have a seat number.")))
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

        booking.Confirm(now);
        await _statusHistory.AddAsync(
            BookingStatusHistory.Create(
                booking.Id,
                BookingStatus.CONFIRMED,
                now,
                BookingStatusHistorySource.CreateRoundTripBooking,
                booking.PassengerUserId),
            cancellationToken);

        // voucherUsageId propagated in event payload (BSOT:1741 optional field).
        var confirmedEvent = new
        {
            bookingId = booking.Id,
            bookingCode = booking.BookingCode.Value,
            tripId = booking.TripId,
            totalAmount = booking.TotalAmount.Amount,
            userId = booking.PassengerUserId,
            voucherUsageId,
            tickets = booking.Tickets.Select(ticket => new
            {
                ticketId = ticket.Id,
                passengerUserId = booking.PassengerUserId,
            }).ToArray(),
            ticketCodes = booking.Tickets.Select(ticket => ticket.TicketCode.Value).ToArray(),
            ticketCount = booking.Tickets.Count,
            shuttlePickup = booking.ShuttleIntent is null ? null : new
            {
                address = booking.ShuttleIntent.PickupAddress,
                latitude = booking.ShuttleIntent.PickupLatitude,
                longitude = booking.ShuttleIntent.PickupLongitude,
            },
            shuttleRequests = booking.ShuttleIntents
                .Where(intent => intent.IsActive)
                .Select(intent => new
                {
                    direction = intent.Direction,
                    address = intent.PickupAddress,
                    latitude = intent.PickupLatitude,
                    longitude = intent.PickupLongitude,
                    roadDistanceMeters = intent.RoadDistanceMeters,
                })
                .ToArray(),
        };

        await _outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(confirmedEvent, JsonOptions),
            cancellationToken);

        var createdEvent = new BookingCreatedIntegrationEvent(
            booking.Id,
            booking.BookingCode.Value,
            booking.TripId,
            booking.Tickets.Select(ticket => ticket.TicketCode.Value).ToArray(),
            booking.Passengers.Select(passenger => passenger.SeatNumber).OfType<string>().ToArray(),
            trip.DepartureDateTime,
            new BookingLocationSnapshot(booking.PickupStationId, booking.PickupStopId, null),
            new BookingLocationSnapshot(booking.DropoffStationId, booking.DropoffStopId, null),
            trip.DriverUserId,
            trip.AssistantUserId,
            now);
        await _outbox.EnqueueAsync(
            createdEvent.EventType,
            JsonSerializer.Serialize(createdEvent, JsonOptions),
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

    private async Task<IReadOnlyDictionary<Guid, BookingHistoryVehicleDto>> ResolveVehiclesAsync(
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var summaries = await _tripClient.GetHistoryVehicleSummariesAsync(
                tripIds,
                cancellationToken) ?? [];
            return summaries
                .Select(summary => (
                    summary.TripId,
                    Vehicle: BookingHistoryVehicleMapping.FromSummary(summary)))
                .Where(item =>
                    item.Vehicle is not null
                    && tripIds.Contains(item.TripId))
                .GroupBy(item => item.TripId)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single().Vehicle!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new Dictionary<Guid, BookingHistoryVehicleDto>();
        }
    }

    private static CreateRoundTripBookingResult BuildResult(
        Guid bookingGroupId,
        BookingEntity outboundBooking,
        BookingEntity returnBooking,
        long grandTotal,
        Guid? paymentId,
        string status,
        string? paymentRedirectUrl,
        string? paymentReturnMode = null,
        VnPaySdkMetadata? vnPaySdk = null,
        BookingHistoryVehicleDto? outboundVehicle = null,
        BookingHistoryVehicleDto? returnVehicle = null)
        => new(
            bookingGroupId,
            new CreateRoundTripBookingResult.RoundTripBookingResult(
                outboundBooking.Id,
                outboundBooking.BookingCode.Value,
                outboundBooking.TotalAmount.Amount,
                outboundBooking.DiscountAmount.Amount,
                ToTicketResults(outboundBooking),
                outboundVehicle),
            new CreateRoundTripBookingResult.RoundTripBookingResult(
                returnBooking.Id,
                returnBooking.BookingCode.Value,
                returnBooking.TotalAmount.Amount,
                returnBooking.DiscountAmount.Amount,
                ToTicketResults(returnBooking),
                returnVehicle),
            grandTotal,
            paymentId,
            status,
            paymentRedirectUrl,
            paymentReturnMode,
            vnPaySdk);

    private sealed record PaymentChargeInfo(
        Guid? PaymentId,
        string? RedirectUrl,
        string? PaymentReturnMode = null,
        VnPaySdkMetadata? VnPaySdk = null);

    private static void GuardMobileReturnMode(string paymentMethod, string? paymentReturnMode)
    {
        if (!string.Equals(paymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(paymentReturnMode))
        {
            throw new BookingPaymentException(
                426,
                "MOBILE_APP_UPDATE_REQUIRED",
                "Update the mobile app to continue with VNPay.");
        }

        if (!string.Equals(paymentReturnMode, "MOBILE_SDK", StringComparison.OrdinalIgnoreCase))
        {
            throw new BookingPaymentException(
                422,
                "PAYMENT_RETURN_MODE_INVALID",
                "paymentReturnMode must be MOBILE_SDK.");
        }
    }

    private static IReadOnlyList<TicketAllocation> BuildTicketAllocations(
        IReadOnlyList<CreateRoundTripBookingCommand.RoundTripSeatRequest> seats,
        Money perSeatFare,
        Money totalDiscount,
        DateTimeOffset now)
    {
        var orderedSeats = seats
            .Select(seat => seat.SeatNumber.Trim())
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var count = orderedSeats.Length;
        var baseDiscount = count == 0 ? 0 : totalDiscount.Amount / count;
        var remainder = count == 0 ? 0 : totalDiscount.Amount % count;

        return orderedSeats
            .Select((seatNumber, index) =>
            {
                var discount = Money.FromRaw(baseDiscount + (index < remainder ? 1 : 0));
                return new TicketAllocation(
                    seatNumber,
                    TicketCode.Generate(now),
                    perSeatFare,
                    discount,
                    perSeatFare - discount);
            })
            .ToArray();
    }

    private static void ValidateStopSelections(TripSnapshot trip, Guid? pickupStopId, Guid? dropoffStopId)
    {
        var pickup = pickupStopId.HasValue
            ? trip.Stops.FirstOrDefault(stop => stop.StopId == pickupStopId.Value && stop.IsActive)
            : null;
        var dropoff = dropoffStopId.HasValue
            ? trip.Stops.FirstOrDefault(stop => stop.StopId == dropoffStopId.Value && stop.IsActive)
            : null;

        if (pickupStopId.HasValue && pickup is null || dropoffStopId.HasValue && dropoff is null)
        {
            throw new CodedValidationException("STOP_NOT_FOUND", "Selected stop was not found or is inactive.");
        }

        if (pickup is not null && !pickup.AllowPickup)
        {
            throw new CodedValidationException("STOP_NOT_PICKUP_ALLOWED", "The selected stop does not allow pickup.");
        }

        if (dropoff is not null && !dropoff.AllowDropoff)
        {
            throw new CodedValidationException("STOP_NOT_DROPOFF_ALLOWED", "The selected stop does not allow dropoff.");
        }

        if (pickup is not null && dropoff is not null && dropoff.OrderIndex <= pickup.OrderIndex)
        {
            throw new CodedValidationException("STOP_NOT_DROPOFF_ALLOWED", "Dropoff stop must be after pickup stop.");
        }
    }

    private static long ResolvePerSeatFare(TripSnapshot trip, Guid? pickupStopId)
    {
        if (!pickupStopId.HasValue)
            return trip.BaseFare;

        return trip.Stops.First(stop => stop.StopId == pickupStopId.Value).FareFromThisStop
            ?? trip.BaseFare;
    }

    private static IReadOnlyList<CreateRoundTripBookingResult.RoundTripTicketResult> ToTicketResults(
        BookingEntity booking)
        => booking.Tickets
            .OrderBy(ticket => ticket.SeatNumber, StringComparer.OrdinalIgnoreCase)
            .Select(ticket => new CreateRoundTripBookingResult.RoundTripTicketResult(
                ticket.Id,
                ticket.TicketCode.Value,
                ticket.SeatNumber,
                ticket.Status.ToString(),
                ticket.FareAmount.Amount,
                ticket.DiscountAmount.Amount,
                ticket.PaidAmount.Amount))
            .ToArray();

    private sealed record TicketAllocation(
        string SeatNumber,
        TicketCode TicketCode,
        Money FareAmount,
        Money DiscountAmount,
        Money PaidAmount);
}
