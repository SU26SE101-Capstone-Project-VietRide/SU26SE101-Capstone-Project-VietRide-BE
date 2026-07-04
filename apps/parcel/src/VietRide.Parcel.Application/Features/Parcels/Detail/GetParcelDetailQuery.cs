using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.Detail;

public sealed record GetParcelDetailQuery(Guid ParcelId, Guid? UserId, Guid? OperatorId, bool SkipAuthorization = false)
    : IQuery<ParcelDetailResponse>;
