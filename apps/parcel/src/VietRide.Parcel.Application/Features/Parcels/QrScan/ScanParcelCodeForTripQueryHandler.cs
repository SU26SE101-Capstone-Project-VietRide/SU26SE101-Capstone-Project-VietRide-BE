using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.QrScan;

public sealed class ScanParcelCodeForTripQueryHandler
    : IRequestHandler<ScanParcelCodeForTripQuery, ScanParcelCodeForTripResult>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;

    public ScanParcelCodeForTripQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
    }

    public async Task<ScanParcelCodeForTripResult> Handle(
        ScanParcelCodeForTripQuery query,
        CancellationToken cancellationToken)
    {
        var authorization = await _tripClient.AuthorizeAssistantForTripAsync(
            query.TripId,
            query.AssistantUserId,
            query.OperatorId,
            cancellationToken);

        switch (authorization.Kind)
        {
            case TripCrewAuthorizationOutcomeKind.Authorized:
                break;
            case TripCrewAuthorizationOutcomeKind.Denied:
            case TripCrewAuthorizationOutcomeKind.TripNotFound:
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Caller is not authorized to scan parcels for this trip.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }

        var parcel = await _parcelRepository.FindByParcelCodeAsync(
            query.ParcelCode,
            cancellationToken);

        if (parcel is null
            || parcel.TripId != query.TripId
            || parcel.OperatorId != query.OperatorId)
        {
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel not found.");
        }

        return new ScanParcelCodeForTripResult(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.TripId,
            parcel.RecipientName,
            parcel.SizeCategory.ToString(),
            parcel.PhotoUrl);
    }
}
