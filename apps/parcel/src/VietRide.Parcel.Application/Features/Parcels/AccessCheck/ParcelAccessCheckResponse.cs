namespace VietRide.Parcel.Application.Features.Parcels.AccessCheck;

public sealed record ParcelAccessCheckResponse(Guid ParcelId, bool Allowed, string Role);
