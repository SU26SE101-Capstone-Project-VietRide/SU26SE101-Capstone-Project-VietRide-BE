namespace VietRide.Booking.Api.Controllers.Requests;

public sealed class CampaignRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? OwnerOperatorId { get; init; }
    public DateTimeOffset ValidFrom { get; init; }
    public DateTimeOffset ValidUntil { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<Guid> VoucherIds { get; init; } = [];
}
