namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Query-string parameters for GET /v1/operator/vouchers.
/// The owner operator is always taken from the authenticated caller claim.
/// </summary>
public sealed class ListOperatorVouchersRequest
{
    public bool? IsActive { get; init; }

    public string? Search { get; init; }

    public string? Service { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string? SortBy { get; init; }

    public string SortDir { get; init; } = "desc";
}
