namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public sealed record PaymentRedirectLookupReference(
    string ReferenceType,
    Guid ReferenceId);
