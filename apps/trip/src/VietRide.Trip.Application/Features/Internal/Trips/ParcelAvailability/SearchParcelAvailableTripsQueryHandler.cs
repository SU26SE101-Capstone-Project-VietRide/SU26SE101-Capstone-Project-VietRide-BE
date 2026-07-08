using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;

public sealed class SearchParcelAvailableTripsQueryHandler
    : IRequestHandler<SearchParcelAvailableTripsQuery, PagedResult<ParcelTripAvailabilityItemDto>>
{
    private readonly IIdentityInternalClient _identityClient;
    private readonly IRouteRepository _routeRepository;
    private readonly ITripRepository _tripRepository;

    public SearchParcelAvailableTripsQueryHandler(
        IRouteRepository routeRepository,
        ITripRepository tripRepository,
        IIdentityInternalClient identityClient)
    {
        _routeRepository = routeRepository;
        _tripRepository = tripRepository;
        _identityClient = identityClient;
    }

    public async Task<PagedResult<ParcelTripAvailabilityItemDto>> Handle(
        SearchParcelAvailableTripsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EstimatedWeightKg <= 0)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "estimatedWeightKg must be greater than 0.",
                [new ValidationError(nameof(request.EstimatedWeightKg), "estimatedWeightKg must be greater than 0.")]);
        }

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var localStart = new DateTimeOffset(request.DepartureDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        var start = localStart.ToUniversalTime();
        var end = localStart.AddDays(1).ToUniversalTime();

        var routes = await _routeRepository.QueryNoTracking()
            .Where(route => route.OriginStationId == request.OriginStationId
                && route.DestinationStationId == request.DestinationStationId
                && route.DeletedAt == null
                && route.IsActive)
            .ToDictionaryAsync(route => route.Id, cancellationToken)
            .ConfigureAwait(false);

        if (routes.Count == 0)
        {
            return PagedResult<ParcelTripAvailabilityItemDto>.Create([], page, pageSize, 0);
        }

        var routeIds = routes.Keys.ToHashSet();
        var tripCandidates = await _tripRepository.QueryNoTracking()
            .Where(trip => routeIds.Contains(trip.RouteId)
                && (trip.Status == TripStatus.SCHEDULED || trip.Status == TripStatus.BOARDING)
                && trip.DepartureDateTime >= start
                && trip.DepartureDateTime < end)
            .OrderBy(trip => trip.DepartureDateTime)
            .ThenBy(trip => trip.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidates = tripCandidates
            .Select(trip => new
            {
                Trip = trip,
                AvailableCargoWeightKg = GetAvailableCargoWeightKg(trip),
            })
            .Where(item => item.AvailableCargoWeightKg >= request.EstimatedWeightKg)
            .ToList();

        var pageItems = candidates
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var operatorNames = await GetOperatorNamesAsync(
            pageItems.Select(item => item.Trip.OperatorId).Distinct().ToArray(),
            cancellationToken);

        var items = pageItems
            .Select(item => new ParcelTripAvailabilityItemDto(
                item.Trip.Id,
                item.Trip.RouteId,
                item.Trip.OperatorId,
                ResolveOperatorName(operatorNames, item.Trip.OperatorId),
                item.Trip.DepartureDateTime,
                item.AvailableCargoWeightKg))
            .ToList();

        return PagedResult<ParcelTripAvailabilityItemDto>.Create(items, page, pageSize, candidates.Count);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetOperatorNamesAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var operatorId in operatorIds)
        {
            var lookup = await _identityClient.GetOperatorAsync(operatorId, cancellationToken);
            if (!lookup.Found || string.IsNullOrWhiteSpace(lookup.Name))
            {
                continue;
            }

            names[operatorId] = lookup.Name;
        }

        return names;
    }

    private static string ResolveOperatorName(IReadOnlyDictionary<Guid, string> operatorNames, Guid operatorId)
        => operatorNames.TryGetValue(operatorId, out var name)
            ? name
            : $"Operator {operatorId:N}";

    private static decimal GetAvailableCargoWeightKg(Domain.Entities.Trip trip)
    {
        if (!trip.MaxCargoWeightKg.HasValue)
        {
            return 0m;
        }

        return Math.Max(0m, trip.MaxCargoWeightKg.Value - trip.ReservedParcelWeightKg - trip.TotalLoadedWeightKg);
    }
}
