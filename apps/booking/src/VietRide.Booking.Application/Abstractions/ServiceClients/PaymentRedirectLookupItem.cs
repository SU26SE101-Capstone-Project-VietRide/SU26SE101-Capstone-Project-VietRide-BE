namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public sealed record PaymentRedirectLookupItem(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId,
    long Amount,
    DateTimeOffset DueAt,
    string PaymentRedirectUrl);
