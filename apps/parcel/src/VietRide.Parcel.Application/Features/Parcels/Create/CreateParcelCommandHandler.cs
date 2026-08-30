using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Security;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Quotes;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Domain.Helpers;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.Create;

public sealed class CreateParcelCommandHandler
    : IRequestHandler<CreateParcelCommand, CreateParcelResponse>
{
    private readonly IIdentityServiceClient _identityClient;
    private readonly IBookingServiceClient _bookingClient;
    private readonly ITripServiceClient _tripClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IParcelRepository _parcelRepository;
    private readonly ParcelQuoteService _quoteService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly ILogger<CreateParcelCommandHandler> _logger;
    private readonly IClock _clock;
    private readonly IParcelReliabilityRepository? _reliability;

    public CreateParcelCommandHandler(
        IIdentityServiceClient identityClient,
        IBookingServiceClient bookingClient,
        ITripServiceClient tripClient,
        IPaymentServiceClient paymentClient,
        IParcelRepository parcelRepository,
        IParcelRouteFareRepository fareRepository,
        IParcelPricingPolicyRepository? policyRepository,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        ILogger<CreateParcelCommandHandler> logger,
        IClock clock,
        IParcelQuoteTokenService? quoteTokenService = null,
        IParcelReliabilityRepository? reliability = null)
    {
        _identityClient = identityClient;
        _bookingClient = bookingClient;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
        _parcelRepository = parcelRepository;
        _quoteService = new ParcelQuoteService(fareRepository, policyRepository, quoteTokenService);
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _logger = logger;
        _clock = clock;
        _reliability = reliability;
    }

    public CreateParcelCommandHandler(
        IIdentityServiceClient identityClient,
        IBookingServiceClient bookingClient,
        ITripServiceClient tripClient,
        IPaymentServiceClient paymentClient,
        IParcelRepository parcelRepository,
        IParcelRouteFareRepository fareRepository,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        ILogger<CreateParcelCommandHandler> logger)
        : this(
            identityClient,
            bookingClient,
            tripClient,
            paymentClient,
            parcelRepository,
            fareRepository,
            policyRepository: null,
            unitOfWork,
            outbox,
            statsRepository,
            logger,
            new SystemClock(),
            null)
    {
    }

    public async Task<CreateParcelResponse> Handle(
        CreateParcelCommand command,
        CancellationToken cancellationToken)
    {
        var userOutcome = await _identityClient.GetUserInfoAsync(command.SenderUserId, cancellationToken);
        switch (userOutcome.Kind)
        {
            case UserLookupOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException(
                    "UPSTREAM_UNAVAILABLE",
                    userOutcome.ErrorMessage ?? "Identity service unavailable.");
            case UserLookupOutcomeKind.UserNotFound:
                throw new CodedNotFoundException(
                    "USER_NOT_FOUND",
                    $"User with id '{command.SenderUserId}' not found.");
            case UserLookupOutcomeKind.Forbidden:
                throw new ForbiddenException(
                    "USER_FORBIDDEN",
                    userOutcome.ErrorMessage ?? "User lookup not permitted.");
        }

        var user = userOutcome.UserInfo!;
        if (user.Role != "PASSENGER")
            throw new ForbiddenException("USER_NOT_PASSENGER", "Only passengers can create parcels.");
        if (user.Status != "ACTIVE")
            throw new ForbiddenException("USER_INACTIVE", "User account is not active.");

        var normalizedRecipientEmail = string.IsNullOrWhiteSpace(command.RecipientEmail)
            ? null
            : command.RecipientEmail.Trim().ToLowerInvariant();
        Guid? recipientUserId = null;
        if (normalizedRecipientEmail is not null)
        {
            var recipientOutcome = await _identityClient.FindUserByEmailAsync(
                normalizedRecipientEmail,
                cancellationToken);
            if (recipientOutcome is null || recipientOutcome.Kind == RecipientUserLookupOutcomeKind.TransportError)
            {
                throw new ParcelDependencyUnavailableException(
                    "UPSTREAM_UNAVAILABLE",
                    recipientOutcome?.ErrorMessage ?? "Identity recipient lookup returned an invalid response.");
            }

            if (recipientOutcome.Kind == RecipientUserLookupOutcomeKind.Success)
            {
                if (!recipientOutcome.UserId.HasValue || recipientOutcome.UserId.Value == Guid.Empty)
                {
                    throw new ParcelDependencyUnavailableException(
                        "UPSTREAM_UNAVAILABLE",
                        "Identity recipient lookup returned an invalid user id.");
                }

                recipientUserId = recipientOutcome.UserId.Value;
            }
        }

        if (command.BookingId.HasValue)
        {
            var bookingOutcome = await _bookingClient.GetBookingSnapshotAsync(command.BookingId.Value, cancellationToken);
            switch (bookingOutcome.Kind)
            {
                case BookingLookupOutcomeKind.TransportError:
                    throw new ParcelDependencyUnavailableException(
                        "BOOKING_SERVICE_UNAVAILABLE",
                        bookingOutcome.ErrorMessage ?? "Booking service unavailable.");
                case BookingLookupOutcomeKind.BookingNotFound:
                    throw new CodedNotFoundException(
                        "BOOKING_NOT_FOUND",
                        $"Booking with id '{command.BookingId}' not found.");
            }

            var booking = bookingOutcome.Snapshot!;
            if (booking.UserId != command.SenderUserId)
                throw new ForbiddenException(
                    "BOOKING_NOT_OWNED_BY_SENDER",
                    "Booking does not belong to the sender.");
            if (booking.TripId != command.TripId)
                throw new CodedConflictException(
                    "BOOKING_NOT_FOR_THIS_TRIP",
                    "Booking is not associated with the specified trip.");
            if (booking.Status != "CONFIRMED")
                throw new CodedConflictException(
                    "BOOKING_NOT_ATTACHABLE",
                    $"Booking is in status '{booking.Status}' and cannot be attached to a parcel.");
            if (booking.ActiveTicketCount <= 0)
                throw new CodedConflictException(
                    "BOOKING_NOT_ATTACHABLE",
                    "Booking has no active ticket that can be attached to a parcel.");
        }

        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(command.TripId, cancellationToken);
        switch (tripOutcome.Kind)
        {
            case TripSnapshotOutcomeKind.TripNotFound:
                throw new CodedNotFoundException(
                    "TRIP_NOT_FOUND",
                    $"Trip with id '{command.TripId}' not found.");
            case TripSnapshotOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    tripOutcome.ErrorMessage ?? "Trip service unavailable.");
        }

        var trip = tripOutcome.Snapshot!;
        if (trip.Status != "SCHEDULED")
            throw new CodedConflictException(
                "TRIP_NOT_ACCEPTING_PARCEL",
                $"Trip '{command.TripId}' is in status '{trip.Status}' and is not accepting parcels.");
        if (!trip.AssistantUserId.HasValue)
            throw new CodedConflictException(
                "PARCEL_ASSISTANT_REQUIRED",
                "The Trip must have an assigned Assistant before it can accept Parcels.");

        var subscriptionEligibility = await _identityClient.GetSubscriptionWriteEligibilityAsync(
            trip.OperatorId,
            requireParcelModule: true,
            cancellationToken) ?? SubscriptionWriteEligibilityOutcome.Allowed();
        if (!subscriptionEligibility.IsAllowed)
        {
            throw new SubscriptionWriteBlockedException(
                subscriptionEligibility.FailureStatusCode ?? 503,
                subscriptionEligibility.ErrorCode ?? "UPSTREAM_UNAVAILABLE",
                subscriptionEligibility.ErrorMessage ?? "Operator subscription cannot create parcels.");
        }

        if (command.DropoffStopId.HasValue)
        {
            var dropoffStop = trip.Stops.FirstOrDefault(stop => stop.StopId == command.DropoffStopId.Value);
            if (dropoffStop is null)
                throw new CodedValidationException(
                    "DROP_OFF_STOP_NOT_FOUND",
                    $"Drop-off stop '{command.DropoffStopId}' not found in trip '{command.TripId}'.");

            if (!dropoffStop.AllowDropoff)
                throw new CodedValidationException(
                    "DROP_OFF_STOP_NOT_ALLOWED",
                    $"Drop-off stop '{command.DropoffStopId}' does not allow drop-off.");
        }

        var parcelCode = await GenerateParcelCodeAsync(cancellationToken);

        var finalDescription = command.ItemName is not null
            ? command.Description is not null
                ? $"{command.ItemName}\n{command.Description}"
                : command.ItemName
            : command.Description;

        if (!Enum.TryParse<ParcelDeliveryMethod>(command.DeliveryMethod, ignoreCase: true, out var deliveryMethod))
            throw new CodedValidationException(
                "INVALID_DELIVERY_METHOD",
                $"'{command.DeliveryMethod}' is not a valid ParcelDeliveryMethod.");

        var recipientPhone = PhoneNumber.Normalize(command.RecipientPhone);

        var now = _clock.UtcNow;
        if (!string.IsNullOrWhiteSpace(command.QuoteToken))
        {
            if (!Enum.TryParse<ParcelSizeCategory>(command.SizeCategory, ignoreCase: true, out var requestedSizeCategory)
                || !Enum.IsDefined(requestedSizeCategory))
            {
                throw new CodedValidationException(
                    "INVALID_SIZE_CATEGORY",
                    $"'{command.SizeCategory}' is not a valid ParcelSizeCategory.");
            }

            await _quoteService.ValidateTokenAsync(
                command.QuoteToken,
                new ParcelQuoteTokenExpectation(
                    command.SenderUserId,
                    command.TripId,
                    trip.RouteId,
                    trip.OperatorId,
                    trip.OriginStation.Id,
                    trip.DestinationStation.Id,
                    command.LengthCm,
                    command.WidthCm,
                    command.HeightCm,
                    command.EstimatedWeightKg,
                    requestedSizeCategory),
                now,
                cancellationToken);
        }

        var dimFactor = await _quoteService.GetDimWeightFactorAsync(now, cancellationToken);
        var cargoEstimate = ParcelCargoCalculator.Calculate(
            command.LengthCm,
            command.WidthCm,
            command.HeightCm,
            command.EstimatedWeightKg,
            dimFactor);
        var sizeCategory = ParcelCargoCalculator.DeriveSizeCategory(cargoEstimate.ChargeableWeightKg);
        var deadlines = ParcelCargoCalculator.CalculateSettlementDeadlines(trip.DepartureDateTime);
        if (now >= deadlines.LatestCheckInAt)
            throw new CodedConflictException(
                "PARCEL_CHECK_IN_CLOSED",
                "The trip no longer has enough time for parcel check-in and final settlement.");

        var fare = await _quoteService.FindActiveFareAsync(
            trip.RouteId,
            sizeCategory,
            now,
            cancellationToken);
        if (fare is null)
            throw new CodedValidationException(
                "FARE_NOT_CONFIGURED",
                $"No fare configured for route '{trip.RouteId}' and size category '{sizeCategory}'.");

        var baseQuote = _quoteService.Calculate(cargoEstimate, fare, 0, dimFactor);
        var estimatedGrossPrice = Money.FromRaw(baseQuote.EstimatedGrossPriceVnd);
        var discountAmount = Money.Zero;

        if (!string.IsNullOrWhiteSpace(command.VoucherCode))
        {
            var voucherOutcome = await _bookingClient.ValidateVoucherAsync(
                command.VoucherCode,
                trip.OperatorId,
                trip.RouteId,
                command.SenderUserId,
                estimatedGrossPrice.Amount,
                command.PaymentMethod,
                cancellationToken);

            if (voucherOutcome.Kind == VoucherValidationOutcomeKind.TransportError)
                throw new ParcelDependencyUnavailableException(
                    "BOOKING_SERVICE_UNAVAILABLE",
                    voucherOutcome.ErrorMessage ?? "Booking service unavailable.");

            if (voucherOutcome.Kind == VoucherValidationOutcomeKind.Invalid || !voucherOutcome.VoucherId.HasValue)
                throw new CodedValidationException(
                    "VOUCHER_NOT_APPLICABLE",
                    voucherOutcome.ErrorMessage ?? "Voucher is not applicable to this parcel.");

            discountAmount = Money.FromRaw(voucherOutcome.DiscountAmount);
        }

        var quote = _quoteService.Calculate(cargoEstimate, fare, discountAmount.Amount, dimFactor);
        estimatedGrossPrice = Money.FromRaw(quote.EstimatedGrossPriceVnd);
        discountAmount = Money.FromRaw(quote.EstimatedDiscountVnd);
        var estimatedTotalPrice = Money.FromRaw(quote.EstimatedTotalPriceVnd);
        var depositPercent = quote.DepositPercent;
        var depositRequired = Money.FromRaw(quote.EstimatedDepositVnd);

        var summaryOutcome = await _tripClient.GetTripSummariesAsync(
            [command.TripId],
            cancellationToken);
        if (summaryOutcome.Kind != TripSummaryBatchOutcomeKind.Success)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                summaryOutcome.ErrorMessage ?? "Trip summary is unavailable.");
        }

        var matchingTripSummaries = summaryOutcome.Summaries
            .Where(summary => summary.TripId == command.TripId)
            .Take(2)
            .ToArray();
        if (matchingTripSummaries.Length != 1
            || matchingTripSummaries[0].Route.RouteId != trip.RouteId
            || matchingTripSummaries[0].Vehicle.VehicleId != trip.VehicleId)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                "Trip summary is missing or inconsistent with the validated trip snapshot.");
        }

        var tripSummary = matchingTripSummaries[0];

        var parcel = ParcelEntity.CreatePendingPayment(
            parcelCode,
            command.SenderUserId,
            recipientUserId,
            command.RecipientName,
            recipientPhone,
            normalizedRecipientEmail,
            trip.OperatorId,
            command.TripId,
            command.DropoffStopId,
            command.BookingId,
            finalDescription,
            command.PhotoUrl,
            sizeCategory,
            cargoEstimate.LengthCm,
            cargoEstimate.WidthCm,
            cargoEstimate.HeightCm,
            cargoEstimate.WeightKg,
            cargoEstimate.VolumeM3,
            cargoEstimate.DimWeightKg,
            cargoEstimate.ChargeableWeightKg,
            deliveryMethod,
            estimatedTotalPrice,
            depositPercent,
            depositRequired,
            depositRequired,
            discountAmount,
            command.VoucherCode,
            null);

        parcel.CaptureTripDisplaySnapshot(
            tripSummary.Route.RouteId,
            tripSummary.Route.Name,
            tripSummary.Route.OriginName,
            tripSummary.Route.DestinationName,
            tripSummary.Vehicle.VehicleId,
            tripSummary.Vehicle.LicensePlate);

        parcel.ConfigureSettlementV2(
            sizeCategory,
            estimatedGrossPrice,
            discountAmount,
            estimatedTotalPrice,
            depositPercent,
            depositRequired,
            fare.PricePerChargeableKgVnd.Amount > 0
                ? fare.PricePerChargeableKgVnd
                : fare.PriceVnd,
            fare.MinimumPriceVnd,
            dimFactor,
            deadlines.LoadCutoffAt,
            deadlines.LatestCheckInAt);

        var compensationPolicy = _reliability is null
            ? null
            : await _reliability.GetCompensationPolicyAsync(parcel.OperatorId, cancellationToken);
        parcel.AcceptDeclaration(
            command.DeclaredValueVnd,
            declarationPolicyVersion: 1,
            now,
            compensationPolicy?.CompensationRatePercent
                ?? VietRide.Parcel.Domain.Entities.ParcelCompensationPolicy.DefaultRatePercent,
            compensationPolicy?.MaxCompensationVnd
                ?? VietRide.Parcel.Domain.Entities.ParcelCompensationPolicy.DefaultMaximumCompensationVnd,
            compensationPolicy?.NoProofFallbackMultiplier
                ?? VietRide.Parcel.Domain.Entities.ParcelCompensationPolicy.DefaultNoProofFallbackMultiplier,
            compensationPolicy?.Version ?? 1,
            compensationPolicy?.ClaimWindowDays
                ?? VietRide.Parcel.Domain.Entities.ParcelCompensationPolicy.DefaultClaimWindowDays,
            compensationPolicy?.SearchSlaHours
                ?? VietRide.Parcel.Domain.Entities.ParcelCompensationPolicy.DefaultSearchSlaHours,
            compensationPolicy?.DecisionSlaBusinessDays
                ?? VietRide.Parcel.Domain.Entities.ParcelCompensationPolicy.DefaultDecisionSlaBusinessDays,
            compensationPolicy?.PayoutSlaBusinessDays
                ?? VietRide.Parcel.Domain.Entities.ParcelCompensationPolicy.DefaultPayoutSlaBusinessDays,
            command.Quantity);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _parcelRepository.AddAsync(parcel, cancellationToken);

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Created,
                new { parcelId = parcel.Id, tripId = parcel.TripId, senderUserId = parcel.SenderUserId, recipientUserId = parcel.RecipientUserId, userIds = new[] { parcel.SenderUserId }.Concat(parcel.RecipientUserId.HasValue ? new[] { parcel.RecipientUserId.Value } : Array.Empty<Guid>()).Distinct().ToArray() },
                cancellationToken);

            await _statsRepository.UpsertIncrementAsync(
                parcel.OperatorId,
                VietRide.Shared.Kernel.Time.BusinessTime.ToLocalDate(now),
                totalParcels: 1,
                totalLoaded: 0,
                totalDelivered: 0,
                totalRejected: 0,
                totalReturned: 0,
                totalRevenue: 0,
                totalRefunded: 0,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return new CreateParcelResponse(
            parcel.Id,
            parcel.BookingId,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.EstimatedSizeCategory.ToString(),
            estimatedGrossPrice.Amount,
            discountAmount.Amount,
            estimatedTotalPrice.Amount,
            depositPercent,
            depositRequired.Amount,
            0,
            parcel.VoucherCode,
            ParcelCargoCalculator.SettlementPolicyVersion,
            new ParcelCompensationPolicySnapshotResponse(
                parcel.CompensationPolicyVersionSnapshot,
                parcel.CompensationRatePercentSnapshot,
                parcel.CompensationPolicyCapVndSnapshot,
                parcel.NoProofFallbackMultiplierSnapshot,
                parcel.ClaimWindowDaysSnapshot,
                parcel.SearchSlaHoursSnapshot,
                parcel.DecisionSlaBusinessDaysSnapshot,
                parcel.PayoutSlaBusinessDaysSnapshot));
    }

    private static PaymentContextSnapshot CreatePaymentContext(
        VietRide.Parcel.Domain.Entities.Parcel parcel,
        string referenceType,
        long amount)
        => new(1,
        [
            new PaymentAllocationSnapshot(
                parcel.Id,
                referenceType,
                parcel.OperatorId,
                parcel.TripId,
                amount,
                0,
                0,
                parcel.ParcelCode),
        ]);

    private async Task<string> GenerateParcelCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var code = ParcelCodeGenerator.Generate(_clock.UtcNow);
            var existing = await _parcelRepository.FindByParcelCodeAsync(code, cancellationToken);
            if (existing is null)
            {
                return code;
            }
        }

        throw new CodedConflictException(
            "PARCEL_CODE_COLLISION",
            "Failed to generate a unique parcel code after 3 attempts.");
    }

    private async Task CompensateVoucherUsageAsync(
        Guid parcelId,
        Guid voucherUsageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _bookingClient.DeleteVoucherUsageByReferenceAsync(
                parcelId,
                voucherUsageId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to compensate voucher usage for parcel {ParcelId} after payment failure.",
                parcelId);
        }
    }
}
