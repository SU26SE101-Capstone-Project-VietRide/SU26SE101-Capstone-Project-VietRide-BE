namespace VietRide.Booking.Application.Features.Bookings.EditDropoff;

/// <summary>
/// Response DTO for POST /v1/bookings/{bookingId}/edit-dropoff.
/// Shape per VietRide_API_Contract_v1.md lines 877-886.
/// </summary>
public sealed record EditDropoffResult(
    Guid BookingId,
    EditDropoffResult.DropoffDto Dropoff,
    long FareDelta)
{
    public sealed record DropoffDto(Guid? StationId, Guid? StopId);
}
