using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.InternalDetail;

public sealed record GetInternalParcelQuery(Guid ParcelId) : IQuery<InternalParcelResponse>;
