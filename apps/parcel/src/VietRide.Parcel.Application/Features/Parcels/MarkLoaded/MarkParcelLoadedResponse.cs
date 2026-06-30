namespace VietRide.Parcel.Application.Features.Parcels.MarkLoaded;

public sealed record MarkParcelLoadedResponse(Guid ParcelId, string ParcelCode, string Status);
