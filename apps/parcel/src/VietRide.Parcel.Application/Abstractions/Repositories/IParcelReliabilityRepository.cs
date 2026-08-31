using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelReliabilityRepository
{
    Task<ParcelTransitLeg?> GetActiveLegAsync(Guid parcelId, CancellationToken ct = default);

    Task<ParcelTransitLeg?> GetTransitLegAsync(
        Guid parcelId,
        Guid tripId,
        CancellationToken ct = default);

    Task<ParcelTransitLeg?> GetLatestTransitLegAsync(
        Guid parcelId,
        CancellationToken ct = default);

    Task<ParcelCustodyEvent?> GetCustodyEventByIdempotencyAsync(
        Guid parcelId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelCustodyEvent>> ListCustodyEventsAsync(
        Guid parcelId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelCustodyEvent>> ListCustodyEventsPageAsync(
        Guid parcelId,
        int? beforeSequence,
        int take,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelCustodyEvent>> ListCustodyEventsByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default);

    Task<ParcelCurrentCustody?> GetCurrentCustodyAsync(
        Guid parcelId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelCurrentCustody>> ListCurrentCustodiesAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default);

    Task<ParcelIncident?> GetIncidentAsync(Guid incidentId, CancellationToken ct = default);

    async Task<ParcelIncident?> GetForwardingIncidentForUpdateAsync(
        Guid parcelId,
        CancellationToken ct = default)
        => (await ListActiveIncidentsByParcelsAsync([parcelId], ct))
            .FirstOrDefault(incident => incident.Status == ParcelIncidentStatus.FORWARDING);

    Task<IReadOnlyList<ParcelIncident>> ListIncidentsByIdsAsync(
        IReadOnlyCollection<Guid> incidentIds,
        CancellationToken ct = default);

    Task<ParcelIncident?> GetOpenIncidentAsync(
        Guid parcelId,
        ParcelIncidentType type,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelIncident>> ListIncidentsByParcelAsync(
        Guid parcelId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelIncident>> ListActiveIncidentsByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelIncident>> ListIncidentsByOperatorAsync(
        Guid operatorId,
        ParcelIncidentStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PagedResult<ParcelIncident>> SearchIncidentsByOperatorAsync(
        Guid operatorId,
        ParcelIncidentStatus? status,
        ParcelIncidentType? type,
        string? search,
        IReadOnlyCollection<Guid> senderUserIds,
        Guid? tripId,
        Guid? assigneeId,
        string? slaState,
        ParcelCustodyExceptionRequestStatus? approvalStatus,
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        DateTimeOffset now,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ParcelClaim?> GetClaimByIdAsync(Guid claimId, CancellationToken ct = default);

    Task<ParcelClaim?> GetClaimByIdForUpdateAsync(Guid claimId, CancellationToken ct = default);

    Task<ParcelClaim?> GetClaimByIncidentAsync(Guid incidentId, CancellationToken ct = default);

    Task<ParcelClaimAppeal?> GetClaimAppealByIdAsync(Guid appealId, CancellationToken ct = default);

    Task<ParcelClaimAppeal?> GetClaimAppealByIdForUpdateAsync(Guid appealId, CancellationToken ct = default);

    Task<ParcelClaimAppeal?> GetClaimAppealByClaimAsync(Guid claimId, CancellationToken ct = default);

    Task<ParcelClaimAppeal?> GetClaimAppealByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken ct = default);

    Task<PagedResult<ParcelClaimAppeal>> SearchClaimAppealsByOperatorAsync(
        Guid operatorId,
        ParcelClaimAppealStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ParcelCompensationPolicy?> GetCompensationPolicyAsync(Guid operatorId, CancellationToken ct = default);

    Task<UnidentifiedParcelPackage?> GetUnidentifiedPackageAsync(Guid packageId, CancellationToken ct = default);

    Task<ParcelSearchTask?> GetSearchTaskAsync(Guid taskId, CancellationToken ct = default);

    Task<IReadOnlyList<ParcelSearchTask>> ListSearchTasksAsync(Guid incidentId, CancellationToken ct = default);

    Task<IReadOnlyList<ParcelSearchTask>> ListSearchTasksByIncidentsAsync(
        IReadOnlyCollection<Guid> incidentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelIncident>> ListExpiredSearchIncidentsAsync(
        DateTimeOffset now,
        int maxBatch,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelClaim>> ListClaimsByParcelAsync(Guid parcelId, CancellationToken ct = default);

    Task<IReadOnlyList<ParcelClaim>> ListLatestClaimsByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default);

    Task<PagedResult<ParcelClaim>> SearchClaimsByOperatorAsync(
        Guid operatorId,
        ParcelClaimStatus? status,
        string? search,
        IReadOnlyCollection<Guid> senderUserIds,
        string? slaState,
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        DateTimeOffset now,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelClaimEvidence>> ListClaimEvidenceAsync(Guid claimId, CancellationToken ct = default);

    Task<IReadOnlyList<ParcelClaimEvidence>> ListClaimEvidenceByClaimsAsync(
        IReadOnlyCollection<Guid> claimIds,
        CancellationToken ct = default);

    Task<PagedResult<UnidentifiedParcelPackage>> ListUnidentifiedPackagesAsync(
        Guid operatorId,
        UnidentifiedParcelPackageStatus? status,
        string? search,
        Guid? tripId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<VietRide.Parcel.Domain.Entities.Parcel>> ListUnidentifiedMatchCandidatesAsync(
        Guid operatorId,
        Guid? tripId,
        decimal? observedWeightKg,
        int maxResults,
        CancellationToken ct = default);

    Task AddTransitLegAsync(ParcelTransitLeg entity, CancellationToken ct = default);
    Task AddCustodyEventAsync(ParcelCustodyEvent entity, CancellationToken ct = default);
    Task AddCurrentCustodyAsync(ParcelCurrentCustody entity, CancellationToken ct = default);
    Task AddIncidentAsync(ParcelIncident entity, CancellationToken ct = default);
    Task AddSearchTaskAsync(ParcelSearchTask entity, CancellationToken ct = default);
    Task AddClaimAsync(ParcelClaim entity, CancellationToken ct = default);
    Task AddClaimAppealAsync(ParcelClaimAppeal entity, CancellationToken ct = default);
    Task AddClaimEvidenceAsync(ParcelClaimEvidence entity, CancellationToken ct = default);
    Task AddCompensationPolicyAsync(ParcelCompensationPolicy entity, CancellationToken ct = default);
    Task AddUnidentifiedPackageAsync(UnidentifiedParcelPackage entity, CancellationToken ct = default);

    Task UpdateCurrentCustodyAsync(ParcelCurrentCustody entity, CancellationToken ct = default);
    Task UpdateTransitLegAsync(ParcelTransitLeg entity, CancellationToken ct = default);
    Task UpdateIncidentAsync(ParcelIncident entity, CancellationToken ct = default);
    Task UpdateSearchTaskAsync(ParcelSearchTask entity, CancellationToken ct = default);
    Task UpdateClaimAsync(ParcelClaim entity, CancellationToken ct = default);
    Task UpdateClaimAppealAsync(ParcelClaimAppeal entity, CancellationToken ct = default);
    Task UpdateCompensationPolicyAsync(ParcelCompensationPolicy entity, CancellationToken ct = default);
    Task UpdateUnidentifiedPackageAsync(UnidentifiedParcelPackage entity, CancellationToken ct = default);
}
