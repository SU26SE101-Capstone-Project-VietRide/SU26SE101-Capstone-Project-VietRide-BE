using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class ListOperatorParcelClaimsQueryHandler
    : IRequestHandler<ListOperatorParcelClaimsQuery, PagedResult<OperatorParcelClaimListItem>>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IIdentityServiceClient _identity;
    private readonly IClock _clock;

    public ListOperatorParcelClaimsQueryHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        ITripServiceClient trips,
        IIdentityServiceClient identity,
        IClock clock)
    {
        _reliability = reliability;
        _parcels = parcels;
        _trips = trips;
        _identity = identity;
        _clock = clock;
    }

    public async Task<PagedResult<OperatorParcelClaimListItem>> Handle(
        ListOperatorParcelClaimsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.Search?.Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        ParcelClaimStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status)
            && (!Enum.TryParse(request.Status, true, out ParcelClaimStatus parsed) || !Enum.IsDefined(parsed)))
            throw new CodedValidationException("VALIDATION_ERROR", "status is invalid.");
        else if (!string.IsNullOrWhiteSpace(request.Status))
            status = Enum.Parse<ParcelClaimStatus>(request.Status, true);
        if (!string.IsNullOrWhiteSpace(request.SlaState)
            && request.SlaState.ToUpperInvariant() is not ("ON_TRACK" or "DUE_SOON" or "BREACHED" or "CLOSED"))
            throw new CodedValidationException("VALIDATION_ERROR", "slaState is invalid.");

        var now = _clock.UtcNow;
        IReadOnlyList<Guid> matchedUserIds = [];
        var identityRequestUsedForSearch = !string.IsNullOrWhiteSpace(request.Search);
        if (identityRequestUsedForSearch)
        {
            var userSearch = await _identity.SearchUserIdsAsync(request.Search!, cancellationToken);
            matchedUserIds = userSearch.Kind switch
            {
                IdentityUserSearchOutcomeKind.Success => userSearch.UserIds,
                IdentityUserSearchOutcomeKind.TooBroad => throw new CodedValidationException(
                    "SEARCH_TOO_BROAD",
                    "User search matched too many records."),
                _ => throw new ParcelDependencyUnavailableException(
                    "UPSTREAM_UNAVAILABLE",
                    userSearch.ErrorMessage ?? "Identity search is unavailable."),
            };
        }
        var page = await _reliability.SearchClaimsByOperatorAsync(
            request.OperatorId,
            status,
            request.Search,
            matchedUserIds,
            request.SlaState,
            request.From,
            request.To?.AddTicks(1),
            now,
            request.Page,
            request.PageSize,
            cancellationToken);
        if (page.Items.Count == 0)
            return PagedResult<OperatorParcelClaimListItem>.Create([], page.Page, page.PageSize, page.TotalItems);

        var parcelIds = page.Items.Select(claim => claim.ParcelId).Distinct().ToArray();
        var parcels = await _parcels.ListByIdsAsync(parcelIds, cancellationToken);
        var parcelById = parcels.ToDictionary(parcel => parcel.Id);
        var incidents = await _reliability.ListIncidentsByIdsAsync(
            page.Items.Select(claim => claim.IncidentId).Distinct().ToArray(),
            cancellationToken);
        var incidentById = incidents.ToDictionary(incident => incident.Id);
        var evidence = await _reliability.ListClaimEvidenceByClaimsAsync(
            page.Items.Select(claim => claim.Id).ToArray(),
            cancellationToken);
        var evidenceCounts = evidence.GroupBy(item => item.ClaimId).ToDictionary(group => group.Key, group => group.Count());

        var tripOutcome = await _trips.GetTripSummariesAsync(
            parcels.Select(parcel => parcel.TripId).Distinct().ToArray(),
            cancellationToken);
        var tripById = tripOutcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? tripOutcome.Summaries.ToDictionary(trip => trip.TripId)
            : new Dictionary<Guid, TripSummarySnapshot>();
        var userOutcome = identityRequestUsedForSearch
            ? IdentityUserBatchOutcome.Success([])
            : await _identity.GetUsersAsync(
                parcels.Select(parcel => parcel.SenderUserId).Distinct().Take(100).ToArray(),
                cancellationToken);
        var userById = userOutcome.Kind == IdentityUserBatchOutcomeKind.Success
            ? userOutcome.Users.ToDictionary(user => user.Id)
            : new Dictionary<Guid, IdentityUserSummary>();

        var items = page.Items.Select(claim =>
        {
            if (!parcelById.TryGetValue(claim.ParcelId, out var parcel))
                throw new InvalidOperationException($"Parcel {claim.ParcelId} for claim {claim.Id} is missing.");
            incidentById.TryGetValue(claim.IncidentId, out var incident);
            tripById.TryGetValue(parcel.TripId, out var tripSnapshot);
            var summary = ParcelReliabilityReadModelService.MapClaim(claim, parcel, now);
            var deadline = summary?.DecisionDeadline ?? summary?.PayoutDeadline;
            return new OperatorParcelClaimListItem(
                claim.Id,
                claim.Status.ToString(),
                ListParcelIncidentsQueryHandler.MapParcel(parcel),
                ListParcelIncidentsQueryHandler.MapUser(parcel.SenderUserId, userById, "SENDER"),
                ParcelReliabilityReadModelService.MapIncident(incident, now),
                evidenceCounts.GetValueOrDefault(claim.Id),
                new ParcelCompensationPolicySnapshotResponse(
                    claim.PolicyVersion,
                    claim.CompensationRatePercent,
                    claim.PolicyCapVnd,
                    claim.NoProofFallbackMultiplier,
                    parcel.ClaimWindowDaysSnapshot,
                    parcel.SearchSlaHoursSnapshot,
                    parcel.DecisionSlaBusinessDaysSnapshot,
                    parcel.PayoutSlaBusinessDaysSnapshot),
                claim.CargoAwardVnd,
                claim.FreightRefundVnd,
                claim.TotalAwardVnd,
                deadline,
                summary?.SlaState,
                FundingStatus(claim.Status),
                ParcelReliabilityReadModelService.MapTrip(parcel, tripSnapshot),
                ClaimActions(claim.Status));
        }).ToArray();
        return PagedResult<OperatorParcelClaimListItem>.Create(items, page.Page, page.PageSize, page.TotalItems);
    }

    internal static IReadOnlyList<string> ClaimActions(ParcelClaimStatus status)
        => (status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW)
            ? new[] { "DECIDE_CLAIM" }
            : [];

    internal static string FundingStatus(ParcelClaimStatus status)
        => status switch
        {
            ParcelClaimStatus.FUNDING_PENDING => "FUNDING_PENDING",
            ParcelClaimStatus.APPROVED => "READY_FOR_PAYOUT",
            ParcelClaimStatus.PAID => "PAID",
            _ => "NOT_APPLICABLE",
        };
}
