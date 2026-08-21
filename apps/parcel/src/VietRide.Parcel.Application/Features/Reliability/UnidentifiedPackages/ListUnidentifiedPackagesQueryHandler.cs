using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;

public sealed class ListUnidentifiedPackagesQueryHandler
    : IRequestHandler<ListUnidentifiedPackagesQuery, PagedResult<UnidentifiedPackageResponse>>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;

    public ListUnidentifiedPackagesQueryHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        ITripServiceClient trips)
    {
        _reliability = reliability;
        _parcels = parcels;
        _trips = trips;
    }

    public async Task<PagedResult<UnidentifiedPackageResponse>> Handle(
        ListUnidentifiedPackagesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        UnidentifiedParcelPackageStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse(request.Status, true, out UnidentifiedParcelPackageStatus parsed)
                || !Enum.IsDefined(parsed))
                throw new CodedValidationException("VALIDATION_ERROR", "status is invalid.");
            status = parsed;
        }
        var page = await _reliability.ListUnidentifiedPackagesAsync(
            request.OperatorId,
            status,
            request.Search,
            request.TripId,
            request.Page,
            request.PageSize,
            cancellationToken);
        var matchedIds = page.Items.Where(item => item.MatchedParcelId.HasValue)
            .Select(item => item.MatchedParcelId!.Value)
            .Distinct()
            .ToArray();
        var matchedParcels = await _parcels.ListByIdsAsync(matchedIds, cancellationToken);
        var parcelById = matchedParcels.ToDictionary(parcel => parcel.Id);
        var tripIds = page.Items.Where(item => item.TripId.HasValue).Select(item => item.TripId!.Value)
            .Distinct()
            .ToArray();
        var tripOutcome = await _trips.GetTripSummariesAsync(tripIds, cancellationToken);
        var tripById = tripOutcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? tripOutcome.Summaries.ToDictionary(trip => trip.TripId)
            : new Dictionary<Guid, TripSummarySnapshot>();
        var items = page.Items.Select(package =>
        {
            var trip = package.TripId.HasValue && tripById.TryGetValue(package.TripId.Value, out var snapshot)
                ? ParcelReliabilityReadModelService.MapTrip(snapshot)
                : null;
            var matched = package.MatchedParcelId.HasValue
                ? parcelById.GetValueOrDefault(package.MatchedParcelId.Value)
                : null;
            return UnidentifiedPackageReadModelMapper.Map(package, trip, matched);
        }).ToArray();
        return PagedResult<UnidentifiedPackageResponse>.Create(items, page.Page, page.PageSize, page.TotalItems);
    }
}
