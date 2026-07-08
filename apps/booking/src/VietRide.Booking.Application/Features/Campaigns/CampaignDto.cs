namespace VietRide.Booking.Application.Features.Campaigns;

public sealed record CampaignDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset CreatedAt);
