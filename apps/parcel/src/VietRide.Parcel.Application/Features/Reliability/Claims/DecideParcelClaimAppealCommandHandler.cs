using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class DecideParcelClaimAppealCommandHandler
    : IRequestHandler<DecideParcelClaimAppealCommand, ParcelClaimAppealResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public DecideParcelClaimAppealCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ParcelClaimAppealResponse> Handle(
        DecideParcelClaimAppealCommand command,
        CancellationToken cancellationToken)
    {
        var appeal = await _reliability.GetClaimAppealByIdForUpdateAsync(
            command.AppealId,
            cancellationToken);
        if (appeal is null || appeal.OperatorId != command.OperatorId)
            throw new CodedNotFoundException("PARCEL_CLAIM_APPEAL_NOT_FOUND", "Claim appeal was not found.");
        if (appeal.Status != ParcelClaimAppealStatus.SUBMITTED)
            throw new CodedConflictException(
                "PARCEL_CLAIM_APPEAL_ALREADY_DECIDED",
                "Claim appeal has already been decided.");

        var claim = await _reliability.GetClaimByIdAsync(appeal.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        var parcel = await _parcels.GetByIdAsync(appeal.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (claim.OperatorId != command.OperatorId || parcel.OperatorId != command.OperatorId)
            throw new CodedNotFoundException("PARCEL_CLAIM_APPEAL_NOT_FOUND", "Claim appeal was not found.");

        var now = _clock.UtcNow;
        appeal.BeginReview();
        if (command.Decision == "UPHOLD")
        {
            appeal.UpholdOriginalDecision(command.Reason, command.DecidedByUserId, now);
        }
        else
        {
            var award = ParcelCompensationCalculator.Calculate(
                command.RevisedProvenDirectLossVnd,
                parcel.DeclaredValueVnd,
                parcel.FinalTotalPriceVnd.Amount,
                parcel.RefundedAmountVnd.Amount,
                claim.CompensationRatePercent,
                claim.PolicyCapVnd,
                claim.NoProofFallbackMultiplier);
            if (award.TotalAwardVnd <= appeal.OriginalTotalAwardVnd)
                throw new CodedValidationException(
                    "PARCEL_CLAIM_APPEAL_ADJUSTMENT_REQUIRED",
                    "An approved appeal must increase the total compensation award.");
            appeal.ApproveAdjustment(
                command.RevisedProvenDirectLossVnd,
                award.CargoAwardVnd,
                award.FreightRefundVnd,
                command.Reason,
                command.DecidedByUserId,
                now);
        }

        await _reliability.UpdateClaimAppealAsync(appeal, cancellationToken);
        var eventId = Guid.NewGuid();
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            eventId,
            ParcelOutboxEvents.ParcelClaimAppealDecided,
            new
            {
                eventId,
                occurredAt = now,
                appealId = appeal.Id,
                claimId = appeal.ClaimId,
                parcelId = appeal.ParcelId,
                tripId = parcel.TripId,
                operatorId = appeal.OperatorId,
                beneficiaryUserId = appeal.BeneficiaryUserId,
                status = appeal.Status.ToString(),
                supplementaryAwardVnd = appeal.SupplementaryAwardVnd,
            },
            cancellationToken);

        return ParcelClaimAppealResponseMapper.Map(appeal, operatorView: true);
    }
}
