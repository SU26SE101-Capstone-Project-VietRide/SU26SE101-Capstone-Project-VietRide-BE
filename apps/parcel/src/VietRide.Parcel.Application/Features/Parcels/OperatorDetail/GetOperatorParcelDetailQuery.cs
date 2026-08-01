using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorDetail;

public sealed record GetOperatorParcelDetailQuery(Guid ParcelId, Guid OperatorId)
    : IQuery<OperatorParcelDetailResponse>;
