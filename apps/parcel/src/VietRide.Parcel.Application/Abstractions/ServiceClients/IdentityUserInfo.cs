namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record IdentityUserInfo(
    Guid Id,
    string Role,
    Guid? OperatorId,
    string Status);
