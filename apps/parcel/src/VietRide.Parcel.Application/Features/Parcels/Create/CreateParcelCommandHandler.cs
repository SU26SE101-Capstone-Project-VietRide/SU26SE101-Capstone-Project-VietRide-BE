using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
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
    private readonly IParcelRouteFareRepository _fareRepository;
    private readonly IParcelPricingPolicyRepository? _policyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly ILogger<CreateParcelCommandHandler> _logger;
    private readonly IClock _clock;

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
        IClock clock)
    {
        _identityClient = identityClient;
        _bookingClient = bookingClient;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
        _parcelRepository = parcelRepository;
        _fareRepository = fareRepository;
        _policyRepository = policyRepository;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _logger = logger;
        _clock = clock;
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
            new SystemClock())
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
        if (trip.Status != "SCHEDULED" && trip.Status != "BOARDING")
            throw new CodedConflictException(
                "TRIP_NOT_ACCEPTING_PARCEL",
                $"Trip '{command.TripId}' is in status '{trip.Status}' and is not accepting parcels.");

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
        var dimFactor = _policyRepository is null
            ? ParcelCargoCalculator.DefaultDimWeightFactor
            : await _policyRepository.GetSystemDecimalAsync(
                "DIM_WEIGHT_FACTOR",
                ParcelCargoCalculator.DefaultDimWeightFactor,
                now,
                cancellationToken);
        var cargoEstimate = ParcelCargoCalculator.Calculate(
            command.LengthCm,
            command.WidthCm,
            command.HeightCm,
            command.EstimatedWeightKg,
            dimFactor);
        var sizeCategory = _policyRepository is null
            && Enum.TryParse<ParcelSizeCategory>(command.SizeCategory, ignoreCase: true, out var legacySizeCategory)
                ? legacySizeCategory
                : ParcelCargoCalculator.DeriveSizeCategory(cargoEstimate.ChargeableWeightKg);
        var deadlines = ParcelCargoCalculator.CalculateSettlementDeadlines(trip.DepartureDateTime);
        if (now >= deadlines.LatestCheckInAt)
            throw new CodedConflictException(
                "PARCEL_CHECK_IN_CLOSED",
                "The trip no longer has enough time for parcel check-in and final settlement.");

        var fare = await _fareRepository.FindByCompositeAsync(trip.RouteId, sizeCategory, cancellationToken);
        if (fare is null && (_policyRepository is not null || sizeCategory != ParcelSizeCategory.EXTRA_LARGE))
            throw new CodedValidationException(
                "FARE_NOT_CONFIGURED",
                $"No fare configured for route '{trip.RouteId}' and size category '{command.SizeCategory}'.");

        var estimatedGrossPrice = fare is null
            ? Money.Zero
            : _policyRepository is null
                ? fare.PriceVnd
                : ParcelCargoCalculator.CalculateTotalPrice(
                    cargoEstimate.ChargeableWeightKg,
                    fare.PricePerChargeableKgVnd.Amount > 0 ? fare.PricePerChargeableKgVnd : fare.PriceVnd,
                    fare.MinimumPriceVnd);
        var depositPercent = ParcelCargoCalculator.DefaultDepositPercent;
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

        var estimatedTotalPrice = ParcelCargoCalculator.CalculateDiscountedTotal(
            estimatedGrossPrice,
            discountAmount);
        var depositRequired = ParcelCargoCalculator.CalculatePercent(
            estimatedTotalPrice,
            depositPercent);

        var parcel = sizeCategory == ParcelSizeCategory.EXTRA_LARGE
            ? ParcelEntity.CreatePendingOperatorReview(
                parcelCode,
                command.SenderUserId,
                command.RecipientUserId,
                command.RecipientName,
                recipientPhone,
                command.RecipientEmail,
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
                null)
            : ParcelEntity.CreatePendingPayment(
                parcelCode,
                command.SenderUserId,
                command.RecipientUserId,
                command.RecipientName,
                recipientPhone,
                command.RecipientEmail,
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

        parcel.ConfigureSettlementV2(
            sizeCategory,
            estimatedGrossPrice,
            discountAmount,
            estimatedTotalPrice,
            depositPercent,
            depositRequired,
            fare?.PricePerChargeableKgVnd.Amount > 0
                ? fare.PricePerChargeableKgVnd
                : fare?.PriceVnd ?? Money.Zero,
            fare?.MinimumPriceVnd ?? Money.Zero,
            dimFactor,
            deadlines.LoadCutoffAt,
            deadlines.LatestCheckInAt);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _parcelRepository.AddAsync(parcel, cancellationToken);

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Created,
                new { parcelId = parcel.Id, tripId = parcel.TripId, senderUserId = parcel.SenderUserId, recipientUserId = parcel.RecipientUserId, userIds = new[] { parcel.SenderUserId }.Concat(parcel.RecipientUserId.HasValue ? new[] { parcel.RecipientUserId.Value } : Array.Empty<Guid>()).Distinct().ToArray() },
                cancellationToken);

            if (sizeCategory == ParcelSizeCategory.EXTRA_LARGE)
            {
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.ReviewRequested,
                    new { parcelId = parcel.Id, operatorId = parcel.OperatorId },
                    cancellationToken);
            }

            await _statsRepository.UpsertIncrementAsync(
                parcel.OperatorId,
                DateOnly.FromDateTime(now.UtcDateTime),
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
            ParcelCargoCalculator.SettlementPolicyVersion);
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
                0),
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
