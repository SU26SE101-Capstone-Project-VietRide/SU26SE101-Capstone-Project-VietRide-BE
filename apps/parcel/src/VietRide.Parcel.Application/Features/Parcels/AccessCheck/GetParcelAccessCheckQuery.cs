using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.AccessCheck;

public sealed record GetParcelAccessCheckQuery(Guid ParcelId, Guid? UserId, Guid? OperatorId) : IQuery<ParcelAccessCheckResponse>;
