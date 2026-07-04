using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.TrackingAuthorization;

public sealed class GetParcelTrackingAuthorizationQueryHandler
    : IRequestHandler<GetParcelTrackingAuthorizationQuery, ParcelTrackingAuthorizationResponse>
{
    private static readonly ParcelStatus[] TrackableStatuses =
    [
        ParcelStatus.PENDING,
        ParcelStatus.LOADED,
        ParcelStatus.IN_TRANSIT,
        ParcelStatus.UNLOADED,
        ParcelStatus.DELIVERED_PENDING_CONFIRM,
        ParcelStatus.DELIVERY_CONFIRMED,
        ParcelStatus.DELIVERY_REJECTED,
        ParcelStatus.PENDING_OPERATOR_ACTION,
        ParcelStatus.PENDING_TRANSFER_CONFIRM,
        ParcelStatus.TRANSFER_ESCALATED,
    ];

    private readonly IParcelRepository parcelRepository;

    public GetParcelTrackingAuthorizationQueryHandler(IParcelRepository parcelRepository)
    {
        this.parcelRepository = parcelRepository;
    }

    public async Task<ParcelTrackingAuthorizationResponse> Handle(
        GetParcelTrackingAuthorizationQuery request,
        CancellationToken cancellationToken)
    {
        var role = request.Role?.Trim().ToUpperInvariant();
        var parcels = await parcelRepository.QueryNoTracking()
            .Where(parcel => parcel.TripId == request.TripId && TrackableStatuses.Contains(parcel.Status))
            .ToArrayAsync(cancellationToken);

        if (role is "OPERATOR_ADMIN" or "OPERATOR_STAFF")
        {
            var operatorAllowed = request.OperatorId.HasValue
                && parcels.Any(parcel => parcel.OperatorId == request.OperatorId.Value);
            return operatorAllowed
                ? new ParcelTrackingAuthorizationResponse(true, "OPERATOR")
                : new ParcelTrackingAuthorizationResponse(false, Error: "ACCESS_DENIED");
        }

        if (role == "PASSENGER" && request.UserId.HasValue)
        {
            if (parcels.Any(parcel => parcel.SenderUserId == request.UserId.Value))
            {
                return new ParcelTrackingAuthorizationResponse(true, "PARCEL_SENDER");
            }

            if (parcels.Any(parcel => parcel.RecipientUserId == request.UserId.Value))
            {
                return new ParcelTrackingAuthorizationResponse(true, "PARCEL_RECIPIENT");
            }
        }

        return new ParcelTrackingAuthorizationResponse(false, Error: "ACCESS_DENIED");
    }
}
