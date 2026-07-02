using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.MarkLoaded;

public sealed record MarkParcelLoadedCommand(Guid ParcelId, Guid TripId, string ParcelCode, Guid? LoadedByUserId) : IRequest<MarkParcelLoadedResponse>;
