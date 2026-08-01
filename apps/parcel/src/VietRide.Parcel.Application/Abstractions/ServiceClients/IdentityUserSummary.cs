namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record IdentityUserSummary(
    Guid Id,
    string DisplayName,
    string? Phone,
    string? Email,
    string? AvatarUrl,
    string Role,
    Guid? OperatorId,
    string Status,
    bool Deleted);
