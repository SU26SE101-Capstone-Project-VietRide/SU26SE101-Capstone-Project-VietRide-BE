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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;
    private readonly ITripServiceClient _tripClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IBookingService _bookingService;
    private readonly IVoucherService _voucherService;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IBookingStationCanonicalizer _stationCanonicalizer;
    private readonly IIdentityUserServiceClient _identityUsers;
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
        ILogger<CreateBookingCommandHandler> logger,
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
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
        _stationCanonicalizer = stationCanonicalizer;
        _identityUsers = identityUsers;
    }

    public async Task<CreateBookingResult> Handle(
        CreateBookingCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        GuardMobileReturnMode(request.PaymentMethod, request.PaymentReturnMode);

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
        var trip = await _tripClient.GetTripSnapshotAsync(request.TripId, now, cancellationToken);
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

        if (!trip.DriverUserId.HasValue)
        {
            throw new BookingUpstreamUnavailableException(
                $"Trip '{request.TripId}' does not have an assigned driver.");
        }

        var stationCanonicalization = await _stationCanonicalizer.LockAndResolveAsync(
            BookingStationCanonicalization.Collect(
                request.PickupStationId,
                request.DropoffStationId,
                trip.OriginStation.Id,
                trip.DestinationStation.Id),
            cancellationToken);
        request = request with
        {
            PickupStationId = stationCanonicalization.Resolve(request.PickupStationId),
            DropoffStationId = stationCanonicalization.Resolve(request.DropoffStationId),
        };
        trip = BookingStationCanonicalization.ResolveTrip(trip, stationCanonicalization);

        ValidateStopSelections(trip, request.PickupStopId, request.DropoffStopId);
        var shuttleDistances = await ValidateShuttleRequestAsync(request, trip, now, cancellationToken);
        var buyerProfile = await GetRequiredBuyerProfileAsync(
            request.PassengerUserId,
            cancellationToken);

        // -----------------------------------------------------------------------
        // 3. Lock seats (all-or-nothing)
        // -----------------------------------------------------------------------
        var seatNumbers = request.Seats.Select(s => s.SeatNumber.Trim()).ToList();
        var lockIdempotencyKey = request.IdempotencyKey ?? request.PassengerUserId.ToString("D");

        var lockOutcome = await _tripClient.LockSeatsAsync(
            request.TripId,
            seatNumbers,
            holdOwnerId: request.PassengerUserId,
            idempotencyKey: lockIdempotencyKey,
            ttlSeconds: SeatLockTtlSeconds,
            cancellationToken: cancellationToken);

        // Map lock outcome to typed exceptions — no booking created on lock failure
        Guid seatLockToken;
        DateTimeOffset seatLockExpiresAt;
        switch (lockOutcome)
        {
            case LockSeatsOutcome.Success success:
                seatLockToken = success.Data.SeatLockToken;
                seatLockExpiresAt = success.Data.ExpiresAt;
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
        var seatCount = request.Seats.Count;
        var perSeatFare = Money.FromRaw(ResolvePerSeatFare(trip, request.PickupStopId));
        var baseFare = Money.FromRaw(perSeatFare.Amount * seatCount);

        Money discountAmount = Money.Zero;
        Guid? validatedVoucherId = null;
        VoucherFundingType? voucherFundingType = null;

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
            voucherFundingType = validation.FundingType;
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
                tripSnapshotRouteName: null,
                seatLockToken: seatLockToken,
                tripCurrentDeparture: trip.DepartureDateTime,
                buyerDisplayName: buyerProfile.DisplayName,
                buyerPhone: buyerProfile.Phone,
                buyerEmail: buyerProfile.Email,
                buyerAvatarUrl: buyerProfile.AvatarUrl);

            // Add passenger rows (operational-only — no PII stored)
            var ticketAllocations = BuildTicketAllocations(request.Seats, perSeatFare, discountAmount, now);
            foreach (var allocation in ticketAllocations)
            {
                booking.AddTicketedPassenger(
                    allocation.SeatNumber,
                    allocation.TicketCode,
                    allocation.FareAmount,
                    allocation.DiscountAmount,
                    allocation.PaidAmount);
            }


            if (request.ShuttlePickup is not null)
            {
                booking.RequestShuttle(
                    BookingShuttleIntent.InboundDirection,
                    request.ShuttlePickup.Address,
                    request.ShuttlePickup.Latitude,
                    request.ShuttlePickup.Longitude,
                    shuttleDistances.InboundDistanceMeters);
            }

            if (request.ShuttleDropoff is not null)
            {
                booking.RequestShuttle(
                    BookingShuttleIntent.OutboundDirection,
                    request.ShuttleDropoff.Address,
                    request.ShuttleDropoff.Latitude,
                    request.ShuttleDropoff.Longitude,
                    shuttleDistances.OutboundDistanceMeters);
            }

            await _bookings.AddAsync(booking, cancellationToken);
            await _statusHistory.AddAsync(
                BookingStatusHistory.Create(
                    booking.Id,
                    BookingStatus.PENDING_PAYMENT,
                    now,
                    BookingStatusHistorySource.CreateBooking,
                    request.PassengerUserId),
                cancellationToken);

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
        var chargeIdempotencyKey = request.IdempotencyKey ?? booking.Id.ToString("D");

        if (string.Equals(request.PaymentMethod, "VNPAY", StringComparison.OrdinalIgnoreCase)
            && seatLockExpiresAt <= _clock.UtcNow)
        {
            await CompensateSeatsAndVoucherAsync(
                request.TripId, seatLockToken, seatNumbers, booking.Id, voucherUsageId, cancellationToken);
            throw new BookingPaymentException(
                422,
                "PAYMENT_DEADLINE_PASSED",
                "The seat-lock payment deadline has passed.");
        }

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
                context: CreatePaymentContext(booking, baseFare.Amount, discountAmount.Amount, voucherFundingType),
                dueAt: seatLockExpiresAt,
                paymentReturnMode: request.PaymentReturnMode,
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
            throw new BookingPaymentException(402, "PAYMENT_INSUFFICIENT_WALLET", insuf.Message);
        }

        if (chargeOutcome is ChargeOutcome.DeadlinePassed deadlinePassed)
        {
            await CompensateSeatsAndVoucherAsync(
                request.TripId, seatLockToken, seatNumbers, booking.Id, voucherUsageId, cancellationToken);
            throw new BookingPaymentException(
                422,
                "PAYMENT_DEADLINE_PASSED",
                deadlinePassed.Message);
        }

        if (chargeOutcome is ChargeOutcome.TransportError transportError)
        {
            await CompensateSeatsAndVoucherAsync(
                request.TripId, seatLockToken, seatNumbers, booking.Id, voucherUsageId, cancellationToken);
            // TODO Day 15/16: map ChargeOutcome.TransportError to a registered BSOT §5.9
            // error code (e.g. PAYMENT_SERVICE_UNAVAILABLE) instead of surfacing 500 INTERNAL_ERROR.
            throw new BookingPaymentException(
                transportError.StatusCode,
                transportError.ErrorCode,
                transportError.Message);
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
                PaymentId: chargeSuccess.Data.PaymentId,
                PaymentRedirectUrl: paymentRedirectUrl,
                Tickets: ToTicketResults(booking),
                PaymentReturnMode: chargeSuccess.Data.PaymentReturnMode,
                VnPaySdk: chargeSuccess.Data.VnPaySdk,
                Vehicle: await ResolveVehicleAsync(request.TripId, cancellationToken));
        }

        // -----------------------------------------------------------------------
        // 7. WALLET path — book-seats → Confirm → enqueue confirmed event
        //    (all in the same TransactionBehavior transaction)
        // -----------------------------------------------------------------------
        var passengerAssignments = booking.Passengers
            .Select(p => new PassengerSeatAssignment(
                p.Id,
                p.SeatNumber
                    ?? throw new InvalidOperationException(
                        "A newly created checkout passenger must have a seat number.")))
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
        await _statusHistory.AddAsync(
            BookingStatusHistory.Create(
                booking.Id,
                BookingStatus.CONFIRMED,
                now,
                BookingStatusHistorySource.CreateBooking,
                request.PassengerUserId),
            cancellationToken);

        // Enqueue booking.booking.confirmed (same tx — outbox committed by TransactionBehavior).
        // voucherUsageId propagated in event payload (BSOT:1741 optional field, replaces hardcoded null).
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
            PaymentId: chargeSuccess.Data.PaymentId,
            PaymentRedirectUrl: paymentRedirectUrl,
            Tickets: ToTicketResults(booking),
            Vehicle: await ResolveVehicleAsync(request.TripId, cancellationToken));
    }

    private async Task<BookingHistoryVehicleDto?> ResolveVehicleAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        try
        {
            var summaries = await _tripClient.GetHistoryVehicleSummariesAsync(
                [tripId],
                cancellationToken) ?? [];
            var summary = summaries.SingleOrDefault(item =>
                item.TripId == tripId && !string.IsNullOrWhiteSpace(item.LicensePlate));
            return BookingHistoryVehicleMapping.FromSummary(summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static PaymentContextSnapshot CreatePaymentContext(
        BookingEntity booking,
        long grossAmount,
        long discountAmount,
        VoucherFundingType? fundingType)
        => new(1,
        [
            new PaymentAllocationSnapshot(
                booking.Id,
                "BOOKING",
                booking.OperatorId,
                booking.TripId,
                grossAmount,
                fundingType == VoucherFundingType.VIETRIDE_FUNDED ? discountAmount : 0,
                fundingType == VoucherFundingType.OPERATOR_FUNDED ? discountAmount : 0,
                booking.BookingCode.Value),
        ]);

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

    private static IReadOnlyList<TicketAllocation> BuildTicketAllocations(
        IReadOnlyList<SeatRequest> seats,
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
        if (pickupStopId.HasValue && pickup is null)
        {
            throw new CodedValidationException("STOP_NOT_FOUND", "Pickup stop was not found or is inactive.");
        }

        if (pickup is not null && !pickup.AllowPickup)
        {
            throw new CodedValidationException("STOP_NOT_PICKUP_ALLOWED", "The selected stop does not allow pickup.");
        }

        var dropoff = dropoffStopId.HasValue
            ? trip.Stops.FirstOrDefault(stop => stop.StopId == dropoffStopId.Value && stop.IsActive)
            : null;
        if (dropoffStopId.HasValue && dropoff is null)
        {
            throw new CodedValidationException("STOP_NOT_FOUND", "Dropoff stop was not found or is inactive.");
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

    private async Task<(int? InboundDistanceMeters, int? OutboundDistanceMeters)> ValidateShuttleRequestAsync(
        CreateBookingCommand request,
        TripSnapshot trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.ShuttlePickup is null && request.ShuttleDropoff is null)
        {
            return (null, null);
        }

        if (request.ShuttlePickup is not null
            && (string.IsNullOrWhiteSpace(request.ShuttlePickup.Address)
                || request.ShuttlePickup.Latitude is < -90m or > 90m
                || request.ShuttlePickup.Longitude is < -180m or > 180m))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle pickup address and coordinates are invalid.");
        }
        if (request.ShuttleDropoff is not null
            && (string.IsNullOrWhiteSpace(request.ShuttleDropoff.Address)
                || request.ShuttleDropoff.Latitude is < -90m or > 90m
                || request.ShuttleDropoff.Longitude is < -180m or > 180m))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle dropoff address and coordinates are invalid.");
        }

        if (request.ShuttlePickup is not null
            && (request.PickupStopId.HasValue || request.PickupStationId != trip.OriginStation.Id))
        {
            throw new CodedValidationException(
                "SHUTTLE_STATION_NOT_SUPPORTED",
                "Shuttle is available only for pickup at the trip origin Station.");
        }

        if (request.ShuttleDropoff is not null
            && (request.DropoffStopId.HasValue || request.DropoffStationId != trip.DestinationStation.Id))
        {
            throw new CodedValidationException(
                "SHUTTLE_STATION_NOT_SUPPORTED",
                "Shuttle is available only for dropoff at the trip destination Station.");
        }

        if (request.ShuttlePickup is not null
            && (!trip.OriginStation.IsActive
                || !trip.OriginStation.SupportsShuttle
                || !trip.OriginStation.Latitude.HasValue
                || !trip.OriginStation.Longitude.HasValue))
        {
            throw new CodedValidationException(
                "SHUTTLE_STATION_NOT_SUPPORTED",
                "The trip origin Station does not support shuttle service.");
        }

        if (request.ShuttleDropoff is not null
            && (!trip.DestinationStation.IsActive
                || !trip.DestinationStation.SupportsShuttle
                || !trip.DestinationStation.Latitude.HasValue
                || !trip.DestinationStation.Longitude.HasValue))
        {
            throw new CodedValidationException(
                "SHUTTLE_STATION_NOT_SUPPORTED",
                "The trip destination Station does not support shuttle service.");
        }

        if (now >= trip.DepartureDateTime.AddMinutes(-30))
        {
            throw new ConflictException(
                "SHUTTLE_REQUEST_CUTOFF_PASSED",
                "The shuttle request cutoff has passed.");
        }

        var inboundDistance = request.ShuttlePickup is null
            ? (int?)null
            : await ResolveShuttleDistanceAsync(
                request.TripId,
                BookingShuttleIntent.InboundDirection,
                request.ShuttlePickup.Latitude,
                request.ShuttlePickup.Longitude,
                cancellationToken);
        var outboundDistance = request.ShuttleDropoff is null
            ? (int?)null
            : await ResolveShuttleDistanceAsync(
                request.TripId,
                BookingShuttleIntent.OutboundDirection,
                request.ShuttleDropoff.Latitude,
                request.ShuttleDropoff.Longitude,
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

    private static IReadOnlyList<CreateBookingTicketResult> ToTicketResults(BookingEntity booking)
        => booking.Tickets
            .OrderBy(ticket => ticket.SeatNumber, StringComparer.OrdinalIgnoreCase)
            .Select(ticket => new CreateBookingTicketResult(
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
