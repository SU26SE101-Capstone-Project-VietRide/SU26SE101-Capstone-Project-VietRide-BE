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

    public AvailableTripsQueryHandler(
        ITripServiceClient tripClient,
        IIdentityServiceClient identityClient,
        IParcelRouteFareRepository fareRepository)
    {
        _tripClient = tripClient;
        _identityClient = identityClient;
        _fareRepository = fareRepository;
    }

    public async Task<PagedResult<AvailableTripResponse>> Handle(
        AvailableTripsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Page must be >= 1.");
        if (request.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "PageSize must be between 1 and 100.");

        if (!Enum.TryParse<ParcelSizeCategory>(request.SizeCategory, ignoreCase: true, out var sizeCategory))
            throw new CodedValidationException(
                "INVALID_SIZE_CATEGORY",
                $"'{request.SizeCategory}' is not a valid ParcelSizeCategory.");

        var searchOutcome = await _tripClient.SearchAvailableParcelTripsAsync(
            request.OriginStationId,
            request.DestinationStationId,
            request.DepartureDate,
            request.EstimatedWeightKg,
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

            responseItems.Add(new AvailableTripResponse(
                trip.TripId,
                trip.RouteId,
                operatorName,
                trip.DepartureDateTime,
                trip.AvailableCargoWeightKg,
                fare.PriceVnd.Amount));
        }

        return PagedResult<AvailableTripResponse>.Create(
            responseItems,
            request.Page,
            request.PageSize,
            searchOutcome.TotalItems);
    }
}
