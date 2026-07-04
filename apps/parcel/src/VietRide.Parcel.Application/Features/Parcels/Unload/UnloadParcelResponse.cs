namespace VietRide.Parcel.Application.Features.Parcels.Unload;

public sealed record UnloadParcelResponse(Guid ParcelId, string ParcelCode, string Status);
