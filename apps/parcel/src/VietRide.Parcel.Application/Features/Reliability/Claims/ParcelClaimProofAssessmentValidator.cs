using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

internal static class ParcelClaimProofAssessmentValidator
{
    public static async Task<ValidatedParcelClaimProof> ValidateAsync(
        string? proofStatus,
        long? provenDirectLossVnd,
        IReadOnlyList<Guid>? acceptedEvidenceIds,
        Guid claimId,
        IParcelReliabilityRepository reliability,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ParcelClaimProofStatus>(proofStatus, true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "proofStatus must be VERIFIED, UNVERIFIED or NO_PROOF.");
        }

        if (acceptedEvidenceIds is null)
            throw EvidenceRequired("acceptedEvidenceIds is required.");
        if (acceptedEvidenceIds.Any(id => id == Guid.Empty)
            || acceptedEvidenceIds.Distinct().Count() != acceptedEvidenceIds.Count)
        {
            throw EvidenceRequired("acceptedEvidenceIds must contain unique, non-empty ids.");
        }

        if (parsed == ParcelClaimProofStatus.VERIFIED)
        {
            if (!provenDirectLossVnd.HasValue || acceptedEvidenceIds.Count == 0)
                throw EvidenceRequired("VERIFIED requires a proven direct loss and accepted evidence.");
        }
        else if (provenDirectLossVnd.HasValue || acceptedEvidenceIds.Count > 0)
        {
            throw EvidenceRequired("UNVERIFIED and NO_PROOF cannot include a proven loss or accepted evidence.");
        }

        if (provenDirectLossVnd is < 0)
            throw EvidenceRequired("provenDirectLossVnd cannot be negative.");

        if (acceptedEvidenceIds.Count > 0)
        {
            var evidence = await reliability.ListClaimEvidenceAsync(claimId, cancellationToken);
            var evidenceIds = evidence.Select(item => item.Id).ToHashSet();
            if (acceptedEvidenceIds.Any(id => !evidenceIds.Contains(id)))
            {
                throw new CodedNotFoundException(
                    "PARCEL_CLAIM_EVIDENCE_NOT_FOUND",
                    "Accepted evidence was not found for this claim.");
            }
        }

        return new ValidatedParcelClaimProof(
            parsed,
            provenDirectLossVnd,
            acceptedEvidenceIds.ToArray());
    }

    private static CodedValidationException EvidenceRequired(string message)
        => new("PARCEL_CLAIM_EVIDENCE_REQUIRED", message);
}

internal sealed record ValidatedParcelClaimProof(
    ParcelClaimProofStatus ProofStatus,
    long? ProvenDirectLossVnd,
    IReadOnlyList<Guid> AcceptedEvidenceIds);
