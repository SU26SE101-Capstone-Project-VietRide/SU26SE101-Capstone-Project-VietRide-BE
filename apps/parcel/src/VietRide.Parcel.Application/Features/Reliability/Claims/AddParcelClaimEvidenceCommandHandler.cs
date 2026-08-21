using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class AddParcelClaimEvidenceCommandHandler
    : IRequestHandler<AddParcelClaimEvidenceCommand, ParcelClaimResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IClock _clock;

    public AddParcelClaimEvidenceCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _clock = clock;
    }

    public async Task<ParcelClaimResponse> Handle(
        AddParcelClaimEvidenceCommand command,
        CancellationToken cancellationToken)
    {
        var claim = await _reliability.GetClaimByIdAsync(command.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        if (claim.ParcelId != command.ParcelId)
            throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found for this parcel.");
        if (claim.BeneficiaryUserId != command.UploaderUserId)
            throw new ForbiddenException("FORBIDDEN", "Only the claim beneficiary can upload evidence.");
        if (claim.Status is not (Domain.Enums.ParcelClaimStatus.SUBMITTED or Domain.Enums.ParcelClaimStatus.UNDER_REVIEW))
            throw new CodedConflictException("PARCEL_CLAIM_ALREADY_DECIDED", "Evidence cannot be added after a claim decision.");
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");

        var evidence = ParcelClaimEvidence.Create(
            claim.Id,
            command.EvidenceType,
            command.Reference,
            command.Note,
            command.UploaderUserId);
        await _reliability.AddClaimEvidenceAsync(evidence, cancellationToken);
        var response = await ParcelClaimResponseMapper.MapAsync(
            claim,
            _reliability,
            cancellationToken,
            parcel);
        if (response.Evidence.Any(item => item.EvidenceId == evidence.Id))
            return response;

        // The unit-of-work commits after the handler returns, so an immediate database query
        // cannot see the newly added row yet. Include it in the mutation screen model explicitly.
        return response with
        {
            Evidence =
            [
                .. response.Evidence,
                new ParcelClaimEvidenceResponse(
                    evidence.Id,
                    evidence.EvidenceType,
                    evidence.Reference,
                    evidence.Note,
                    evidence.UploadedByUserId,
                    _clock.UtcNow),
            ],
        };
    }
}
