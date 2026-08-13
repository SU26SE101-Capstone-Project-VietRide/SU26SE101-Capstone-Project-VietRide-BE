namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Query-string parameters for GET /v1/operator/bookings.
/// The owning operator is always taken from the authenticated caller claim.
/// </summary>
public sealed class ListOperatorBookingsRequest
{
    public string? Status { get; init; }

    public Guid? TripId { get; init; }

    public DateOnly? Date { get; init; }

    public string? PassengerPhone { get; init; }

    public string? BookingCode { get; init; }

    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string? SortBy { get; init; }

    public string SortDir { get; init; } = "desc";
}
