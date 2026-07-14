using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed class ReweighParcelCommandHandler
    : IRequestHandler<ReweighParcelCommand, ReweighParcelResponse>
{
    private const int FallbackAdditionalPaymentTimeoutMinutes = 30;

    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelRouteFareRepository _fareRepository;
    private readonly IParcelPricingPolicyRepository _policyRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIdentityServiceClient _identityClient;
    private readonly IPaymentServiceClient _paymentClient;
    private readonly IUnitOfWork _unitOfWork;

    public ReweighParcelCommandHandler(
        IParcelRepository parcelRepository,
        IParcelRouteFareRepository fareRepository,
        IParcelPricingPolicyRepository policyRepository,
        ITripServiceClient tripClient,
        IIdentityServiceClient identityClient,
        IPaymentServiceClient paymentClient,
        IUnitOfWork unitOfWork)
    {
        _parcelRepository = parcelRepository;
        _fareRepository = fareRepository;
        _policyRepository = policyRepository;
        _tripClient = tripClient;
        _identityClient = identityClient;
        _paymentClient = paymentClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<ReweighParcelResponse> Handle(
        ReweighParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (parcel.Status != ParcelStatus.PENDING)
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel is in status '{parcel.Status}'. Only PENDING parcels can be reweighed.");

        if (!Enum.TryParse<ParcelSizeCategory>(command.ActualSizeCategory, ignoreCase: true, out var actualSize))
            throw new CodedValidationException(
                "INVALID_SIZE_CATEGORY",
                $"'{command.ActualSizeCategory}' is not a valid ParcelSizeCategory.");

        if (command.PaymentMethod is not ("WALLET" or "VNPAY"))
            throw new CodedValidationException(
                "VALIDATION_ERROR", "PaymentMethod must be WALLET or VNPAY.");

        if (command.ActualWeightKg <= 0)
            throw new CodedValidationException(
                "VALIDATION_ERROR", "Actual weight must be positive.");

        // Get routeId from trip to look up the fare for actual size category
        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (tripOutcome.Kind != TripSnapshotOutcomeKind.Success)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                tripOutcome.ErrorMessage ?? "Trip service unavailable.");

        var routeId = tripOutcome.Snapshot!.RouteId;
        var departureDateTime = tripOutcome.Snapshot.DepartureDateTime;

        var now = DateTimeOffset.UtcNow;
        var dimFactor = await _policyRepository.GetSystemDecimalAsync(
            "DIM_WEIGHT_FACTOR",
            ParcelCargoCalculator.DefaultDimWeightFactor,
            now,
            cancellationToken);
        var tolerancePercent = await _policyRepository.GetSystemDecimalAsync(
            "REWEIGH_TOLERANCE_PERCENT",
            ParcelCargoCalculator.DefaultReweighTolerancePercent,
            now,
            cancellationToken);
        var autoOverflowPercent = await _policyRepository.GetSystemDecimalAsync(
            "AUTO_APPROVE_OVERFLOW_PERCENT",
            ParcelCargoCalculator.DefaultAutoApproveOverflowPercent,
            now,
            cancellationToken);
        var actualCargo = ParcelCargoCalculator.Calculate(
            command.ActualLengthCm,
            command.ActualWidthCm,
            command.ActualHeightCm,
            command.ActualWeightKg,
            dimFactor);

        var capacityBefore = await _tripClient.GetCargoCapacityAsync(parcel.TripId, cancellationToken);
        if (capacityBefore.Kind != TripCargoOutcomeKind.Success || capacityBefore.Capacity is null)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                capacityBefore.ErrorMessage ?? "Trip cargo capacity is unavailable.");

        var capacityOutcome = await _tripClient.RemeasureCargoAsync(
            parcel.TripId,
            parcel.Id,
            actualCargo.WeightKg,
            actualCargo.VolumeM3,
            allowCapacityOverflow: false,
            cancellationToken);
        if (capacityOutcome.Kind == TripCargoOutcomeKind.CapacityExceeded
            && IsAutoOverflowAllowed(capacityBefore.Capacity, parcel, actualCargo, autoOverflowPercent))
        {
            capacityOutcome = await _tripClient.RemeasureCargoAsync(
                parcel.TripId,
                parcel.Id,
                actualCargo.WeightKg,
                actualCargo.VolumeM3,
                allowCapacityOverflow: true,
                cancellationToken);
        }

        if (capacityOutcome.Kind != TripCargoOutcomeKind.Success)
        {
            await _parcelRepository.TrySetPendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.CAPACITY_EXCEEDED,
                capacityOutcome.ErrorMessage ?? "Actual parcel cargo exceeds trip capacity.",
                null,
                now,
                cancellationToken);

            return new ReweighParcelResponse(
                parcel.Id,
                parcel.ParcelCode,
                ParcelStatus.PENDING_OPERATOR_ACTION.ToString(),
                actualCargo.ChargeableWeightKg,
                parcel.TotalPrice.Amount,
                0,
                0,
                null);
        }

        var fare = await _fareRepository.FindByCompositeAsync(routeId, actualSize, cancellationToken);
        if (fare is null)
            throw new CodedValidationException(
                "FARE_NOT_CONFIGURED",
                $"No fare configured for route '{routeId}' and size category '{command.ActualSizeCategory}'.");

        var actualTotalPrice = ParcelCargoCalculator.CalculateTotalPrice(
            actualCargo.ChargeableWeightKg,
            fare.PricePerChargeableKgVnd.Amount > 0 ? fare.PricePerChargeableKgVnd : fare.PriceVnd,
            fare.MinimumPriceVnd);
        var paidAmount = parcel.DepositAmount + parcel.AdditionalAmount;
        var additionalRaw = Math.Max(0, actualTotalPrice.Amount - paidAmount.Amount);
        var additionalAmount = Money.FromRaw(additionalRaw);
        var refundRaw = Math.Max(0, paidAmount.Amount - actualTotalPrice.Amount);
        var refundAmount = Money.FromRaw(refundRaw);
        var outsideTolerance = ParcelCargoCalculator.IsOutsideTolerance(
            parcel.EstimatedChargeableWeightKg,
            actualCargo.ChargeableWeightKg,
            tolerancePercent);

        if (refundAmount.Amount > 0 && outsideTolerance)
        {
            await _parcelRepository.TrySetPendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.REFUND_CONFIRMATION,
                "Actual chargeable weight is lower than estimated and requires refund confirmation.",
                refundAmount,
                now,
                cancellationToken);

            return new ReweighParcelResponse(
                parcel.Id,
                parcel.ParcelCode,
                ParcelStatus.PENDING_OPERATOR_ACTION.ToString(),
                actualCargo.ChargeableWeightKg,
                actualTotalPrice.Amount,
                0,
                refundAmount.Amount,
                null);
        }

        if (additionalRaw == 0 || !outsideTolerance)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            ParcelPaymentTransitionSnapshot snapshot;
            try
            {
                snapshot = await _parcelRepository.TryReweighNoFeeAsync(
                    command.ParcelId,
                    actualCargo.LengthCm,
                    actualCargo.WidthCm,
                    actualCargo.HeightCm,
                    actualCargo.WeightKg,
                    actualCargo.VolumeM3,
                    actualCargo.DimWeightKg,
                    actualCargo.ChargeableWeightKg,
                    actualSize,
                    actualTotalPrice,
                    now,
                    cancellationToken)
                    ?? throw new CodedConflictException(
                        "RACE_LOST", "Parcel status changed during reweigh.");
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }

            return new ReweighParcelResponse(
                command.ParcelId,
                snapshot.ParcelCode,
                ParcelStatus.PENDING.ToString(),
                actualCargo.ChargeableWeightKg,
                actualTotalPrice.Amount,
                0,
                0,
                null);
        }
        else
        {
            // Additional fee required - transition to PENDING_ADDITIONAL_PAYMENT.
            var policyOutcome = await _identityClient.GetOperatorInfoAsync(parcel.OperatorId, cancellationToken);
            var timeoutMinutes = policyOutcome.Kind == OperatorLookupOutcomeKind.Success
                ? policyOutcome.OperatorInfo!.ParcelNoShowPolicy.AdditionalPaymentTimeoutMinutes
                : FallbackAdditionalPaymentTimeoutMinutes;
            var deadline = Min(now.AddMinutes(timeoutMinutes), departureDateTime);
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            ParcelPaymentTransitionSnapshot snapshot;
            try
            {
                snapshot = await _parcelRepository.TryReweighWithFeeAsync(
                    command.ParcelId,
                    actualCargo.LengthCm,
                    actualCargo.WidthCm,
                    actualCargo.HeightCm,
                    actualCargo.WeightKg,
                    actualCargo.VolumeM3,
                    actualCargo.DimWeightKg,
                    actualCargo.ChargeableWeightKg,
                    actualSize,
                    actualTotalPrice,
                    additionalAmount,
                    deadline,
                    now,
                    cancellationToken)
                    ?? throw new CodedConflictException(
                        "RACE_LOST", "Parcel status changed during reweigh.");
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }

            // Initiate additional payment
            var idempotencyKey = $"parcel:additional:{command.ParcelId}";
            var outcome = await _paymentClient.ChargeParcelPaymentAsync(
                "PARCEL_ADDITIONAL",
                command.ParcelId,
                parcel.SenderUserId,
                additionalAmount.Amount,
                command.PaymentMethod,
                idempotencyKey,
                cancellationToken,
                CreatePaymentContext(parcel, additionalAmount.Amount));

            if (outcome.Kind == ChargeOutcomeKind.InsufficientFunds)
            {
                throw new CodedValidationException(
                    "INSUFFICIENT_FUNDS",
                    outcome.ErrorMessage ?? "Insufficient wallet balance.");
            }

            if (outcome.Kind == ChargeOutcomeKind.TransportError)
            {
                throw new ParcelDependencyUnavailableException(
                    "PAYMENT_SERVICE_ERROR",
                    outcome.ErrorMessage ?? "Payment service unavailable.");
            }

            // Store AdditionalPaymentId if returned (atomic ExecuteUpdateAsync, not stale entity)
            if (outcome.Result?.PaymentId is not null)
            {
                await _parcelRepository.TryAssignAdditionalPaymentIdAsync(
                    command.ParcelId, outcome.Result.PaymentId, now, cancellationToken);
            }

            return new ReweighParcelResponse(
                command.ParcelId, snapshot.ParcelCode,
                ParcelStatus.PENDING_ADDITIONAL_PAYMENT.ToString(),
                actualCargo.ChargeableWeightKg,
                actualTotalPrice.Amount,
                additionalAmount.Amount,
                0,
                outcome.Result?.PaymentRedirectUrl);
        }
    }

    private static PaymentContextSnapshot CreatePaymentContext(
        VietRide.Parcel.Domain.Entities.Parcel parcel,
        long amount)
        => new(1,
        [
            new PaymentAllocationSnapshot(
                parcel.Id,
                "PARCEL_ADDITIONAL",
                parcel.OperatorId,
                parcel.TripId,
                amount,
                0,
                0),
        ]);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
        => left <= right ? left : right;

    private static bool IsAutoOverflowAllowed(
        TripCargoCapacitySnapshot capacity,
        Domain.Entities.Parcel parcel,
        ParcelCargoEstimate actualCargo,
        decimal thresholdPercent)
    {
        var weightOverflowPercent = CalculateOverflowPercent(
            capacity.ReservedWeightKg,
            parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
            capacity.LoadedWeightKg,
            capacity.MaxCargoWeightKg,
            actualCargo.WeightKg);

        var volumeOverflowPercent = CalculateOverflowPercent(
            capacity.ReservedVolumeM3,
            parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
            capacity.LoadedVolumeM3,
            capacity.MaxCargoVolumeM3,
            actualCargo.VolumeM3);

        return weightOverflowPercent <= thresholdPercent
            && volumeOverflowPercent <= thresholdPercent;
    }

    private static decimal CalculateOverflowPercent(
        decimal reservedAxis,
        decimal estimatedAxisOfThisParcel,
        decimal loadedAxis,
        decimal maxAxis,
        decimal actualAxis)
    {
        if (maxAxis <= 0m)
            return 100m;

        var reservedByOthers = Math.Max(0m, reservedAxis - estimatedAxisOfThisParcel);
        var poolForThisParcel = maxAxis - reservedByOthers - loadedAxis;
        var overflow = Math.Max(0m, actualAxis - poolForThisParcel);
        return overflow / maxAxis * 100m;
    }
}
