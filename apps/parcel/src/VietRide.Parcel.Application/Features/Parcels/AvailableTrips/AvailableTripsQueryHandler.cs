using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Security;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Quotes;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed class AvailableTripsQueryHandler
    : IRequestHandler<AvailableTripsQuery, PagedResult<AvailableTripResponse>>
{
    private readonly ITripServiceClient _tripClient;
    private readonly IIdentityServiceClient _identityClient;
    private readonly ParcelQuoteService _quoteService;
    private readonly IClock _clock;

    public AvailableTripsQueryHandler(
        ITripServiceClient tripClient,
        IIdentityServiceClient identityClient,
        IParcelRouteFareRepository fareRepository,
        IParcelPricingPolicyRepository? policyRepository = null,
        IParcelQuoteTokenService? quoteTokenService = null,
        IClock? clock = null)
    {
        _tripClient = tripClient;
        _identityClient = identityClient;
        _quoteService = new ParcelQuoteService(fareRepository, policyRepository, quoteTokenService);
        _clock = clock ?? new SystemClock();
    }

    public async Task<PagedResult<AvailableTripResponse>> Handle(
        AvailableTripsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Page must be >= 1.");
        if (request.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "PageSize must be between 1 and 100.");

        var now = _clock.UtcNow;
        var dimFactor = await _quoteService.GetDimWeightFactorAsync(now, cancellationToken);
        var estimate = ParcelCargoCalculator.Calculate(
            request.LengthCm,
            request.WidthCm,
            request.HeightCm,
            request.EstimatedWeightKg,
            dimFactor);
        var sizeCategory = ParcelCargoCalculator.DeriveSizeCategory(estimate.ChargeableWeightKg);

        var eligibleRouteIds = await _quoteService.ListEligibleRouteIdsAsync(
            sizeCategory,
            now,
            cancellationToken);
        if (eligibleRouteIds.Count == 0)
        {
            return PagedResult<AvailableTripResponse>.Create(
                [],
                request.Page,
                request.PageSize,
                0);
        }

        var searchOutcome = request.DestinationStationId.HasValue
            ? await _tripClient.SearchAvailableParcelTripsForRoutesAsync(
                request.OriginStationId,
                request.DestinationStationId.Value,
                request.DepartureDate,
                estimate.WeightKg,
                estimate.VolumeM3,
                sizeCategory,
                eligibleRouteIds,
                request.Page,
                request.PageSize,
                cancellationToken)
            : await _tripClient.SearchAvailableParcelTripsForRoutesAsync(
                new ParcelTripAvailabilityFilter(
                    request.OriginStationId,
                    null,
                    request.DropoffStopId,
                    request.DestinationProvinceCode,
                    request.DestinationLocationCode),
                request.DepartureDate,
                estimate.WeightKg,
                estimate.VolumeM3,
                sizeCategory,
                eligibleRouteIds,
                request.Page,
                request.PageSize,
                cancellationToken);

        if (searchOutcome.Kind == ParcelTripSearchOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SEARCH_UNAVAILABLE",
                searchOutcome.ErrorMessage ?? "Trip search service returned transport error.");
        }

        var trips = searchOutcome.Trips ?? [];
        var fares = await _quoteService.LoadActiveFaresAsync(
            trips.Select(trip => trip.RouteId).Distinct().ToArray(),
            sizeCategory,
            now,
            cancellationToken);
        var responseItems = new List<AvailableTripResponse>();
        foreach (var trip in trips)
        {
            if (!fares.TryGetValue(trip.RouteId, out var fare))
            {
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SEARCH_UNAVAILABLE",
                    "Trip search returned a route outside the fare-eligible filter.");
            }

            var operatorName = trip.OperatorName;
            if (string.IsNullOrWhiteSpace(operatorName))
            {
                var opOutcome = await _identityClient.GetOperatorInfoAsync(trip.OperatorId, cancellationToken);
                switch (opOutcome.Kind)
                {
                    case OperatorLookupOutcomeKind.Success:
                        operatorName = opOutcome.OperatorInfo!.Name;
                        break;
                    case OperatorLookupOutcomeKind.OperatorNotFound:
                        throw new CodedNotFoundException("OPERATOR_NOT_FOUND",
                            $"Operator with id '{trip.OperatorId}' not found.");
                    default:
                        throw new ParcelDependencyUnavailableException("OPERATOR_LOOKUP_UNAVAILABLE",
                            opOutcome.ErrorMessage ?? $"Operator lookup failed with {opOutcome.Kind}.");
                }
            }

            var quote = _quoteService.Calculate(estimate, fare, 0, dimFactor);
            var issuedQuote = _quoteService.IssueToken(
                quote,
                request.SenderUserId,
                trip.TripId,
                trip.RouteId,
                trip.OperatorId,
                trip.OriginStation.Id,
                trip.DestinationStation.Id,
                now);

            responseItems.Add(new AvailableTripResponse(
                trip.TripId,
                trip.RouteId,
                trip.Status,
                trip.OperatorId,
                operatorName,
                trip.OriginStation,
                trip.DestinationStation,
                trip.DepartureDateTime,
                trip.EstimatedArrivalTime,
                quote.EstimatedTotalPriceVnd,
                quote.DepositPercent,
                quote.EstimatedDepositVnd,
                issuedQuote?.Token,
                issuedQuote?.ExpiresAt,
                quote.SizeCategory.ToString(),
                quote.EstimatedGrossPriceVnd,
                quote.EstimatedDiscountVnd)
            {
                AvailableCargoWeightKg = trip.AvailableCargoWeightKg,
                AvailableCargoVolumeM3 = trip.AvailableCargoVolumeM3,
                DropoffPoints = trip.DropoffPoints ?? [],
            });
        }

        return PagedResult<AvailableTripResponse>.Create(
            responseItems,
            request.Page,
            request.PageSize,
            searchOutcome.TotalItems);
    }
}
