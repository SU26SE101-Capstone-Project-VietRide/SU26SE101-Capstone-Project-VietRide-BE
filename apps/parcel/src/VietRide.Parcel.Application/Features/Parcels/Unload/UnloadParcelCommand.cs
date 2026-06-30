using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.Unload;

public sealed record UnloadParcelCommand(Guid ParcelId, Guid OperatorId) : IRequest<UnloadParcelResponse>;
