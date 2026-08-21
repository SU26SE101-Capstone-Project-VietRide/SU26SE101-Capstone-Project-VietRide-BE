namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record IdentityOperatorSummary(
    Guid OperatorId,
    string OperatorName,
    string? LogoUrl,
    string? ContactPhone);
