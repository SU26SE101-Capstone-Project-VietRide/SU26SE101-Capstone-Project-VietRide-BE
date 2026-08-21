using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelClaimEvidence : BaseEntity<Guid>
{
    public Guid ClaimId { get; private set; }
    public string EvidenceType { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public string? Note { get; private set; }
    public Guid UploadedByUserId { get; private set; }

    private ParcelClaimEvidence()
    {
    }

    public static ParcelClaimEvidence Create(
        Guid claimId,
        string evidenceType,
        string reference,
        string? note,
        Guid uploadedByUserId)
    {
        if (claimId == Guid.Empty || uploadedByUserId == Guid.Empty)
            throw new ArgumentException("Claim and uploader ids are required.");

        return new ParcelClaimEvidence
        {
            Id = Guid.NewGuid(),
            ClaimId = claimId,
            EvidenceType = Normalize(evidenceType, nameof(evidenceType)),
            Reference = Normalize(reference, nameof(reference)),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            UploadedByUserId = uploadedByUserId,
        };
    }

    private static string Normalize(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();
}
