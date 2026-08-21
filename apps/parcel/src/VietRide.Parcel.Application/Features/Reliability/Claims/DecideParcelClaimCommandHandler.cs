using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class DecideParcelClaimCommandHandler
    : IRequestHandler<DecideParcelClaimCommand, ParcelClaimResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public DecideParcelClaimCommandHandler(
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

    public async Task<ParcelClaimResponse> Handle(
        DecideParcelClaimCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new CodedValidationException("PARCEL_CLAIM_EVIDENCE_REQUIRED", "A decision reason is required.");

        var claim = await _reliability.GetClaimByIdAsync(command.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        if (claim.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Claim does not belong to this operator.");
        if (claim.Status != ParcelClaimStatus.SUBMITTED)
            throw new CodedConflictException("PARCEL_CLAIM_ALREADY_DECIDED", "Claim has already been decided.");
        var parcel = await _parcels.GetByIdAsync(claim.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");

        claim.BeginReview();
        var now = _clock.UtcNow;
        Guid? tripId = parcel.TripId;
        if (string.Equals(command.Decision, "REJECT", StringComparison.OrdinalIgnoreCase))
        {
            claim.Reject(command.Reason, command.DecidedBy, now);
        }
        else if (string.Equals(command.Decision, "APPROVE", StringComparison.OrdinalIgnoreCase))
        {
            var rate = claim.CompensationRatePercent;
            var cap = claim.PolicyCapVnd;
            var award = ParcelCompensationCalculator.Calculate(
                command.ProvenDirectLossVnd,
                parcel.DeclaredValueVnd,
                parcel.FinalTotalPriceVnd.Amount,
                parcel.RefundedAmountVnd.Amount,
                rate,
                cap,
                claim.NoProofFallbackMultiplier);
            if (award.TotalAwardVnd <= 0)
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "An approved claim must have a positive total award.");

            claim.Approve(
                command.ProvenDirectLossVnd,
                rate,
                cap,
                award.CargoAwardVnd,
                award.FreightRefundVnd,
                command.Reason,
                command.DecidedBy,
                now);
        }
        else
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Decision must be APPROVE or REJECT.");
        }

        await _reliability.UpdateClaimAsync(claim, cancellationToken);
        var decisionEventId = Guid.NewGuid();
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            decisionEventId,
            ParcelOutboxEvents.ParcelClaimDecided,
            new
            {
                eventId = decisionEventId,
                occurredAt = now,
                claimId = claim.Id,
                parcelId = claim.ParcelId,
                tripId,
                operatorId = claim.OperatorId,
                status = claim.Status.ToString(),
                totalAwardVnd = claim.TotalAwardVnd,
                beneficiaryUserId = claim.BeneficiaryUserId,
            },
            cancellationToken);

        return await ParcelClaimResponseMapper.MapAsync(
            claim,
            _reliability,
            cancellationToken,
            parcel,
            operatorView: true,
            now: now);
    }
}
