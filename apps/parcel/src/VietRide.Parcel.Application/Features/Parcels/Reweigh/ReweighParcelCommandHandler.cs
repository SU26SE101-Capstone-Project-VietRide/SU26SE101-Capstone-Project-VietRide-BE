using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed class ReweighParcelCommandHandler
    : IRequestHandler<ReweighParcelCommand, ReweighParcelResponse>
{
    private const string SettlementRefundReason = "SETTLEMENT_PRICE_DECREASE";

    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ReweighParcelCommandHandler(
        IParcelRepository parcels,
        ITripServiceClient trips,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _parcels = parcels;
        _trips = trips;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<ReweighParcelResponse> Handle(
        ReweighParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        var authorization = await _trips.AuthorizeAssistantForTripAsync(
            parcel.TripId,
            command.AssistantUserId,
            command.OperatorId,
            cancellationToken);
        if (authorization.Kind is TripCrewAuthorizationOutcomeKind.Denied
            or TripCrewAuthorizationOutcomeKind.TripNotFound)
        {
            throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can reweigh this parcel.");
        }

        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                authorization.ErrorMessage ?? "Trip assignment verification failed.");
        }

        if (parcel.Status != ParcelStatus.CHECKED_IN)
        {
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel is in status '{parcel.Status}'. Only CHECKED_IN parcels can be reweighed.");
        }

        var now = _clock.UtcNow;
        if (!parcel.LoadCutoffAt.HasValue || now >= parcel.LoadCutoffAt.Value)
        {
            throw new CodedConflictException(
                "PARCEL_LOAD_CUTOFF_PASSED",
                "The parcel load cutoff has passed.");
        }

        var actualCargo = ParcelCargoCalculator.Calculate(
            command.ActualLengthCm,
            command.ActualWidthCm,
            command.ActualHeightCm,
            command.ActualWeightKg,
            parcel.DimWeightFactor);
        var actualSize = ParcelCargoCalculator.DeriveSizeCategory(actualCargo.ChargeableWeightKg);
        var finalGrossPrice = ParcelCargoCalculator.CalculateTotalPrice(
            actualCargo.ChargeableWeightKg,
            parcel.PricePerKgVnd,
            parcel.MinimumPriceVnd);
        var finalTotalPrice = ParcelCargoCalculator.CalculateDiscountedTotal(
            finalGrossPrice,
            parcel.DiscountAmountVnd);
        var balanceRequired = Money.FromRaw(
            Math.Max(0, finalTotalPrice.Amount - parcel.DepositPaidVnd.Amount));
        var refundDue = Money.FromRaw(
            Math.Max(0, parcel.DepositPaidVnd.Amount - finalTotalPrice.Amount));
        var resumeStatus = balanceRequired.Amount > 0
            ? ParcelStatus.PENDING_FINAL_PAYMENT
            : ParcelStatus.READY_TO_LOAD;
        var finalPaymentDeadline = balanceRequired.Amount > 0
            ? Min(
                now.AddMinutes(ParcelCargoCalculator.FinalPaymentTimeoutMinutes),
                parcel.LoadCutoffAt.Value)
            : (DateTimeOffset?)null;

        var operationId = Guid.TryParse(command.IdempotencyKey, out var parsedOperationId)
            ? parsedOperationId
            : parcel.Id;
        var capacityOutcome = await _trips.RemeasureCargoAsync(
            parcel.TripId,
            parcel.Id,
            actualCargo.WeightKg,
            actualCargo.VolumeM3,
            allowCapacityOverflow: false,
            operationId,
            cancellationToken);

        var capacityAccepted = capacityOutcome.Kind == TripCargoOutcomeKind.Success;
        if (!capacityAccepted && capacityOutcome.Kind != TripCargoOutcomeKind.CapacityExceeded)
        {
            throw capacityOutcome.Kind == TripCargoOutcomeKind.TripNotFound
                ? new ParcelDependencyUnavailableException(
                    "TRIP_NOT_FOUND",
                    capacityOutcome.ErrorMessage ?? "Trip was not found.")
                : new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    capacityOutcome.ErrorMessage ?? "Trip cargo capacity is unavailable.");
        }

        ParcelPaymentTransitionSnapshot snapshot;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcels.TrySettleReweighAsync(
                parcel.Id,
                command.AssistantUserId,
                actualCargo.LengthCm,
                actualCargo.WidthCm,
                actualCargo.HeightCm,
                actualCargo.WeightKg,
                actualCargo.VolumeM3,
                actualCargo.DimWeightKg,
                actualCargo.ChargeableWeightKg,
                actualSize,
                finalGrossPrice,
                finalTotalPrice,
                balanceRequired,
                refundDue,
                finalPaymentDeadline,
                resumeStatus,
                capacityAccepted,
                capacityOutcome.ErrorMessage,
                now,
                cancellationToken)
                ?? throw new CodedConflictException(
                    "RACE_LOST",
                    "Parcel status changed during reweigh.");

            if (capacityAccepted && refundDue.Amount > 0)
            {
                await ParcelOutboxEvents.EnqueueRefundAsync(
                    _outbox,
                    parcel.Id,
                    parcel.SenderUserId,
                    refundDue.Amount,
                    $"{parcel.Id:D}:{SettlementRefundReason}",
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return new ReweighParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            actualSize.ToString(),
            actualCargo.ChargeableWeightKg,
            finalGrossPrice.Amount,
            parcel.DiscountAmountVnd.Amount,
            finalTotalPrice.Amount,
            parcel.DepositPaidVnd.Amount,
            balanceRequired.Amount,
            refundDue.Amount,
            finalPaymentDeadline);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
        => left <= right ? left : right;
}
