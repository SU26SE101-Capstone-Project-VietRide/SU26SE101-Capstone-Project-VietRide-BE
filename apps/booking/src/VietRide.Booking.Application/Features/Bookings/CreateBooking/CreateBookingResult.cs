namespace VietRide.Booking.Application.Features.Bookings.CreateBooking;

using System.Text.Json.Serialization;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Bookings.History;

/// <summary>
/// Response DTO for POST /v1/bookings (201 Created).
/// Shape per VietRide_API_Contract_v1.md lines 706-721.
/// </summary>
public sealed record CreateBookingResult(
    Guid BookingId,
    string BookingCode,
    string Status,
    long TotalAmount,
    long DiscountAmount,
    Guid? PaymentId,
    string? PaymentRedirectUrl,
    IReadOnlyList<CreateBookingTicketResult> Tickets,
    string? PaymentReturnMode = null,
    [property: JsonPropertyName("vnpaySdk")] VnPaySdkMetadata? VnPaySdk = null,
    BookingHistoryVehicleDto? Vehicle = null);

public sealed record CreateBookingTicketResult(
    Guid TicketId,
    string TicketCode,
    string SeatNumber,
    string Status,
    long FareAmount,
    long DiscountAmount,
    long PaidAmount);
