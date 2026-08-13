namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Query-string parameters for GET /v1/admin/vouchers.
/// Bound from <c>?fundingType=...&amp;isActive=...&amp;page=1&amp;pageSize=20&amp;sortBy=createdAt&amp;sortDir=desc</c>.
/// </summary>
public sealed class ListVouchersRequest
{
    /// <summary>VIETRIDE_FUNDED or OPERATOR_FUNDED. Null = no filter.</summary>
    public string? FundingType { get; init; }

    public bool? IsActive { get; init; }

    public string? Search { get; init; }

    public string? Service { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string? SortBy { get; init; }

    public string SortDir { get; init; } = "desc";
}
