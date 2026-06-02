namespace VietRide.Identity.Application.Abstractions.ExternalClients;

/// <summary>
/// Forward-compat DTO for parcel delivery link emails.
/// Fields are populated by Parcel Service (Day 26+).
/// Declared here to satisfy <see cref="IEmailService.SendParcelDeliveryLinkAsync"/> signature.
/// </summary>
public sealed record ParcelDeliveryEmailDto(
    string SenderName,
    string RecipientName,
    string OriginStationName,
    string DestinationStationName,
    string TripDepartureTime,
    string? OperatorName,
    string ExpiresAt);
