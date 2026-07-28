using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed class AvailableTripsQueryHandler
    : IRequestHandler<AvailableTripsQuery, PagedResult<AvailableTripResponse>>
{
    private readonly ITripServiceClient _tripClient;
    private readonly IIdentityServiceClient _identityClient;
    private readonly IParcelRouteFareRepository _fareRepository;
    private readonly IParcelPricingPolicyRepository? _policyRepository;

    public AvailableTripsQueryHandler(
        ITripServiceClient tripClient,
        IIdentityServiceClient identityClient,
        IParcelRouteFareRepository fareRepository,
        IParcelPricingPolicyRepository? policyRepository = null)
    {
        _tripClient = tripClient;
        _identityClient = identityClient;
        _fareRepository = fareRepository;
        _policyRepository = policyRepository;
    }

    public async Task<PagedResult<AvailableTripResponse>> Handle(
        AvailableTripsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Page must be >= 1.");
        if (request.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "PageSize must be between 1 and 100.");

        var now = DateTimeOffset.UtcNow;
        var dimFactor = _policyRepository is null
            ? ParcelCargoCalculator.DefaultDimWeightFactor
            : await _policyRepository.GetSystemDecimalAsync(
                "DIM_WEIGHT_FACTOR",
                ParcelCargoCalculator.DefaultDimWeightFactor,
                now,
                cancellationToken);
        var estimate = ParcelCargoCalculator.Calculate(
            request.LengthCm,
            request.WidthCm,
            request.HeightCm,
            request.EstimatedWeightKg,
            dimFactor);
        var sizeCategory = ParcelCargoCalculator.DeriveSizeCategory(estimate.ChargeableWeightKg);

        var searchOutcome = _policyRepository is null
            ? await _tripClient.SearchAvailableParcelTripsAsync(
                request.OriginStationId,
                request.DestinationStationId,
                request.DepartureDate,
                estimate.WeightKg,
                sizeCategory,
                request.Page,
                request.PageSize,
                cancellationToken)
            : await _tripClient.SearchAvailableParcelTripsAsync(
                request.OriginStationId,
                request.DestinationStationId,
                request.DepartureDate,
                estimate.WeightKg,
                estimate.VolumeM3,
                sizeCategory,
                request.Page,
                request.PageSize,
                cancellationToken);

        if (searchOutcome.Kind == ParcelTripSearchOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SEARCH_UNAVAILABLE",
                searchOutcome.ErrorMessage ?? "Trip search service returned transport error.");
        }

        var responseItems = new List<AvailableTripResponse>();
        foreach (var trip in searchOutcome.Trips ?? [])
        {
            var fare = await _fareRepository.FindByCompositeAsync(trip.RouteId, sizeCategory, cancellationToken);
            if (fare is null)
            {
                continue;
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

            var totalPrice = _policyRepository is null
                ? fare.PriceVnd
                : ParcelCargoCalculator.CalculateTotalPrice(
                    estimate.ChargeableWeightKg,
                    fare.PricePerChargeableKgVnd.Amount > 0 ? fare.PricePerChargeableKgVnd : fare.PriceVnd,
                    fare.MinimumPriceVnd);
            var defaultDepositPercent = _policyRepository is null
                ? ParcelCargoCalculator.DefaultDepositPercent
                : await _policyRepository.GetSystemDecimalAsync(
                    "DEFAULT_DEPOSIT_PERCENT",
                    ParcelCargoCalculator.DefaultDepositPercent,
                    now,
                    cancellationToken);
            var depositPercent = _policyRepository is null
                ? defaultDepositPercent
                : await _policyRepository.GetDepositPercentAsync(
                    trip.OperatorId,
                    trip.RouteId,
                    defaultDepositPercent,
                    now,
                    cancellationToken);
            var depositAmount = ParcelCargoCalculator.CalculatePercent(totalPrice, depositPercent);

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
                totalPrice.Amount,
                depositPercent,
                depositAmount.Amount)
            {
                AvailableCargoWeightKg = trip.AvailableCargoWeightKg,
                AvailableCargoVolumeM3 = trip.AvailableCargoVolumeM3,
            });
        }

        return PagedResult<AvailableTripResponse>.Create(
            responseItems,
            request.Page,
            request.PageSize,
            searchOutcome.TotalItems);
    }
}
