using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class ListParcelIncidentsQueryHandler
    : IRequestHandler<ListParcelIncidentsQuery, PagedResult<ParcelIncidentListItem>>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly IParcelCustodyExceptionRequestRepository _custodyExceptionRequests;
    private readonly ITripServiceClient _trips;
    private readonly IIdentityServiceClient _identity;
    private readonly IClock _clock;

    public ListParcelIncidentsQueryHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        IParcelCustodyExceptionRequestRepository custodyExceptionRequests,
        ITripServiceClient trips,
        IIdentityServiceClient identity,
        IClock clock)
    {
        _reliability = reliability;
        _parcels = parcels;
        _custodyExceptionRequests = custodyExceptionRequests;
        _trips = trips;
        _identity = identity;
        _clock = clock;
    }

    public async Task<PagedResult<ParcelIncidentListItem>> Handle(
        ListParcelIncidentsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.Search?.Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        var parsedStatus = ParseEnum<ParcelIncidentStatus>(request.Status, "status");
        var parsedType = ParseEnum<ParcelIncidentType>(request.Type, "type");
        var parsedApprovalStatus = ParseEnum<ParcelCustodyExceptionRequestStatus>(
            request.ApprovalStatus,
            "approvalStatus");
        if (!string.IsNullOrWhiteSpace(request.SlaState)
            && request.SlaState.ToUpperInvariant() is not ("NOT_STARTED" or "ON_TRACK" or "DUE_SOON" or "BREACHED" or "CLOSED"))
            throw new CodedValidationException("VALIDATION_ERROR", "slaState is invalid.");
        var toExclusive = request.To?.AddTicks(1);
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

        var page = await _reliability.SearchIncidentsByOperatorAsync(
            request.OperatorId,
            parsedStatus,
            parsedType,
            request.Search,
            matchedUserIds,
            request.TripId,
            request.AssigneeId,
            request.SlaState,
            parsedApprovalStatus,
            request.From,
            toExclusive,
            now,
            request.Page,
            request.PageSize,
            cancellationToken);
        if (page.Items.Count == 0)
            return PagedResult<ParcelIncidentListItem>.Create([], page.Page, page.PageSize, page.TotalItems);

        var incidentIds = page.Items.Select(incident => incident.Id).ToArray();
        var pendingCustodyApprovals = await _custodyExceptionRequests.ListPendingIncidentIdsAsync(
            incidentIds,
            cancellationToken);
        var parcelIds = page.Items.Select(incident => incident.ParcelId).Distinct().ToArray();
        var parcels = await _parcels.ListByIdsAsync(parcelIds, cancellationToken);
        var parcelById = parcels.ToDictionary(parcel => parcel.Id);
        var tasks = await _reliability.ListSearchTasksByIncidentsAsync(incidentIds, cancellationToken);
        var custody = await _reliability.ListCurrentCustodiesAsync(parcelIds, cancellationToken);
        var claims = await _reliability.ListLatestClaimsByParcelsAsync(parcelIds, cancellationToken);
        var taskByIncident = tasks.ToLookup(task => task.IncidentId);
        var custodyByParcel = custody.ToDictionary(item => item.ParcelId);
        var claimByParcel = claims.ToDictionary(claim => claim.ParcelId);

        var tripIds = parcels.Select(parcel => parcel.TripId)
            .Concat(page.Items.Where(incident => incident.TripId.HasValue).Select(incident => incident.TripId!.Value))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var tripOutcome = await _trips.GetTripSummariesAsync(tripIds, cancellationToken);
        var tripById = tripOutcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? tripOutcome.Summaries.ToDictionary(trip => trip.TripId)
            : new Dictionary<Guid, TripSummarySnapshot>();

        var userIds = page.Items.Where(incident => incident.ReporterId.HasValue).Select(incident => incident.ReporterId!.Value)
            .Concat(tasks.Where(task => task.AssigneeId.HasValue).Select(task => task.AssigneeId!.Value))
            .Concat(parcels.Select(parcel => parcel.SenderUserId))
            .Concat(parcels.Where(parcel => parcel.RecipientUserId.HasValue).Select(parcel => parcel.RecipientUserId!.Value))
            .Distinct()
            .Take(100)
            .ToArray();
        var identityOutcome = identityRequestUsedForSearch
            ? IdentityUserBatchOutcome.Success([])
            : await _identity.GetUsersAsync(userIds, cancellationToken);
        var userById = identityOutcome.Kind == IdentityUserBatchOutcomeKind.Success
            ? identityOutcome.Users.ToDictionary(user => user.Id)
            : new Dictionary<Guid, IdentityUserSummary>();

        var items = page.Items.Select(incident =>
        {
            parcelById.TryGetValue(incident.ParcelId, out var parcel);
            var incidentTasks = taskByIncident[incident.Id].ToArray();
            claimByParcel.TryGetValue(incident.ParcelId, out var claim);
            ReliabilityTripResponse? trip = null;
            if (parcel is not null)
            {
                tripById.TryGetValue(parcel.TripId, out var tripSnapshot);
                trip = ParcelReliabilityReadModelService.MapTrip(parcel, tripSnapshot);
            }
            custodyByParcel.TryGetValue(incident.ParcelId, out var current);
            var assignees = incidentTasks
                .Where(task => task.AssigneeId.HasValue)
                .Select(task => task.AssigneeId!.Value)
                .Distinct()
                .Select(id => MapUser(id, userById, null))
                .ToArray();
            var remaining = incident.SearchDeadline.HasValue
                ? (long)Math.Ceiling((incident.SearchDeadline.Value - now).TotalMinutes)
                : 0;
            return new ParcelIncidentListItem(
                incident.Id,
                incident.ParcelId,
                incident.OperatorId,
                incident.Type.ToString(),
                incident.Status.ToString(),
                incident.TripId,
                incident.LastKnownLocation,
                pendingCustodyApprovals.Contains(incident.Id) ? null : incident.SearchDeadline,
                incident.CreatedAt,
                incident.OperatorProcessBreach,
                parcel is null ? null : MapParcel(parcel),
                trip,
                parcel is null || trip is null ? null : ParcelReliabilityReadModelService.MapDropoff(parcel, trip),
                ParcelReliabilityReadModelService.MapCustody(current),
                incident.ReporterId.HasValue
                    ? MapUser(incident.ReporterId.Value, userById, incident.ReporterSource)
                    : new OperatorUserSummaryResponse(null, null, null, null, null, incident.ReporterSource),
                new ParcelIncidentTaskSummaryResponse(
                    incidentTasks.Count(task => task.Status == ParcelSearchTaskStatus.COMPLETED),
                    incidentTasks.Length,
                    assignees),
                parcel is null ? null : ParcelReliabilityReadModelService.MapClaim(claim, parcel, now),
                pendingCustodyApprovals.Contains(incident.Id) || !incident.SearchDeadline.HasValue
                    ? null
                    : new ParcelIncidentSlaResponse(
                        incident.SearchDeadline.Value,
                        remaining,
                        ParcelReliabilityReadModelService.MapIncident(incident, now)!.SlaState),
                pendingCustodyApprovals.Contains(incident.Id)
                    ? ["APPROVE", "REJECT"]
                    : ParcelReliabilityActionResolver.Operator(incident, claim, now));
        }).ToArray();

        return PagedResult<ParcelIncidentListItem>.Create(items, page.Page, page.PageSize, page.TotalItems);
    }

    internal static ReliabilityParcelSummaryResponse MapParcel(VietRide.Parcel.Domain.Entities.Parcel parcel)
        => new(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.Description,
            parcel.PhotoUrl,
            parcel.Quantity,
            parcel.DeclaredValueVnd);

    internal static OperatorUserSummaryResponse MapUser(
        Guid userId,
        IReadOnlyDictionary<Guid, IdentityUserSummary> users,
        string? source)
        => users.TryGetValue(userId, out var user)
            ? new OperatorUserSummaryResponse(
                user.Id,
                user.DisplayName,
                user.Phone,
                user.Email,
                user.AvatarUrl,
                source)
            : new OperatorUserSummaryResponse(userId, null, null, null, null, source);

    private static TEnum? ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse(value, true, out TEnum parsed) || !Enum.IsDefined(parsed))
            throw new CodedValidationException("VALIDATION_ERROR", $"{field} is invalid.");
        return parsed;
    }
}
