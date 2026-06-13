namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// HTTP request body for POST /v1/bookings/{bookingId}/edit-dropoff.
/// Shape per VietRide_API_Contract_v1.md lines 870-874.
/// </summary>
public sealed class EditDropoffRequest
{
    public DropoffRequest? Dropoff { get; init; }
}
