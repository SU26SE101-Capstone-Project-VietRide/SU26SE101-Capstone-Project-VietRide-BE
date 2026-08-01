namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record PaymentRedirectLookupReference(
    string ReferenceType,
    Guid ReferenceId);
