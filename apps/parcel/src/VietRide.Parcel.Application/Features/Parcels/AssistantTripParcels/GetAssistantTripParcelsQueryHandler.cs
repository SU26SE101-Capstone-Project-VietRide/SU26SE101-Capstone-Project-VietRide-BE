using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed class GetAssistantTripParcelsQueryHandler
    : IRequestHandler<GetAssistantTripParcelsQuery, PagedResult<AssistantTripParcelResponse>>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;

    public GetAssistantTripParcelsQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
    }

    public async Task<PagedResult<AssistantTripParcelResponse>> Handle(
        GetAssistantTripParcelsQuery query,
        CancellationToken cancellationToken)
    {
        var authorization = await _tripClient.AuthorizeAssistantForTripAsync(
            query.TripId,
            query.UserId,
            query.OperatorId,
            cancellationToken);

        switch (authorization.Kind)
        {
            case TripCrewAuthorizationOutcomeKind.Authorized:
                break;
            case TripCrewAuthorizationOutcomeKind.Denied:
            case TripCrewAuthorizationOutcomeKind.TripNotFound:
                throw new ForbiddenException("FORBIDDEN", "Caller is not authorized to view parcels for this trip.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }

        var pagedResult = await _parcelRepository.ListByTripAndOperatorAsync(
            query.TripId,
            query.OperatorId,
            query.Page,
            query.PageSize,
            cancellationToken);

        var items = pagedResult.Items.Select(parcel => new AssistantTripParcelResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.RecipientName,
            parcel.RecipientPhone.ToString(),
            parcel.DropoffStopId,
            parcel.SizeCategory.ToString(),
            parcel.EstimatedWeightKg,
            parcel.Description,
            parcel.PhotoUrl)).ToList();

        return PagedResult<AssistantTripParcelResponse>.Create(
            items,
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalItems);
    }
}
