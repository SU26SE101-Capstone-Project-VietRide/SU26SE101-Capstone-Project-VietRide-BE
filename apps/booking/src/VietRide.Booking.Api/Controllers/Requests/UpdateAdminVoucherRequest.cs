namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Request body for PATCH /v1/admin/vouchers/{id} (partial update — all fields optional).
/// </summary>
public sealed class UpdateAdminVoucherRequest
{
    public string? Name { get; init; }

    public long? Value { get; init; }

    public long? MinOrderAmount { get; init; }

    public long? MaxDiscountAmount { get; init; }

    public int? TotalUsageLimit { get; init; }

    public int? PerUserLimit { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidUntil { get; init; }

    public bool? NewUserOnly { get; init; }

    public IReadOnlyList<string>? ApplicablePaymentMethods { get; init; }

    public IReadOnlyList<string>? ApplicableServices { get; init; }

    public IReadOnlyList<Guid>? ApplicableRouteIds { get; init; }
}
