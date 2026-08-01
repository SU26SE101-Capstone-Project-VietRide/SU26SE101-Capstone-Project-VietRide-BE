namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed record OperatorParcelUserResponse(
    Guid? UserId,
    string? DisplayName,
    string? Phone);
