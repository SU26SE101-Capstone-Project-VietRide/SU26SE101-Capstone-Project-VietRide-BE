using System.Text.Json;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class UnidentifiedParcelPackage : BaseEntity<Guid>
{
    public string TemporaryExceptionTag { get; private set; } = null!;
    public Guid OperatorId { get; private set; }
    public Guid? TripId { get; private set; }
    public ParcelCustodyLocationType LocationType { get; private set; }
    public Guid LocationId { get; private set; }
    public string? LocationSnapshot { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal? ObservedWeightKg { get; private set; }
    public string EvidenceReferencesJson { get; private set; } = "[]";
    public UnidentifiedParcelPackageStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? MatchedParcelId { get; private set; }
    public DateTimeOffset? MatchedAt { get; private set; }
    public Guid? MatchedByUserId { get; private set; }

    private UnidentifiedParcelPackage()
    {
    }

    public static UnidentifiedParcelPackage Create(
        string temporaryExceptionTag,
        Guid operatorId,
        Guid? tripId,
        ParcelCustodyLocationType locationType,
        Guid locationId,
        string? locationSnapshot,
        string description,
        decimal? observedWeightKg,
        IReadOnlyCollection<string> evidenceReferences,
        Guid createdByUserId)
    {
        if (operatorId == Guid.Empty || locationId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Operator, location and creator ids are required.");
        if (string.IsNullOrWhiteSpace(temporaryExceptionTag) || string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Temporary tag and description are required.");
        if (observedWeightKg is <= 0)
            throw new ArgumentOutOfRangeException(nameof(observedWeightKg));
        if (evidenceReferences.Count == 0)
            throw new ArgumentException("At least one evidence photo is required.", nameof(evidenceReferences));

        return new UnidentifiedParcelPackage
        {
            Id = Guid.NewGuid(),
            TemporaryExceptionTag = temporaryExceptionTag.Trim(),
            OperatorId = operatorId,
            TripId = tripId,
            LocationType = locationType,
            LocationId = locationId,
            LocationSnapshot = string.IsNullOrWhiteSpace(locationSnapshot) ? null : locationSnapshot.Trim(),
            Description = description.Trim(),
            ObservedWeightKg = observedWeightKg,
            EvidenceReferencesJson = JsonSerializer.Serialize(evidenceReferences),
            Status = UnidentifiedParcelPackageStatus.UNIDENTIFIED,
            CreatedByUserId = createdByUserId,
        };
    }

    public void Match(Guid parcelId, Guid matchedByUserId, DateTimeOffset matchedAt)
    {
        if (Status != UnidentifiedParcelPackageStatus.UNIDENTIFIED)
            throw new InvalidOperationException("Only an unidentified package can be matched.");
        if (parcelId == Guid.Empty || matchedByUserId == Guid.Empty)
            throw new ArgumentException("Parcel and matcher ids are required.");
        MatchedParcelId = parcelId;
        MatchedByUserId = matchedByUserId;
        MatchedAt = matchedAt;
        Status = UnidentifiedParcelPackageStatus.MATCHED;
    }
}
