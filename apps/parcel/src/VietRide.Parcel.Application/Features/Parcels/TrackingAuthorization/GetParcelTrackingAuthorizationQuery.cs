using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.TrackingAuthorization;

public sealed record GetParcelTrackingAuthorizationQuery(
    Guid TripId,
    Guid? UserId,
    string? Role,
    Guid? OperatorId) : IRequest<ParcelTrackingAuthorizationResponse>;
