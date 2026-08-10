using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed class ListOperatorTripsQueryHandler
    : IRequestHandler<ListOperatorTripsQuery, PagedResult<OperatorTripListItemDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    private readonly ITripRepository trips;
    private readonly IIdentityInternalClient identity;

    public ListOperatorTripsQueryHandler(
        ITripRepository trips,
        IIdentityInternalClient identity)
    {
        this.trips = trips;
        this.identity = identity;
    }

    public async Task<PagedResult<OperatorTripListItemDto>> Handle(
        ListOperatorTripsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page ?? DefaultPage, DefaultPage);
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaximumPageSize);
        var routeSearch = NormalizeSearch(request.Search);
        var plateSearch = NormalizePlateSearch(routeSearch);
        DateTimeOffset? fromUtc = request.From.HasValue
            ? BusinessTime.ToUtc(request.From.Value, TimeOnly.MinValue)
            : null;
        DateTimeOffset? toUtc = request.To.HasValue
            ? BusinessTime.ToUtc(request.To.Value.AddDays(1), TimeOnly.MinValue)
            : null;
        var sortDescending = !string.Equals(request.SortDir, "asc", StringComparison.OrdinalIgnoreCase);

        var rows = await trips.ListOperatorTripsAsync(
            request.OperatorId,
            page,
            pageSize,
            routeSearch,
            plateSearch,
            ToDomainStatus(request.Status),
            fromUtc,
            toUtc,
            sortDescending,
            cancellationToken);

        var crewIds = rows.Items
            .SelectMany(row => row.AssistantUserId.HasValue
                ? new[] { row.DriverUserId, row.AssistantUserId.Value }
                : new[] { row.DriverUserId })
            .Distinct()
            .ToArray();
        var crewProfiles = await identity.GetUsersAsync(crewIds, cancellationToken);
        var items = rows.Items.Select(row => ToDto(row, crewProfiles)).ToArray();

        return PagedResult<OperatorTripListItemDto>.Create(
            items,
            rows.Page,
            rows.PageSize,
            rows.TotalItems);
    }

    private static OperatorTripListItemDto ToDto(
        OperatorTripListRow row,
        IReadOnlyDictionary<Guid, IdentityUserProfile> crewProfiles)
        => new(
            row.TripId,
            row.Status.ToString(),
            new OperatorTripRouteDto(
                row.RouteId,
                row.RouteName,
                row.OriginName,
                row.DestinationName),
            new OperatorTripVehicleDto(
                row.VehicleId,
                row.LicensePlate,
                row.VehicleStatus.ToString()),
            ToCrew(row.DriverUserId, crewProfiles),
            row.AssistantUserId.HasValue
                ? ToCrew(row.AssistantUserId.Value, crewProfiles)
                : null,
            row.DepartureAt,
            row.ArrivalEstimate,
            TripVehicleSubstitutionPolicy.CanSubstitute(row.Status),
            row.SourceScheduleId);

    private static OperatorTripCrewDto? ToCrew(
        Guid userId,
        IReadOnlyDictionary<Guid, IdentityUserProfile> crewProfiles)
        => crewProfiles.TryGetValue(userId, out var profile)
            ? new OperatorTripCrewDto(profile.Id, profile.DisplayName, profile.Phone)
            : null;

    private static string? NormalizeSearch(string? search)
        => string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    private static string? NormalizePlateSearch(string? search)
    {
        if (search is null)
        {
            return null;
        }

        var normalized = new string(search
            .Where(character => character is >= '0' and <= '9'
                or >= 'A' and <= 'Z'
                or >= 'a' and <= 'z')
            .Select(char.ToUpperInvariant)
            .ToArray());
        return normalized.Length == 0 ? null : normalized;
    }

    private static TripStatus? ToDomainStatus(OperatorTripStatusFilter? status)
        => status switch
        {
            null => null,
            OperatorTripStatusFilter.SCHEDULED => TripStatus.SCHEDULED,
            OperatorTripStatusFilter.BOARDING => TripStatus.BOARDING,
            OperatorTripStatusFilter.IN_PROGRESS => TripStatus.IN_PROGRESS,
            OperatorTripStatusFilter.COMPLETED => TripStatus.COMPLETED,
            OperatorTripStatusFilter.CANCELLED => TripStatus.CANCELLED,
            OperatorTripStatusFilter.DISRUPTED => TripStatus.DISRUPTED,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
}
