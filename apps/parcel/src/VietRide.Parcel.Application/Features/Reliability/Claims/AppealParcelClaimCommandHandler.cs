using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
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

        var claim = await _reliability.GetClaimByIdAsync(command.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        if (claim.ParcelId != parcel.Id || claim.BeneficiaryUserId != command.SenderUserId)
            throw new ForbiddenException("FORBIDDEN", "Claim does not belong to this sender and parcel.");
        if (claim.Status is not (ParcelClaimStatus.PAID or ParcelClaimStatus.REJECTED))
            throw new CodedConflictException(
                "PARCEL_CLAIM_APPEAL_NOT_ALLOWED",
                "Only paid or rejected claims can be appealed.");

        var now = _clock.UtcNow;
        claim.Appeal(command.Reason, command.SenderUserId, now);
        await _reliability.UpdateClaimAsync(claim, cancellationToken);

        var eventId = Guid.NewGuid();
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            eventId,
            ParcelOutboxEvents.ParcelClaimAppealed,
            new
            {
                eventId,
                occurredAt = now,
                claimId = claim.Id,
                parcelId = claim.ParcelId,
                incidentId = claim.IncidentId,
                operatorId = claim.OperatorId,
                beneficiaryUserId = claim.BeneficiaryUserId,
                status = claim.Status.ToString(),
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
