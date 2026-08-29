using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class AppealParcelClaimCommandHandler
    : IRequestHandler<AppealParcelClaimCommand, ParcelClaimResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public AppealParcelClaimCommandHandler(
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
        AppealParcelClaimCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new CodedValidationException("VALIDATION_ERROR", "An appeal reason is required.");

        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.SenderUserId != command.SenderUserId)
            throw new ForbiddenException("FORBIDDEN", "Only the sender can appeal a parcel claim.");

        var claim = await _reliability.GetClaimByIdForUpdateAsync(command.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        if (claim.ParcelId != parcel.Id || claim.BeneficiaryUserId != command.SenderUserId)
            throw new ForbiddenException("FORBIDDEN", "Claim does not belong to this sender and parcel.");
        if (claim.Status is not (ParcelClaimStatus.PAID or ParcelClaimStatus.REJECTED))
            throw new CodedConflictException(
                "PARCEL_CLAIM_APPEAL_NOT_ALLOWED",
                "Only paid or rejected claims can be appealed.");

        var replay = await _reliability.GetClaimAppealByIdempotencyKeyAsync(
            command.IdempotencyKey,
            cancellationToken);
        if (replay is not null)
        {
            if (replay.ClaimId != claim.Id || replay.SubmittedByUserId != command.SenderUserId)
                throw new CodedConflictException(
                    "IDEMPOTENCY_KEY_REUSED",
                    "Idempotency-Key was already used for a different claim appeal.");
            return await ParcelClaimResponseMapper.MapAsync(
                claim,
                _reliability,
                cancellationToken,
                parcel,
                operatorView: false,
                now: _clock.UtcNow);
        }
        var existing = await _reliability.GetClaimAppealByClaimAsync(claim.Id, cancellationToken);
        if (existing is not null)
            throw new CodedConflictException(
                "PARCEL_CLAIM_APPEAL_ALREADY_EXISTS",
                "This claim already has an appeal.");

        var now = _clock.UtcNow;
        var appeal = ParcelClaimAppeal.Submit(
            claim,
            command.Reason,
            command.SenderUserId,
            now,
            command.IdempotencyKey);
        await _reliability.AddClaimAppealAsync(appeal, cancellationToken);

        var eventId = Guid.NewGuid();
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            eventId,
            ParcelOutboxEvents.ParcelClaimAppealed,
            new
            {
                eventId,
                occurredAt = now,
                appealId = appeal.Id,
                claimId = claim.Id,
                parcelId = claim.ParcelId,
                incidentId = claim.IncidentId,
                operatorId = claim.OperatorId,
                beneficiaryUserId = claim.BeneficiaryUserId,
                status = appeal.Status.ToString(),
            },
            cancellationToken);

        return await ParcelClaimResponseMapper.MapAsync(
            claim,
            _reliability,
            cancellationToken,
            parcel,
            operatorView: false,
            now: now);
    }
}
